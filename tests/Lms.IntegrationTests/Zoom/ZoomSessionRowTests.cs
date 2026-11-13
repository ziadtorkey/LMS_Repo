using System.Net;
using System.Net.Http.Json;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Zoom;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Lms.IntegrationTests.Content.ContentTestHost;
using static Lms.IntegrationTests.LiveSessions.LiveSessionTestKit;

namespace Lms.IntegrationTests.Zoom;

/// <summary>
/// M4b Task 5 on top of the live session rows: the shared host pool is read through slot columns only, a client holds at most its share
/// of it, and the sync sweep retries failed sessions client by client (D53, FR-VC-01).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ZoomSessionRowTests(LmsApiFactory factory) : IDisposable
{
    /// <summary>The tests share one database and one host pool: each takes its own day (years apart at random) so their slots never meet.</summary>
    private readonly DateTime Start = DateTime.UtcNow.Date.AddDays(30 + Random.Shared.Next(1, 20000)).AddHours(9);

    private readonly List<ContentTestHost> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private ContentTestHost NewHost()
    {
        var host = new ContentTestHost(factory);
        _hosts.Add(host);
        return host;
    }

    private async Task<Guid> AddRowAsync(ContentWorld w, string? host, DateTime start, int minutes = 60, bool cancelled = false, bool deleted = false)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var session = new LiveSession(w.ClassCourse, Lesson1, null, null, "Zoom", start, minutes);
        if (host is not null)
        {
            session.SetHost(host);
        }

        if (cancelled)
        {
            session.Cancel();
        }

        db.Add(session);
        await db.SaveChangesAsync();
        if (deleted)
        {
            db.Remove(session);
            await db.SaveChangesAsync();
        }

        return session.Id;
    }

    [Fact]
    public async Task Allocator_reads_only_slot_columns_and_returns_no_foreign_rows()
    {
        var a = await NewHost().SetUpAsync("ZS1A");
        var b = await NewHost().SetUpAsync("ZS1B");
        await AddRowAsync(a, "h1@pool.test", Start);
        await AddRowAsync(b, "h2@pool.test", Start.AddMinutes(30));
        await AddRowAsync(b, null, Start);                                      // no host yet: not a slot
        await AddRowAsync(b, "h3@pool.test", Start, cancelled: true);           // cancelled: free again
        await AddRowAsync(b, "h4@pool.test", Start, deleted: true);             // deleted: free again
        await AddRowAsync(b, "h5@pool.test", Start.AddDays(2));                 // far outside the window

        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.Tree.Org.Id, a.Tree.Root.Path));
        var slots = await new EfHostSlotStore(db).SlotsAroundAsync(Start.AddMinutes(-10), Start.AddMinutes(70), CancellationToken.None);

        // Another client's booked host is visible (the pool is shared) but only as a slot: no entity, title, link or user.
        Assert.Equal(["h1@pool.test", "h2@pool.test"], slots.Select(s => s.HostId).Order());
        Assert.Contains(slots, s => s.HostId == "h2@pool.test" && s.OrganizationId == b.Tree.Org.Id && s.EndUtc == Start.AddMinutes(90));
        Assert.Equal(["EndUtc", "HostId", "OrganizationId", "StartUtc"], typeof(HostSlot).GetProperties().Select(p => p.Name).Order());
        Assert.Empty(db.ChangeTracker.Entries()); // the projection materialised no LiveSession
    }

    [Fact]
    public async Task A_session_that_started_before_the_window_still_counts_while_it_runs()
    {
        var a = await NewHost().SetUpAsync("ZS2");
        await AddRowAsync(a, "h1@pool.test", Start.AddHours(-2), minutes: 180); // 07:00 to 10:00

        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.Tree.Org.Id, a.Tree.Root.Path));
        var slots = await new EfHostSlotStore(db).SlotsAroundAsync(Start, Start.AddMinutes(60), CancellationToken.None);

        Assert.Single(slots);
    }

    [Fact]
    public async Task Client_share_counts_only_that_clients_slots()
    {
        var a = await NewHost().SetUpAsync("ZS3A");
        var b = await NewHost().SetUpAsync("ZS3B");
        foreach (var host in new[] { "h1@pool.test", "h2@pool.test", "h3@pool.test" })
        {
            await AddRowAsync(b, host, Start); // client B holds three hosts in the window
        }

        var options = Options.Create(new ZoomOptions { HostPool = [.. Enumerable.Range(1, 8).Select(i => $"h{i}@pool.test")], HostBufferMinutes = 10 });
        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.Tree.Org.Id, a.Tree.Root.Path));
        var allocator = new ZoomHostAllocator(options, new InProcessJobKeyLock(), new EfHostSlotStore(db), () => 25); // 25 % of 8 = 2 hosts

        var chosen = new List<string?>();
        for (var i = 0; i < 3; i++)
        {
            var choice = await allocator.AllocateAsync(a.Tree.Org.Id, Start, Start.AddMinutes(60), host => AddRowAsync(a, host, Start));
            chosen.Add(choice.HostId ?? choice.Failure.ToString());
        }

        // B's three hosts are busy for A, but they do not count against A's own share: A gets two hosts, then the share is reached.
        Assert.Equal(["h4@pool.test", "h5@pool.test", nameof(ZoomHostFailure.ClientShareReached)], chosen);
    }

    [Fact]
    public async Task Provider_down_keeps_session_in_sync_failed_and_sweep_retries()
    {
        var host = NewHost();
        var w = await host.SetUpAsync("ZS4");
        await EnableAsync(factory, w);
        Provider(host).FailNext(1);
        var created = Assert.Single(await CreateAsync(host, w.Teacher, w, Single("Retry me")));
        Assert.Equal(LiveSessionStatuses.SyncFailed, created.Status);
        Assert.Empty(Provider(host).Meetings);

        var recovered = await SweepAsync(host, w.Tree.Org.Id);

        Assert.Equal(1, recovered);
        var after = await (await host.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(created.Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Equal(LiveSessionStatuses.Scheduled, after!.Status);
        Assert.Equal("Retry me", Assert.Single(Provider(host).Meetings.Values).Topic);
        Assert.Equal(0, await SweepAsync(host, w.Tree.Org.Id)); // nothing left to retry
    }

    [Fact]
    public async Task Polling_runs_each_client_in_its_own_context()
    {
        var host = NewHost();
        var a = await host.SetUpAsync("ZS5A");
        var b = await host.SetUpAsync("ZS5B");
        await EnableAsync(factory, a);
        await EnableAsync(factory, b);
        Provider(host).FailNext(2);
        var first = Assert.Single(await CreateAsync(host, a.Teacher, a, Single("A live")));
        var second = Assert.Single(await CreateAsync(host, b.Teacher, b, Single("B live")));
        Assert.Equal([LiveSessionStatuses.SyncFailed, LiveSessionStatuses.SyncFailed], [first.Status, second.Status]);

        Provider(host).FailNext(1); // the sweep's first call fails: one client is retried next run, the other still recovers now
        var firstRun = await SweepAsync(host, a.Tree.Org.Id, b.Tree.Org.Id);
        var secondRun = await SweepAsync(host, a.Tree.Org.Id, b.Tree.Org.Id);

        Assert.Equal(1, firstRun);
        Assert.Equal(1, secondRun);
        Assert.Equal(["A live", "B live"], Provider(host).Meetings.Values.Select(m => m.Topic).Order());
        // Each meeting was created for its own client and class course.
        Assert.Contains(Provider(host).Meetings.Values, m => m.OrganizationId == a.Tree.Org.Id && m.ClassCourseId == a.ClassCourse.Id);
        Assert.Contains(Provider(host).Meetings.Values, m => m.OrganizationId == b.Tree.Org.Id && m.ClassCourseId == b.ClassCourse.Id);
    }

    /// <summary>The fake provider, but each create takes a moment, so two sweeps that are not excluded really do overlap.</summary>
    private sealed class SlowProvider(FakeMeetingProvider inner) : IMeetingProvider
    {
        public string Name => inner.Name;

        public async Task<MeetingResult> CreateMeetingAsync(MeetingRequest request, CancellationToken ct = default)
        {
            await Task.Delay(400, ct);
            return await inner.CreateMeetingAsync(request, ct);
        }

        public Task UpdateMeetingAsync(string id, MeetingRequest request, CancellationToken ct = default) => inner.UpdateMeetingAsync(id, request, ct);

        public Task DeleteMeetingAsync(string id, CancellationToken ct = default) => inner.DeleteMeetingAsync(id, ct);

        public Task<IReadOnlyList<MeetingParticipant>> GetParticipantsAsync(string id, CancellationToken ct = default) => inner.GetParticipantsAsync(id, ct);

        public Task<IReadOnlyList<MeetingRecording>> GetRecordingsAsync(string id, CancellationToken ct = default) => inner.GetRecordingsAsync(id, ct);

        public Task<ProviderHealth> GetHealthAsync(CancellationToken ct = default) => inner.GetHealthAsync(ct);
    }

    [Fact]
    public async Task Two_sweeps_at_once_create_one_meeting_per_session()
    {
        var host = new ContentTestHost(factory, services =>
            services.AddSingleton<IMeetingProvider>(sp => new SlowProvider(sp.GetRequiredService<FakeMeetingProvider>())));
        _hosts.Add(host);
        var w = await host.SetUpAsync("ZS9");
        await EnableAsync(factory, w);
        Provider(host).FailNext(2);
        var sessions = await CreateAsync(host, w.Teacher, w, Weekly(2));
        Assert.All(sessions, s => Assert.Equal(LiveSessionStatuses.SyncFailed, s.Status));
        Assert.Empty(Provider(host).Meetings);

        var results = await Task.WhenAll(Task.Run(() => SweepAsync(host, w.Tree.Org.Id)), Task.Run(() => SweepAsync(host, w.Tree.Org.Id)));

        Assert.Equal(2, Provider(host).Meetings.Count); // one meeting per session, not four
        Assert.Equal(2, results.Sum());                 // and exactly one of the runs did the work
        Assert.Contains(0, results);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var rows = await db.Set<LiveSession>().Where(s => s.ClassCourseId == w.ClassCourse.Id).ToListAsync();
        Assert.All(rows, r => Assert.Equal(LiveSessionStatuses.Scheduled, r.Status));
        Assert.Equal(2, rows.Select(r => r.ExternalMeetingId).Distinct().Count());
        Assert.Equal(2, Provider(host).Meetings.Keys.Intersect(rows.Select(r => r.ExternalMeetingId!)).Count()); // no orphaned meeting
    }

    [Fact]
    public async Task The_sweep_leaves_started_and_cancelled_sessions_alone()
    {
        var host = NewHost();
        var w = await host.SetUpAsync("ZS6");
        await EnableAsync(factory, w);
        Provider(host).FailNext(1);
        var created = Assert.Single(await CreateAsync(host, w.Teacher, w, Single("Past", start: DateTime.UtcNow.AddMinutes(30))));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var row = await db.Set<LiveSession>().SingleAsync(s => s.Id == created.Id);
            row.Reschedule(DateTime.UtcNow.AddMinutes(-5), 60); // it has started in the meantime
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await SweepAsync(host, w.Tree.Org.Id));
        Assert.Empty(Provider(host).Meetings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cloud_recording_only_when_unit_setting_enabled(bool enabled)
    {
        var host = NewHost();
        var w = await host.SetUpAsync("ZS7" + enabled);
        await EnableAsync(factory, w);
        if (enabled)
        {
            await SetAsync(factory, w, SettingKeys.LiveCloudRecordingEnabled.Name, "true");
        }

        await CreateAsync(host, w.Teacher, w, Single("Recorded?"));

        Assert.Equal(enabled, Assert.Single(Provider(host).Meetings.Values).CloudRecording);
    }

    [Fact]
    public async Task Teacher_cannot_enable_cloud_recording()
    {
        var host = NewHost();
        var w = await host.SetUpAsync("ZS8");

        var refused = await host.SendAsync(w.Teacher, HttpMethod.Put, Routes.OrgUnitSetting(w.Tree.Root.Id, SettingKeys.LiveCloudRecordingEnabled.Name),
            new SetSettingRequest("true", false));
        var allowed = await host.SendAsync(w.Admin, HttpMethod.Put, Routes.OrgUnitSetting(w.Tree.Root.Id, SettingKeys.LiveCloudRecordingEnabled.Name),
            new SetSettingRequest("true", false));

        Assert.False(refused.IsSuccessStatusCode); // a Teacher holds no Settings.Update
        Assert.Contains(refused.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    /// <summary>Runs the sweep the way the job server does: a fresh scope with the platform context.</summary>
    private static async Task<int> SweepAsync(ContentTestHost host, params Guid[] clients)
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.Empty, true);
        var job = scope.ServiceProvider.GetRequiredService<ZoomSyncSweepJob>();
        if (clients.Length == 0)
        {
            return await job.RunAsync();
        }

        var recovered = 0;
        foreach (var client in clients)
        {
            recovered += await job.RunForClientAsync(client);
        }

        return recovered;
    }
}
