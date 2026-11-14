using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Hangfire;
using Lms.Api.Infrastructure;
using Lms.Domain.Classes;
using Lms.Domain.LiveSessions;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.LiveSessions;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Content.ContentTestHost;
using static Lms.IntegrationTests.LiveSessions.LiveSessionTestKit;

namespace Lms.IntegrationTests.LiveSessions;

/// <summary>M4b Task 6 (#473): reminders, the recording link, the participants, upcoming sessions and the attendance seam.</summary>
[Collection(ApiCollection.Name)]
public sealed class LiveSessionLifecycleTests : IDisposable
{
    private sealed class RecordingSink : ILiveSessionAttendanceSink
    {
        public ConcurrentQueue<(Guid SessionId, IReadOnlyList<AttendanceParticipant> Participants)> Calls { get; } = new();

        public bool Throw { get; set; }

        public Task SessionEndedAsync(
            Guid sessionId, Guid classCourseId, DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyList<AttendanceParticipant> participants,
            CancellationToken ct = default)
        {
            Calls.Enqueue((sessionId, participants));
            return Throw ? throw new InvalidOperationException("attendance is down") : Task.CompletedTask;
        }
    }

    private readonly LmsApiFactory _factory;
    private readonly RecordingSink _sink = new();
    private readonly ContentTestHost _t;

    public LiveSessionLifecycleTests(LmsApiFactory factory)
    {
        _factory = factory;
        _t = new ContentTestHost(factory, services => services.AddSingleton<ILiveSessionAttendanceSink>(_sink));
    }

    public void Dispose() => _t.Dispose();

    /// <summary>The Hangfire job of the reminder (id, state, due time), or null when no reminder is scheduled.</summary>
    private static (string JobId, string State, DateTime? DueUtc)? ReminderOf(Guid sessionId)
    {
        using var connection = JobStorage.Current.GetConnection();
        var mapping = connection.GetAllEntriesFromHash(ScheduledJobKeys.HashKey("live-reminder:" + sessionId));
        if (mapping is null || !mapping.TryGetValue("jobId", out var jobId))
        {
            return null;
        }

        var data = connection.GetStateData(jobId);
        DateTime? due = data is not null && data.Data.TryGetValue("EnqueueAt", out var ms) && long.TryParse(ms, out var millis)
            ? DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime
            : null;
        return (jobId, data?.Name ?? "Missing", due);
    }

    private async Task<ContentWorld> SetUpAsync(string name)
    {
        var w = await _t.SetUpAsync(name);
        await EnableAsync(_factory, w);
        return w;
    }

    private async Task<int> NotificationsAsync(Guid userId)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().CountAsync(n => n.UserId == userId && n.EventKey == NotificationEvents.LiveReminder);
    }

    private async Task RunReminderAsync(Guid sessionId, Guid organizationId)
    {
        await using var scope = _t.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true); // as the job runs: the system context
        await scope.ServiceProvider.GetRequiredService<LiveReminderJob>().RunAsync(sessionId, organizationId);
    }

    private async Task<bool> EndAsync(Guid sessionId)
    {
        await using var scope = _t.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
        return await scope.ServiceProvider.GetRequiredService<LiveSessionLifecycle>().EndedAsync(sessionId, default);
    }

    private async Task<Guid> UserOfAsync(Guid profileId)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<Lms.Domain.People.StudentProfile>().SingleAsync(s => s.Id == profileId)).UserId!.Value;
    }

    [Fact]
    public async Task Reminder_is_scheduled_before_the_start_moves_on_update_and_goes_on_cancel()
    {
        var w = await SetUpAsync("LL1");
        var start = DateTime.UtcNow.AddDays(3);
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: start)));

        var first = ReminderOf(session.Id);
        Assert.NotNull(first);
        Assert.Equal("Scheduled", first!.Value.State);
        Assert.InRange((first.Value.DueUtc!.Value - start.AddMinutes(-30)).TotalSeconds, -5, 5); // Live.ReminderMinutes = 30

        var later = start.AddDays(1);
        await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSession(session.Id), new UpdateLiveSessionRequest(Module(), later, 60));
        var moved = ReminderOf(session.Id);
        Assert.Equal(first.Value.JobId, moved!.Value.JobId); // the same job moved, not a second one
        Assert.InRange((moved.Value.DueUtc!.Value - later.AddMinutes(-30)).TotalSeconds, -5, 5);

        await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LiveSession(session.Id));
        Assert.Null(ReminderOf(session.Id));
    }

    [Fact]
    public async Task Reminder_follows_the_setting_and_a_session_already_inside_the_reminder_time_is_reminded_at_once()
    {
        var w = await SetUpAsync("LL2");
        await SetAsync(_factory, w, SettingKeys.LiveReminderMinutes.Name, "120");
        var start = DateTime.UtcNow.AddDays(2);
        var far = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: start)));
        var soon = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: DateTime.UtcNow.AddMinutes(45))));

        Assert.InRange((ReminderOf(far.Id)!.Value.DueUtc!.Value - start.AddMinutes(-120)).TotalSeconds, -5, 5);
        Assert.InRange((ReminderOf(soon.Id)!.Value.DueUtc!.Value - DateTime.UtcNow).TotalSeconds, -10, 15);
    }

    [Fact]
    public async Task Reminder_is_sent_once_to_the_students_who_see_the_module()
    {
        var w = await SetUpAsync("LL3");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: DateTime.UtcNow.AddHours(2))));
        var hidden = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: DateTime.UtcNow.AddHours(2), visibility: ModuleVisibilities.Hidden)));
        var student = await UserOfAsync(w.StudentProfileId);

        await RunReminderAsync(session.Id, w.Tree.Org.Id);
        await RunReminderAsync(session.Id, w.Tree.Org.Id); // a retry or a duplicate job sends nothing more
        await RunReminderAsync(hidden.Id, w.Tree.Org.Id);

        Assert.Equal(1, await NotificationsAsync(student));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.NotNull((await db.Set<LiveSession>().SingleAsync(s => s.Id == session.Id)).ReminderSentAtUtc);
        Assert.Null((await db.Set<LiveSession>().SingleAsync(s => s.Id == hidden.Id)).ReminderSentAtUtc);
        var row = await db.Set<Notification>().SingleAsync(n => n.UserId == student && n.EventKey == NotificationEvents.LiveReminder);
        Assert.Contains("TitleEn", row.ParametersJson);
        Assert.Contains("StartsAtUtc", row.ParametersJson);
    }

    [Fact]
    public async Task Reminder_is_sent_again_after_the_session_is_rescheduled()
    {
        var w = await SetUpAsync("LL4");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: DateTime.UtcNow.AddHours(2))));
        var student = await UserOfAsync(w.StudentProfileId);
        await RunReminderAsync(session.Id, w.Tree.Org.Id);

        await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSession(session.Id),
            new UpdateLiveSessionRequest(Module(), DateTime.UtcNow.AddHours(5), 60));
        await RunReminderAsync(session.Id, w.Tree.Org.Id);

        Assert.Equal(2, await NotificationsAsync(student));
    }

    [Fact]
    public async Task Reminder_job_with_a_foreign_org_payload_does_nothing()
    {
        var w = await SetUpAsync("LL5");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: DateTime.UtcNow.AddHours(2))));
        var student = await UserOfAsync(w.StudentProfileId);

        await RunReminderAsync(session.Id, Guid.NewGuid());
        await RunReminderAsync(Guid.NewGuid(), w.Tree.Org.Id);

        Assert.Equal(0, await NotificationsAsync(student));
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Null((await db.Set<LiveSession>().SingleAsync(s => s.Id == session.Id)).ReminderSentAtUtc);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://files.example.com/a.mp4")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//example.com/a")]
    [InlineData("not a url")]
    [InlineData("https://example.com/a b")]
    [InlineData("")]
    public async Task Recording_link_rejects_non_http_schemes(string url)
    {
        var w = await SetUpAsync("LL6" + Math.Abs(url.GetHashCode() % 1000));
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));

        var response = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSessionRecording(session.Id), new RecordingLinkRequest(url));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var read = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(session.Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Null(read!.RecordingUrl);
    }

    [Fact]
    public async Task Recording_link_is_attached_as_manual_and_students_see_it_once_the_module_is_visible()
    {
        var w = await SetUpAsync("LL7");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));
        var url = "https://recordings.example.com/r/abc?x=1";

        var response = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSessionRecording(session.Id), new RecordingLinkRequest(url));
        var tooLong = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSessionRecording(session.Id), new RecordingLinkRequest("https://e.com/" + new string('a', 2040)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<LiveSessionDto>())!;
        Assert.Equal(url, dto.RecordingUrl);
        Assert.Equal(RecordingSources.Manual, dto.RecordingSource);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var student = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.LiveSession(session.Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Equal(url, student!.RecordingUrl);
        Assert.True((await ListAsync(_t, w.Student, w)).Single().HasRecording);
    }

    [Fact]
    public async Task Recording_link_needs_manage_the_relationship_the_flag_and_an_open_class()
    {
        var w = await SetUpAsync("LL8");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));
        var body = new RecordingLinkRequest("https://r.example.com/a");

        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Put, Routes.LiveSessionRecording(session.Id), body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Put, Routes.LiveSessionRecording(session.Id), body)).StatusCode);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var closed = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSessionRecording(session.Id), body);
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal(ProblemCodes.class_closed, await CodeAsync(closed));
    }

    [Fact]
    public async Task Participants_are_staff_only_relationship_checked_and_matched_by_verified_email_only()
    {
        var w = await SetUpAsync("LL9");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: DateTime.UtcNow.AddMinutes(-20))));
        string email;
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            email = (await db.Users.SingleAsync(u => u.Id == w.Student.UserId)).Email!;
            var other = await db.Users.FirstAsync(u => u.Id == w.OtherTeacher.UserId);
            var meetingId = (await db.Set<LiveSession>().SingleAsync(s => s.Id == session.Id)).ExternalMeetingId!;
            var t0 = DateTime.UtcNow.AddMinutes(-15);
            Provider(_t).ParticipantsOf[meetingId] =
            [
                new("Student Zoom name", email, t0, t0.AddMinutes(40), 2400),       // authenticated, enrolled → Matched
                new("Typed the student's email", null, t0, t0.AddMinutes(5), 300),  // not authenticated → Guest
                new("Other account", other.Email, t0, null, 600),                   // authenticated but not enrolled → Guest
            ];
        }

        Assert.True(await EndAsync(session.Id));

        var response = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSessionParticipants(session.Id));
        var text = await response.Content.ReadAsStringAsync();
        var page = System.Text.Json.JsonSerializer.Deserialize<PagedResult<LiveParticipantDto>>(text, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(3, page.Total);
        var matched = Assert.Single(page.Items, p => p.MatchState == ParticipantMatchStates.Matched);
        Assert.Equal(w.StudentProfileId, matched.StudentId);
        Assert.Equal(40, matched.DurationMinutes);
        Assert.NotNull(matched.StudentNameEn);
        Assert.All(page.Items.Where(p => p.MatchState == ParticipantMatchStates.Guest), g => Assert.Null(g.StudentId));
        Assert.DoesNotContain(email, text, StringComparison.OrdinalIgnoreCase); // no email is ever returned
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.LiveSessionParticipants(session.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.LiveSessionParticipants(session.Id))).StatusCode);
        var filtered = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSessionParticipants(session.Id) + "?matchState=Guest"))
            .Content.ReadFromJsonAsync<PagedResult<LiveParticipantDto>>();
        Assert.Equal(2, filtered!.Total);
    }

    [Fact]
    public async Task Attendance_sink_is_called_once_after_the_end_with_the_matched_participants()
    {
        var w = await SetUpAsync("LL10");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single(start: DateTime.UtcNow.AddMinutes(-20))));
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var email = (await db.Users.SingleAsync(u => u.Id == w.Student.UserId)).Email!;
            var meetingId = (await db.Set<LiveSession>().SingleAsync(s => s.Id == session.Id)).ExternalMeetingId!;
            Provider(_t).ParticipantsOf[meetingId] = [new("S", email, DateTime.UtcNow.AddMinutes(-15), DateTime.UtcNow.AddMinutes(-1), 840)];
        }

        Assert.True(await EndAsync(session.Id));
        Assert.False(await EndAsync(session.Id)); // a retried event or the polling fallback does nothing

        var call = Assert.Single(_sink.Calls);
        Assert.Equal(session.Id, call.SessionId);
        var participant = Assert.Single(call.Participants);
        Assert.Equal(w.StudentProfileId, participant.StudentId);
        var read = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(session.Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Equal(LiveSessionStatuses.Ended, read!.Status);
    }

    [Fact]
    public async Task The_participant_import_is_marked_when_it_worked_and_left_unmarked_when_the_provider_failed()
    {
        var w = await SetUpAsync("LL12");
        var ok = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Ok", DateTime.UtcNow.AddMinutes(-20))));
        var failed = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Failed", DateTime.UtcNow.AddMinutes(-20))));

        Assert.True(await EndAsync(ok.Id)); // nobody attended: an empty list is still a successful import
        Provider(_t).FailNext(1);
        Assert.True(await EndAsync(failed.Id));

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var okRow = await db.Set<LiveSession>().SingleAsync(s => s.Id == ok.Id);
        var failedRow = await db.Set<LiveSession>().SingleAsync(s => s.Id == failed.Id);
        Assert.NotNull(okRow.ParticipantsImportedAtUtc);
        Assert.Equal(LiveSessionStatuses.Ended, okRow.Status);
        Assert.Null(failedRow.ParticipantsImportedAtUtc); // attendance must not read this as "everyone was absent"
        Assert.Equal(LiveSessionStatuses.Ended, failedRow.Status);
        Assert.Equal(2, _sink.Calls.Count); // the seam is told either way; the marker says whether the data is complete
    }

    [Fact]
    public async Task A_failing_attendance_sink_never_breaks_the_lifecycle()
    {
        var w = await SetUpAsync("LL13");
        var session = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Sink", DateTime.UtcNow.AddMinutes(-20))));
        _sink.Throw = true;

        var ended = await EndAsync(session.Id); // no exception reaches the caller (the webhook)

        Assert.True(ended);
        Assert.Single(_sink.Calls); // it was called once
        var read = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(session.Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Equal(LiveSessionStatuses.Ended, read!.Status);
        Assert.False(await EndAsync(session.Id)); // already ended: the sink is not called again
        Assert.Single(_sink.Calls);
    }

    [Fact]
    public async Task Upcoming_lists_only_visible_sessions_max_five_and_never_ended_ones()
    {
        var w = await SetUpAsync("LL11");
        for (var i = 1; i <= 7; i++)
        {
            await CreateAsync(_t, w.Teacher, w, Single("S" + i, DateTime.UtcNow.AddHours(i)));
        }

        await CreateAsync(_t, w.Teacher, w, Single("Hidden", DateTime.UtcNow.AddMinutes(30), visibility: ModuleVisibilities.Hidden));
        await CreateAsync(_t, w.Teacher, w, Single("Ended", DateTime.UtcNow.AddHours(-3), minutes: 30));

        var student = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.LiveSessionsUpcoming)).Content.ReadFromJsonAsync<List<UpcomingLiveSessionDto>>();
        var teacher = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSessionsUpcoming)).Content.ReadFromJsonAsync<List<UpcomingLiveSessionDto>>();
        var other = await (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.LiveSessionsUpcoming)).Content.ReadFromJsonAsync<List<UpcomingLiveSessionDto>>();
        var stranger = await (await _t.SendAsync(w.Stranger, HttpMethod.Get, Routes.LiveSessionsUpcoming)).Content.ReadFromJsonAsync<List<UpcomingLiveSessionDto>>();

        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], student!.Select(s => s.TitleEn).ToArray());
        Assert.DoesNotContain(student!, s => s.TitleEn is "Hidden" or "Ended");
        Assert.Equal(5, teacher!.Count);
        Assert.Equal("Hidden", teacher[0].TitleEn); // the teacher sees the hidden one too, ordered by start
        Assert.Empty(other!);
        Assert.Empty(stranger!);
        Assert.All(student!, s => Assert.Equal(s.StartsAtUtc.AddMinutes(-15), s.JoinOpensAtUtc));
        Assert.DoesNotContain("fake.zoom.test", System.Text.Json.JsonSerializer.Serialize(student));
    }
}
