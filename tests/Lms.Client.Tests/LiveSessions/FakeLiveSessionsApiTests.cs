using System.Net;
using Lms.Client.Services.Api;
using Lms.Client.Services.LiveSessions;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;

namespace Lms.Client.Tests.LiveSessions;

public sealed class FakeLiveSessionsApiTests
{
    private static readonly Guid Cc = Guid.NewGuid(), Lesson = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc); // a Monday

    private static FakeLiveSessionsApi NewFake(bool seedDemo = false) => new(seedDemo) { ServerNow = () => Now };

    private static ModuleSettingsInput Settings(string title = "Weekly review") =>
        new(title, title, ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null);

    private static CreateLiveSessionRequest Single(DateTime start, int minutes = 60) =>
        new(Lesson, Settings(), start, minutes);

    private static CreateLiveSessionRequest Weekly(LiveRecurrenceInput recurrence, DateTime? start = null) =>
        new(Lesson, Settings(), start ?? Now.AddDays(1), 45, recurrence); // Now is a Monday, so the default start is a Tuesday

    private static LiveRecurrenceInput Every(int weeks, int? count = null, DateTime? until = null, params string[] days) =>
        new(weeks, days.Length == 0 ? ["Tuesday"] : days, until, count, "UTC");

    private static async Task<ApiProblemException> Problem(Func<Task> call) =>
        await Assert.ThrowsAsync<ApiProblemException>(call);

    [Fact]
    public async Task Fake_enforces_the_join_window_with_the_server_clock()
    {
        var fake = NewFake();
        var s = (await fake.CreateAsync(Cc, Single(Now.AddMinutes(30)))).Single();
        fake.AsStudent = true;
        Assert.Equal(Now.AddMinutes(30 - 10), s.JoinOpensAtUtc); // Live.JoinWindowMinutes default 10

        // Too early (by the SERVER clock): refused with live_session_not_open.
        var early = await Problem(() => fake.JoinAsync(s.Id));
        Assert.Equal(409, early.Status);
        Assert.Equal(ProblemCodes.live_session_not_open, early.Code);

        // Inside the window: a URL that expires at the scheduled end.
        var inside = Now.AddMinutes(25);
        fake.ServerNow = () => inside;
        var join = await fake.JoinAsync(s.Id);
        Assert.StartsWith("https://", join.Url);
        Assert.Equal(s.EndsAtUtc, join.ExpiresAtUtc);
        Assert.Equal(inside, join.ServerNowUtc);

        // After the end: closed again.
        fake.ServerNow = () => Now.AddMinutes(30 + 61);
        Assert.Equal(ProblemCodes.live_session_not_open, (await Problem(() => fake.JoinAsync(s.Id))).Code);
    }

    [Fact]
    public async Task Fake_join_refuses_cancelled_and_sync_failed_sessions_and_lets_staff_in_early()
    {
        var fake = NewFake();
        var cancelled = fake.Seed(Cc, Lesson, Now.AddMinutes(5), 60, LiveSessionStatuses.Cancelled);
        var failed = fake.Seed(Cc, Lesson, Now.AddMinutes(5), 60, LiveSessionStatuses.SyncFailed);
        var later = fake.Seed(Cc, Lesson, Now.AddDays(1), 60, LiveSessionStatuses.Scheduled);
        fake.AsStudent = true;

        Assert.Equal(ProblemCodes.live_session_not_open, (await Problem(() => fake.JoinAsync(cancelled.Id))).Code);
        Assert.Equal(ProblemCodes.live_session_not_open, (await Problem(() => fake.JoinAsync(failed.Id))).Code);

        // Staff are not held to the student window.
        fake.AsStudent = false;
        Assert.NotNull((await fake.JoinAsync(later.Id)).Url);
    }

    [Fact]
    public async Task Fake_creates_a_module_per_session_and_validates_the_duration()
    {
        var fake = NewFake();

        var created = (await fake.CreateAsync(Cc, Single(Now.AddDays(1), 90))).Single();

        Assert.Equal(ModuleKinds.LiveSession, created.Module.Kind);
        Assert.Equal(Now.AddDays(1).AddMinutes(90), created.EndsAtUtc);
        Assert.Equal(LiveSessionStatuses.Scheduled, created.Status);
        Assert.Null(created.SeriesId);
        Assert.Equal(ProblemCodes.validation_failed, (await Problem(() => fake.CreateAsync(Cc, Single(Now.AddDays(1), 14)))).Code);
        Assert.Equal(ProblemCodes.validation_failed, (await Problem(() => fake.CreateAsync(Cc, Single(Now.AddDays(1), 481)))).Code);
    }

    [Fact]
    public async Task Fake_recurring_creates_one_session_per_occurrence_max_52()
    {
        var fake = NewFake();

        // Tuesdays and Thursdays, every week, 6 occurrences, starting on a Tuesday.
        var series = await fake.CreateAsync(Cc, Weekly(Every(1, count: 6, days: ["Tuesday", "Thursday"])));

        Assert.Equal(6, series.Count);
        Assert.All(series, s => Assert.Equal(series[0].SeriesId, s.SeriesId));
        Assert.NotNull(series[0].SeriesId);
        Assert.Equal([1, 2, 3, 4, 5, 6], series.Select(s => s.OccurrenceIndex!.Value));
        Assert.All(series, s => Assert.Equal(6, s.OccurrenceCount));
        Assert.Equal(
            [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Tuesday, DayOfWeek.Thursday],
            series.Select(s => s.StartsAtUtc.DayOfWeek));
        Assert.Equal(series.Select(s => s.StartsAtUtc).Order(), series.Select(s => s.StartsAtUtc));
        Assert.Equal(series.Count, series.Select(s => s.Module.Id).Distinct().Count());

        // The end date also ends the series: 3 Tuesdays up to 2 weeks and 1 hour after the first.
        var until = Weekly(Every(1, until: Now.AddDays(1 + 14).AddHours(1)));
        Assert.Equal(3, (await fake.CreateAsync(Cc, until)).Count);

        // Exactly 52 is fine, 53 is not.
        Assert.Equal(52, (await fake.CreateAsync(Cc, Weekly(Every(1, count: 52)))).Count);
        Assert.Equal(ProblemCodes.recurrence_invalid, (await Problem(() => fake.CreateAsync(Cc, Weekly(Every(1, count: 53))))).Code);
        Assert.Equal(ProblemCodes.recurrence_invalid, (await Problem(() => fake.CreateAsync(Cc, Weekly(Every(1, until: Now.AddYears(3)))))).Code);
    }

    [Fact]
    public async Task Fake_recurrence_needs_exactly_one_end_a_day_and_a_known_zone()
    {
        var fake = NewFake();

        async Task Invalid(LiveRecurrenceInput r) =>
            Assert.Equal(ProblemCodes.recurrence_invalid, (await Problem(() => fake.CreateAsync(Cc, Weekly(r)))).Code);

        await Invalid(Every(1));                                                          // no end
        await Invalid(Every(1, count: 3, until: Now.AddDays(30)));                        // both ends
        await Invalid(new LiveRecurrenceInput(1, [], null, 3, "UTC"));                    // no day
        await Invalid(new LiveRecurrenceInput(0, ["Tuesday"], null, 3, "UTC"));           // interval below 1
        await Invalid(new LiveRecurrenceInput(1, ["Tuesday"], null, 3, "Mars/Olympus"));  // unknown zone
        await Invalid(Every(1, until: Now));                                              // end before start
    }

    [Fact]
    public async Task Fake_recurrence_keeps_the_wall_clock_time_across_a_dst_change()
    {
        var fake = NewFake();
        // New York leaves daylight time on 2026-11-01: 09:00 EDT is 13:00 UTC, 09:00 EST is 14:00 UTC.
        var start = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc); // a Tuesday
        fake.ServerNow = () => new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

        var series = await fake.CreateAsync(Cc, new(Lesson, Settings(), start, 60,
            new LiveRecurrenceInput(1, ["Tuesday"], null, 2, "America/New_York")));

        Assert.Equal(start, series[0].StartsAtUtc);
        Assert.Equal(start.AddDays(7).AddHours(1), series[1].StartsAtUtc);
    }

    [Fact]
    public async Task Fake_series_edit_scope_this_vs_following()
    {
        var fake = NewFake();
        var series = await fake.CreateAsync(Cc, Weekly(Every(1, count: 4)));

        // This: only the second occurrence moves.
        await fake.UpdateAsync(series[1].Id, new(Settings("Moved"), series[1].StartsAtUtc.AddHours(2), 45));
        Assert.Equal(series[1].StartsAtUtc.AddHours(2), (await fake.GetAsync(series[1].Id)).StartsAtUtc);
        Assert.Equal(series[2].StartsAtUtc, (await fake.GetAsync(series[2].Id)).StartsAtUtc);
        Assert.Equal("Weekly review", (await fake.GetAsync(series[2].Id)).Module.TitleEn);

        // ThisAndFollowing: from the third on, never the earlier ones.
        await fake.UpdateAsync(series[2].Id, new(Settings("Renamed"), series[2].StartsAtUtc.AddHours(1), 90, SeriesEditScopes.ThisAndFollowing));
        Assert.Equal(series[0].StartsAtUtc, (await fake.GetAsync(series[0].Id)).StartsAtUtc);
        Assert.Equal("Moved", (await fake.GetAsync(series[1].Id)).Module.TitleEn);
        Assert.Equal(series[2].StartsAtUtc.AddHours(1), (await fake.GetAsync(series[2].Id)).StartsAtUtc);
        var last = await fake.GetAsync(series[3].Id);
        Assert.Equal(series[3].StartsAtUtc.AddHours(1), last.StartsAtUtc);
        Assert.Equal(90, last.DurationMinutes);
        Assert.Equal("Renamed", last.Module.TitleEn);

        // A single session only accepts the This scope.
        var single = (await fake.CreateAsync(Cc, Single(Now.AddDays(2)))).Single();
        Assert.Equal(ProblemCodes.validation_failed,
            (await Problem(() => fake.UpdateAsync(single.Id, new(Settings(), single.StartsAtUtc, 60, SeriesEditScopes.ThisAndFollowing)))).Code);
    }

    [Fact]
    public async Task Fake_series_delete_scope_cancels_this_or_this_and_following()
    {
        var fake = NewFake();
        var series = await fake.CreateAsync(Cc, Weekly(Every(1, count: 4)));

        await fake.DeleteAsync(series[1].Id, SeriesEditScopes.This);
        await fake.DeleteAsync(series[2].Id, SeriesEditScopes.ThisAndFollowing);

        var statuses = new List<string>();
        foreach (var s in series) statuses.Add((await fake.GetAsync(s.Id)).Status);
        Assert.Equal(
            [LiveSessionStatuses.Scheduled, LiveSessionStatuses.Cancelled, LiveSessionStatuses.Cancelled, LiveSessionStatuses.Cancelled],
            statuses);
    }

    [Fact]
    public async Task Fake_reports_sync_failed_and_provider_unavailable()
    {
        var fake = NewFake();
        var failed = fake.Seed(Cc, Lesson, Now.AddDays(1), 60, LiveSessionStatuses.SyncFailed);

        // A SyncFailed row stays listed, with its status, so the UI can explain it.
        var list = await fake.ListAsync(Cc, new LiveSessionQuery(Status: LiveSessionStatuses.SyncFailed));
        Assert.Equal([failed.Id], list.Items.Select(i => i.Id));

        // Platform status for the banner.
        Assert.Equal(new ProviderStatusDto(true, true), await fake.GetProviderStatusAsync());
        fake.ProviderConfigured = false;
        Assert.Equal(new ProviderStatusDto(false, false), await fake.GetProviderStatusAsync());
        fake.ProviderConfigured = true;
        fake.ProviderHealthy = false;
        Assert.Equal(new ProviderStatusDto(true, false), await fake.GetProviderStatusAsync());

        // Creating while Zoom is down: 409 provider_unavailable; nothing is created.
        var before = (await fake.ListAsync(Cc, new LiveSessionQuery())).Total;
        var ex = await Problem(() => fake.CreateAsync(Cc, Single(Now.AddDays(1))));
        Assert.Equal(409, ex.Status);
        Assert.Equal(ProblemCodes.provider_unavailable, ex.Code);
        Assert.Equal(before, (await fake.ListAsync(Cc, new LiveSessionQuery())).Total);

        // Feature off: 403 feature_disabled.
        fake.ProviderHealthy = true;
        fake.FeatureEnabled = false;
        var off = await Problem(() => fake.CreateAsync(Cc, Single(Now.AddDays(1))));
        Assert.Equal(403, off.Status);
        Assert.Equal(ProblemCodes.feature_disabled, off.Code);
    }

    [Fact]
    public async Task Fake_recording_link_accepts_only_http_and_https()
    {
        var fake = NewFake();
        var s = (await fake.CreateAsync(Cc, Single(Now.AddDays(1)))).Single();

        var updated = await fake.SetRecordingAsync(s.Id, new RecordingLinkRequest("https://videos.example.org/r/1"));
        Assert.Equal("https://videos.example.org/r/1", updated.RecordingUrl);
        Assert.Equal(RecordingSources.Manual, updated.RecordingSource);

        foreach (var bad in new[] { "javascript:alert(1)", "ftp://x/y", "not a url", "", new string('a', 2050) })
            Assert.Equal(ProblemCodes.validation_failed, (await Problem(() => fake.SetRecordingAsync(s.Id, new RecordingLinkRequest(bad)))).Code);
    }

    [Fact]
    public async Task Fake_lists_by_class_course_with_filters_and_paging()
    {
        var fake = NewFake();
        for (var i = 1; i <= 5; i++) fake.Seed(Cc, Lesson, Now.AddDays(i), 60, LiveSessionStatuses.Scheduled);
        fake.Seed(Cc, Lesson, Now.AddDays(-2), 60, LiveSessionStatuses.Ended);
        fake.Seed(Guid.NewGuid(), Lesson, Now.AddDays(1), 60, LiveSessionStatuses.Scheduled); // another class course

        var page = await fake.ListAsync(Cc, new LiveSessionQuery(Page: 2, PageSize: 4));
        Assert.Equal(6, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(page.Items.Select(i => i.StartsAtUtc).Order(), page.Items.Select(i => i.StartsAtUtc));

        var ended = await fake.ListAsync(Cc, new LiveSessionQuery(Status: LiveSessionStatuses.Ended));
        Assert.Single(ended.Items);
        var window = await fake.ListAsync(Cc, new LiveSessionQuery(FromUtc: Now.AddDays(2), ToUtc: Now.AddDays(3)));
        Assert.Equal(2, window.Items.Count);
        Assert.All(window.Items, i => Assert.Equal(Now, i.ServerNowUtc));
    }

    [Fact]
    public async Task Fake_upcoming_has_at_most_five_not_ended_sessions_in_start_order()
    {
        var fake = NewFake();
        for (var i = 1; i <= 7; i++) fake.Seed(Cc, Lesson, Now.AddDays(8 - i), 60, LiveSessionStatuses.Scheduled);
        fake.Seed(Cc, Lesson, Now.AddDays(-1), 60, LiveSessionStatuses.Ended);

        var upcoming = await fake.ListUpcomingAsync();

        Assert.Equal(LiveSessionLimits.UpcomingMax, upcoming.Count);
        Assert.Equal(upcoming.Select(u => u.StartsAtUtc).Order(), upcoming.Select(u => u.StartsAtUtc));
        Assert.DoesNotContain(upcoming, u => u.Status == LiveSessionStatuses.Ended);
    }

    [Fact]
    public async Task Fake_participants_are_filtered_and_staff_only()
    {
        var fake = NewFake(seedDemo: true);
        var ended = (await fake.ListAsync(FakeLiveSessionsApi.DemoClassCourseId, new LiveSessionQuery(Status: LiveSessionStatuses.Ended))).Items.First();

        var all = await fake.ListParticipantsAsync(ended.Id, new LiveParticipantQuery());
        var guests = await fake.ListParticipantsAsync(ended.Id, new LiveParticipantQuery(ParticipantMatchStates.Guest));

        Assert.Contains(all.Items, p => p.MatchState == ParticipantMatchStates.Matched && p.StudentId is not null);
        Assert.NotEmpty(guests.Items);
        Assert.All(guests.Items, p => Assert.Null(p.StudentId));

        fake.AsStudent = true;
        Assert.Equal(403, (await Problem(() => fake.ListParticipantsAsync(ended.Id, new LiveParticipantQuery()))).Status);
    }

    [Fact]
    public async Task Fake_student_cannot_author()
    {
        var fake = NewFake();
        var s = fake.Seed(Cc, Lesson, Now.AddDays(1), 60, LiveSessionStatuses.Scheduled);
        fake.AsStudent = true;

        Assert.Equal(403, (await Problem(() => fake.CreateAsync(Cc, Single(Now.AddDays(1))))).Status);
        Assert.Equal(403, (await Problem(() => fake.DeleteAsync(s.Id))).Status);
        Assert.Equal(403, (await Problem(() => fake.GetProviderStatusAsync())).Status);
        Assert.False((await fake.GetAsync(s.Id)).CanEdit);
    }

    [Fact]
    public async Task Fake_unknown_session_is_404()
    {
        var fake = NewFake();
        Assert.Equal(404, (await Problem(() => fake.GetAsync(Guid.NewGuid()))).Status);
        Assert.Equal(404, (await Problem(() => fake.JoinAsync(Guid.NewGuid()))).Status);
    }

    [Fact]
    public async Task Fake_demo_data_covers_every_status()
    {
        var fake = NewFake(seedDemo: true);

        var list = await fake.ListAsync(FakeLiveSessionsApi.DemoClassCourseId, new LiveSessionQuery(PageSize: 50));

        Assert.Equal(LiveSessionStatuses.All.Order(), list.Items.Select(i => i.Status).Distinct().Order());
        Assert.Contains(list.Items, i => i.HasRecording);
    }

    [Fact]
    public async Task Http_client_uses_the_contract_routes()
    {
        var requests = new List<(HttpMethod Method, string Path)>();
        var handler = new RecordingHandler(r => requests.Add((r.Method, r.RequestUri!.PathAndQuery)));
        var api = new LiveSessionsApi(new ApiHttp(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }, new InMemoryContextStore()));
        var id = Guid.NewGuid();
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        await api.ListAsync(Cc, new LiveSessionQuery(from, null, LiveSessionStatuses.Scheduled, 2, 20)).ContinueWith(_ => { });
        await api.CreateAsync(Cc, Single(Now)).ContinueWith(_ => { });
        await api.GetAsync(id).ContinueWith(_ => { });
        await api.UpdateAsync(id, new(Settings(), Now, 60)).ContinueWith(_ => { });
        await api.DeleteAsync(id, SeriesEditScopes.ThisAndFollowing);
        await api.JoinAsync(id).ContinueWith(_ => { });
        await api.SetRecordingAsync(id, new RecordingLinkRequest("https://x.example/r")).ContinueWith(_ => { });
        await api.ListParticipantsAsync(id, new LiveParticipantQuery(ParticipantMatchStates.Guest, 3, 10)).ContinueWith(_ => { });
        await api.GetProviderStatusAsync().ContinueWith(_ => { });
        await api.ListUpcomingAsync().ContinueWith(_ => { });

        Assert.Contains((HttpMethod.Get, Routes.ClassCourseLiveSessions(Cc) + "?page=2&pageSize=20&fromUtc=2026-11-01T00%3A00%3A00Z&status=Scheduled"), requests);
        Assert.Contains((HttpMethod.Post, Routes.ClassCourseLiveSessions(Cc)), requests);
        Assert.Contains((HttpMethod.Get, Routes.LiveSession(id)), requests);
        Assert.Contains((HttpMethod.Put, Routes.LiveSession(id)), requests);
        Assert.Contains((HttpMethod.Delete, Routes.LiveSession(id, SeriesEditScopes.ThisAndFollowing)), requests);
        Assert.Contains((HttpMethod.Post, Routes.LiveSessionJoin(id)), requests);
        Assert.Contains((HttpMethod.Put, Routes.LiveSessionRecording(id)), requests);
        Assert.Contains((HttpMethod.Get, Routes.LiveSessionParticipants(id) + "?page=3&pageSize=10&matchState=Guest"), requests);
        Assert.Contains((HttpMethod.Get, Routes.LiveSessionsProviderStatus), requests);
        Assert.Contains((HttpMethod.Get, Routes.LiveSessionsUpcoming), requests);
    }

    private sealed class RecordingHandler(Action<HttpRequestMessage> record) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            record(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
