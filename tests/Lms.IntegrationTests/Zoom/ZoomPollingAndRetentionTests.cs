using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Zoom;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.LiveSessions.LiveSessionTestKit;

namespace Lms.IntegrationTests.Zoom;

/// <summary>M4b Task 7 (#474, D54): the polling fallback, the receipt retention and the VirtualClasses enable guard.</summary>
[Collection(ApiCollection.Name)]
public sealed class ZoomPollingAndRetentionTests(LmsApiFactory factory) : IDisposable
{
    private readonly List<ContentTestHost> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private sealed class CountingSink : ILiveSessionAttendanceSink
    {
        public int Calls;

        public Task SessionEndedAsync(
            Guid sessionId, Guid classCourseId, DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyList<AttendanceParticipant> participants,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.CompletedTask;
        }
    }

    private async Task<(ContentTestHost Host, ContentWorld World, Guid SessionId, CountingSink Sink)> SessionAsync(string name)
    {
        var sink = new CountingSink();
        var host = new ContentTestHost(factory, services => services.AddSingleton<ILiveSessionAttendanceSink>(sink));
        _hosts.Add(host);
        var w = await host.SetUpAsync(name);
        await EnableAsync(factory, w);
        var created = Assert.Single(await CreateAsync(host, w.Teacher, w, Single(name)));
        return (host, w, created.Id, sink);
    }

    private async Task MoveAsync(Guid sessionId, DateTime start)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.Set<LiveSession>().SingleAsync(s => s.Id == sessionId)).Reschedule(start, 60);
        await db.SaveChangesAsync();
    }

    private async Task<string> StatusAsync(Guid sessionId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<LiveSession>().SingleAsync(s => s.Id == sessionId)).Status;
    }

    private static async Task<T> InPlatformScopeAsync<T>(ContentTestHost host, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.Empty, true);
        return await work(scope.ServiceProvider);
    }

    [Fact]
    public async Task Polling_fills_a_missed_webhook_once()
    {
        var (host, w, id, sink) = await SessionAsync("ZP1");
        await MoveAsync(id, DateTime.UtcNow.AddHours(-3)); // it ended two hours ago and no event arrived

        var first = await InPlatformScopeAsync(host, sp => sp.GetRequiredService<ZoomPollingJob>().RunForClientAsync(w.Tree.Org.Id));
        var second = await InPlatformScopeAsync(host, sp => sp.GetRequiredService<ZoomPollingJob>().RunForClientAsync(w.Tree.Org.Id));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(LiveSessionStatuses.Ended, await StatusAsync(id));
        Assert.Equal(1, sink.Calls); // the attendance seam is called once
    }

    [Fact]
    public async Task Polling_leaves_sessions_that_have_not_ended_or_are_older_than_the_lookback_alone()
    {
        var (host, w, id, sink) = await SessionAsync("ZP2");
        var (_, _, old, _) = await SessionAsync("ZP2Old");
        await MoveAsync(old, DateTime.UtcNow.AddDays(-10));

        var changed = await InPlatformScopeAsync(host, sp => sp.GetRequiredService<ZoomPollingJob>().RunForClientAsync(w.Tree.Org.Id));

        Assert.Equal(0, changed);
        Assert.Equal(LiveSessionStatuses.Scheduled, await StatusAsync(id)); // in the future
        Assert.Equal(LiveSessionStatuses.Scheduled, await StatusAsync(old)); // too old to poll (and another client)
        Assert.Equal(0, sink.Calls);
    }

    [Fact]
    public async Task Polling_runs_each_client_in_its_own_context()
    {
        var a = await SessionAsync("ZP3A");
        var b = await SessionAsync("ZP3B");
        await MoveAsync(a.SessionId, DateTime.UtcNow.AddHours(-3));
        await MoveAsync(b.SessionId, DateTime.UtcNow.AddHours(-3));

        var changed = await InPlatformScopeAsync(a.Host, sp => sp.GetRequiredService<ZoomPollingJob>().RunForClientAsync(a.World.Tree.Org.Id));

        Assert.Equal(1, changed);
        Assert.Equal(LiveSessionStatuses.Ended, await StatusAsync(a.SessionId));
        Assert.Equal(LiveSessionStatuses.Scheduled, await StatusAsync(b.SessionId)); // the other client was not part of this run
    }

    [Fact]
    public async Task Receipts_older_than_30_days_are_purged()
    {
        var host = new ContentTestHost(factory);
        _hosts.Add(host);
        var tag = Guid.NewGuid().ToString("N");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.AddRange(
                new ZoomWebhookReceipt("old-" + tag, DateTime.UtcNow.AddDays(-31)),
                new ZoomWebhookReceipt("new-" + tag, DateTime.UtcNow.AddDays(-29)));
            await db.SaveChangesAsync();
        }

        var deleted = await InPlatformScopeAsync(host, sp => sp.GetRequiredService<ZoomReceiptRetentionJob>().RunAsync());

        Assert.True(deleted >= 1);
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var ids = await check.Set<ZoomWebhookReceipt>().Where(r => r.EventId.EndsWith(tag)).Select(r => r.EventId).ToListAsync();
        Assert.Equal(["new-" + tag], ids);
    }

    [Fact]
    public async Task Enabling_the_flag_without_zoom_options_is_409_and_with_the_fake_provider_it_works()
    {
        var zoom = new ContentTestHost(factory, services =>
            services.AddScoped<Lms.Domain.LiveSessions.IMeetingProvider>(sp => sp.GetRequiredService<ZoomMeetingProvider>())); // Zoom, unconfigured
        var fake = new ContentTestHost(factory);
        _hosts.AddRange([zoom, fake]);
        var a = await zoom.SetUpAsync("ZP5A");
        var b = await fake.SetUpAsync("ZP5B");

        var refused = await zoom.SendAsync(a.Admin, HttpMethod.Put, Routes.OrgUnitSetting(a.Tree.Root.Id, FeatureFlags.VirtualClasses.Name), new SetSettingRequest("true", false));
        var off = await zoom.SendAsync(a.Admin, HttpMethod.Put, Routes.OrgUnitSetting(a.Tree.Root.Id, FeatureFlags.VirtualClasses.Name), new SetSettingRequest("false", false));
        var allowed = await fake.SendAsync(b.Admin, HttpMethod.Put, Routes.OrgUnitSetting(b.Tree.Root.Id, FeatureFlags.VirtualClasses.Name), new SetSettingRequest("true", false));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("provider_not_configured", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }
}
