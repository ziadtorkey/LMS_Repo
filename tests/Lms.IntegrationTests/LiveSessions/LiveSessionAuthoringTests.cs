using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.LiveSessions;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Content.ContentTestHost;
using static Lms.IntegrationTests.LiveSessions.LiveSessionTestKit;

namespace Lms.IntegrationTests.LiveSessions;

/// <summary>M4b Task 4 (#471, FR-VC-01): live session authoring, series, module creation, caps and visibility.</summary>
[Collection(ApiCollection.Name)]
public sealed class LiveSessionAuthoringTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Teacher_schedules_single_session_with_module()
    {
        var w = await _t.SetUpAsync("LS1");
        await EnableAsync(factory, w);

        var created = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Intro live")));

        Assert.Equal(LiveSessionStatuses.Scheduled, created.Status);
        Assert.Equal(ModuleKinds.LiveSession, created.Module.Kind);
        Assert.Null(created.SeriesId);
        Assert.True(created.CanEdit);
        Assert.Equal(created.StartsAtUtc.AddMinutes(60), created.EndsAtUtc);
        Assert.Equal(created.StartsAtUtc.AddMinutes(-15), created.JoinOpensAtUtc);
        var meeting = Assert.Single(Provider(_t).Meetings.Values);
        Assert.Equal("Intro live", meeting.Topic); // the title only (Decision 5)
        var modules = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, Lesson1)))
            .Content.ReadFromJsonAsync<List<ModuleNodeDto>>();
        Assert.Contains(modules!, m => m.Id == created.Module.Id && m.Kind == ModuleKinds.LiveSession);
    }

    [Fact]
    public async Task Recurring_creates_one_session_per_occurrence_max_52()
    {
        var w = await _t.SetUpAsync("LS2");
        await EnableAsync(factory, w);

        var series = await CreateAsync(_t, w.Teacher, w, Weekly(4));

        Assert.Equal(4, series.Count);
        Assert.Single(series.Select(s => s.SeriesId).Distinct());
        Assert.Equal([1, 2, 3, 4], series.Select(s => s.OccurrenceIndex!.Value).ToArray());
        Assert.All(series, s => Assert.Equal(4, s.OccurrenceCount));
        Assert.Equal(series[0].StartsAtUtc.AddDays(21), series[3].StartsAtUtc);
        Assert.Equal(4, Provider(_t).Meetings.Count);

        var tooMany = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Weekly(53));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(ProblemCodes.recurrence_invalid, await CodeAsync(tooMany));
        var wrongDay = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id),
            Weekly(2, start: NextSunday().AddDays(1)));
        Assert.Equal(ProblemCodes.recurrence_invalid, await CodeAsync(wrongDay));
        Assert.Equal(4, Provider(_t).Meetings.Count); // nothing was created by the refused requests
    }

    [Fact]
    public async Task Recurrence_follows_wall_clock_time_across_dst()
    {
        var w = await _t.SetUpAsync("LS3");
        await EnableAsync(factory, w);
        // New York: DST ended on the first Sunday of November; 09:00 local is 13:00 UTC before and 14:00 UTC after.
        var year = DateTime.UtcNow.Year + 1;
        var firstSunday = new DateTime(year, 11, 1, 13, 0, 0, DateTimeKind.Utc);
        firstSunday = firstSunday.AddDays((7 - (int)firstSunday.DayOfWeek) % 7);
        var start = firstSunday.AddDays(-7);

        var series = await CreateAsync(_t, w.Teacher, w, Weekly(3, "America/New_York", start));

        Assert.Equal(13, series[0].StartsAtUtc.Hour);
        Assert.Equal(14, series[1].StartsAtUtc.Hour);
        Assert.Equal(14, series[2].StartsAtUtc.Hour);
    }

    [Fact]
    public async Task Edit_this_changes_one_occurrence_and_this_and_following_recreates_future_occurrences_only()
    {
        var w = await _t.SetUpAsync("LS4");
        await EnableAsync(factory, w);
        var series = await CreateAsync(_t, w.Teacher, w, Weekly(4));

        // Scope This: only the second occurrence moves; its module title follows.
        var edit = new UpdateLiveSessionRequest(Module("Moved"), series[1].StartsAtUtc.AddHours(2), 90);
        var moved = await (await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSession(series[1].Id), edit)).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Equal(series[1].StartsAtUtc.AddHours(2), moved!.StartsAtUtc);
        Assert.Equal("Moved", moved.Module.TitleEn);
        Assert.Equal(90, moved.DurationMinutes);
        var untouched = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(series[2].Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Equal(series[2].StartsAtUtc, untouched!.StartsAtUtc);

        // ThisAndFollowing with a new recurrence: occurrence 1 and 2 stay, 3 and 4 are replaced by two new ones after the second.
        var meetingsBefore = Provider(_t).Meetings.Keys.ToHashSet();
        var recurrence = new LiveRecurrenceInput(1, ["Sunday"], null, 3, "UTC");
        var replace = new UpdateLiveSessionRequest(Module("Series"), series[1].StartsAtUtc, 60, SeriesEditScopes.ThisAndFollowing, recurrence);
        var response = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSession(series[1].Id), replace);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await ListAsync(_t, w.Teacher, w);
        Assert.Equal([1, 2, 3, 4], list.Select(s => s.OccurrenceIndex!.Value).Order().ToArray()); // occurrence 1, then 2, 3, 4 again
        Assert.Contains(series[0].Id, list.Select(s => s.Id)); // the earlier occurrence is the same row
        Assert.Contains(series[1].Id, list.Select(s => s.Id)); // the edited one keeps its id
        Assert.DoesNotContain(series[2].Id, list.Select(s => s.Id));
        Assert.DoesNotContain(series[3].Id, list.Select(s => s.Id));
        Assert.Equal(2, Provider(_t).Deleted.Count); // the two replaced meetings were deleted at the provider
        Assert.Equal(meetingsBefore.Count, Provider(_t).Meetings.Count);
    }

    [Fact]
    public async Task This_and_following_without_a_recurrence_shifts_the_following_occurrences()
    {
        var w = await _t.SetUpAsync("LS5");
        await EnableAsync(factory, w);
        var series = await CreateAsync(_t, w.Teacher, w, Weekly(3));

        var edit = new UpdateLiveSessionRequest(Module("Shifted"), series[1].StartsAtUtc.AddHours(3), 45, SeriesEditScopes.ThisAndFollowing);
        var response = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSession(series[1].Id), edit);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var first = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(series[0].Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        var third = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(series[2].Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.Equal(series[0].StartsAtUtc, first!.StartsAtUtc);
        Assert.Equal(series[2].StartsAtUtc.AddHours(3), third!.StartsAtUtc);
        Assert.Equal(45, third.DurationMinutes);
        Assert.Equal("Shifted", third.Module.TitleEn);
    }

    [Fact]
    public async Task A_single_session_accepts_only_the_this_scope_and_a_recurrence_only_with_this_and_following()
    {
        var w = await _t.SetUpAsync("LS6");
        await EnableAsync(factory, w);
        var single = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));

        var following = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSession(single.Id),
            new UpdateLiveSessionRequest(Module(), single.StartsAtUtc, 60, SeriesEditScopes.ThisAndFollowing));
        var withRule = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.LiveSession(single.Id),
            new UpdateLiveSessionRequest(Module(), single.StartsAtUtc, 60, SeriesEditScopes.This, new LiveRecurrenceInput(1, ["Sunday"], null, 2, "UTC")));
        var deleteFollowing = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LiveSession(single.Id, SeriesEditScopes.ThisAndFollowing));

        Assert.Equal(HttpStatusCode.BadRequest, following.StatusCode);
        Assert.Equal(ProblemCodes.recurrence_invalid, await CodeAsync(withRule));
        Assert.Equal(HttpStatusCode.BadRequest, deleteFollowing.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_session_its_module_and_the_provider_meeting()
    {
        var w = await _t.SetUpAsync("LS7");
        await EnableAsync(factory, w);
        var series = await CreateAsync(_t, w.Teacher, w, Weekly(3));

        var one = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LiveSession(series[2].Id));
        var rest = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LiveSession(series[0].Id, SeriesEditScopes.ThisAndFollowing));

        Assert.Equal(HttpStatusCode.NoContent, one.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, rest.StatusCode);
        Assert.Empty(await ListAsync(_t, w.Teacher, w));
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(series[0].Id))).StatusCode);
        Assert.Empty(Provider(_t).Meetings);
        Assert.Equal(3, Provider(_t).Deleted.Count);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(0, await db.Set<LessonModule>().CountAsync(m => m.ClassCourseId == w.ClassCourse.Id && m.Kind == ModuleKinds.LiveSession));
        Assert.False(await db.Set<LiveSessionSeries>().AnyAsync(s => s.ClassCourseId == w.ClassCourse.Id)); // the empty series is gone
    }

    [Fact]
    public async Task Student_sees_only_visible_session_modules()
    {
        var w = await _t.SetUpAsync("LS8");
        await EnableAsync(factory, w);
        var visible = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Visible")));
        var hidden = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Hidden", visibility: ModuleVisibilities.Hidden)));

        var asStudent = await ListAsync(_t, w.Student, w);
        var asTeacher = await ListAsync(_t, w.Teacher, w);

        Assert.Equal([visible.Id], asStudent.Select(s => s.Id).ToArray());
        Assert.Equal(2, asTeacher.Count);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.LiveSession(visible.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.LiveSession(hidden.Id))).StatusCode);
        var student = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.LiveSession(visible.Id))).Content.ReadFromJsonAsync<LiveSessionDto>();
        Assert.False(student!.CanEdit);
    }

    [Fact]
    public async Task Teacher_of_other_class_course_gets_404_and_a_student_gets_403()
    {
        var w = await _t.SetUpAsync("LS9");
        await EnableAsync(factory, w);
        var created = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));
        var url = Routes.ClassCourseLiveSessions(w.ClassCourse.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Post, url, Single())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.LiveSession(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Delete, Routes.LiveSession(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Post, url, Single())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Delete, Routes.LiveSession(created.Id))).StatusCode);
    }

    [Fact]
    public async Task Flag_off_refuses_writes_with_feature_disabled_but_keeps_reads()
    {
        var w = await _t.SetUpAsync("LS10");
        var url = Routes.ClassCourseLiveSessions(w.ClassCourse.Id);

        var create = await _t.SendAsync(w.Teacher, HttpMethod.Post, url, Single());

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(ProblemCodes.feature_disabled, await CodeAsync(create));
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, url)).StatusCode);
        Assert.Empty(Provider(_t).Meetings);
    }

    [Fact]
    public async Task Closed_class_rejects_writes()
    {
        var w = await _t.SetUpAsync("LS11");
        await EnableAsync(factory, w);
        var created = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var create = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Single());
        var delete = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LiveSession(created.Id));

        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal(ProblemCodes.class_closed, await CodeAsync(create));
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(created.Id))).StatusCode);
    }

    [Fact]
    public async Task Daily_create_cap_counts_occurrences_and_refuses_the_whole_series()
    {
        var w = await _t.SetUpAsync("LS12");
        await EnableAsync(factory, w);
        await SetAsync(factory, w, SettingKeys.LiveMaxCreatesPerDay.Name, "5");
        await CreateAsync(_t, w.Teacher, w, Weekly(3)); // 3 of 5

        var refused = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Weekly(3, title: "Too many"));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ProblemCodes.provider_unavailable, await CodeAsync(refused));
        Assert.True(int.Parse(refused.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Equal(3, (await ListAsync(_t, w.Teacher, w)).Count); // none of the refused series exists
        Assert.Equal(3, Provider(_t).Meetings.Count);
        await CreateAsync(_t, w.Teacher, w, Weekly(2, title: "Fits")); // exactly the remaining 2
        Assert.Equal(HttpStatusCode.Conflict, (await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Single())).StatusCode);

        // A delete does not give the quota back.
        var first = (await ListAsync(_t, w.Teacher, w))[0];
        await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.LiveSession(first.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Single())).StatusCode);
    }

    [Fact]
    public async Task Provider_failure_keeps_session_in_sync_failed()
    {
        var w = await _t.SetUpAsync("LS13");
        await EnableAsync(factory, w);
        Provider(_t).FailNext(1);

        var created = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Offline")));

        Assert.Equal(LiveSessionStatuses.SyncFailed, created.Status);
        Assert.Empty(Provider(_t).Meetings);
        var list = await ListAsync(_t, w.Teacher, w);
        Assert.Equal(LiveSessionStatuses.SyncFailed, Assert.Single(list).Status); // kept, not lost
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Null((await db.Set<LiveSession>().SingleAsync(s => s.Id == created.Id)).ExternalMeetingId);
    }

    [Theory]
    [InlineData(MeetingFailure.CapacityExhausted, ProblemCodes.provider_unavailable)]
    [InlineData(MeetingFailure.NotConfigured, ProblemCodes.provider_not_configured)]
    public async Task A_provider_without_capacity_or_configuration_refuses_the_whole_request_and_creates_nothing(MeetingFailure failure, string code)
    {
        var w = await _t.SetUpAsync("LS16" + failure);
        await EnableAsync(factory, w);
        var provider = Provider(_t);
        var first = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Kept")));
        provider.FailNext(2, failure); // the second occurrence of the series fails after the first was created... first call fails at once

        var refused = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Weekly(3, title: "Refused"));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(code, await CodeAsync(refused));
        if (failure == MeetingFailure.CapacityExhausted)
        {
            Assert.True(int.Parse(refused.Headers.GetValues("Retry-After").Single()) > 0);
        }

        Assert.Equal([first.Id], (await ListAsync(_t, w.Teacher, w)).Select(s => s.Id).ToArray()); // only the earlier session remains
        Assert.Single(provider.Meetings);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<LiveSessionSeries>().AnyAsync(x => x.ClassCourseId == w.ClassCourse.Id));
        Assert.Equal(1, await db.Set<LessonModule>().CountAsync(m => m.ClassCourseId == w.ClassCourse.Id && m.Kind == ModuleKinds.LiveSession));
    }

    [Theory]
    [InlineData(false)] // occurrences 1 and 2 are created, 3 is refused
    [InlineData(true)] // occurrence 1 fails plainly (row SyncFailed), 2 is created, 3 is refused
    public async Task A_refusal_after_earlier_occurrences_deletes_their_meetings_and_leaves_no_rows_modules_or_series(bool firstFailsPlainly)
    {
        var w = await _t.SetUpAsync("LS17" + firstFailsPlainly);
        await EnableAsync(factory, w);
        var provider = Provider(_t);
        var kept = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single("Kept")));
        provider.ScriptCreates(firstFailsPlainly ? MeetingFailure.Failed : null, null, MeetingFailure.CapacityExhausted);

        var refused = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Weekly(3, title: "Refused"));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ProblemCodes.provider_unavailable, await CodeAsync(refused));
        Assert.True(int.Parse(refused.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Equal(firstFailsPlainly ? 1 : 2, provider.Deleted.Count); // the meetings that were created are deleted again
        Assert.Equal([kept.Id], (await ListAsync(_t, w.Teacher, w)).Select(s => s.Id).ToArray());
        Assert.Single(provider.Meetings); // back to what it was before the request
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<LiveSessionSeries>().AnyAsync(x => x.ClassCourseId == w.ClassCourse.Id));
        Assert.Equal(1, await db.Set<LiveSession>().CountAsync(x => x.ClassCourseId == w.ClassCourse.Id));
        Assert.Equal(1, await db.Set<LessonModule>().CountAsync(m => m.ClassCourseId == w.ClassCourse.Id && m.Kind == ModuleKinds.LiveSession));
    }

    [Fact]
    public async Task A_host_without_a_meeting_provider_answers_provider_not_configured_and_creates_nothing()
    {
        using var t = new ContentTestHost(factory, services => services.AddSingleton<IMeetingProvider, NotConfiguredMeetingProvider>());
        var w = await t.SetUpAsync("LS18");
        await EnableAsync(factory, w);

        var response = await t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLiveSessions(w.ClassCourse.Id), Single());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.provider_not_configured, await CodeAsync(response));
        Assert.Empty(await ListAsync(t, w.Teacher, w));
    }

    [Fact]
    public async Task An_unexpected_provider_exception_keeps_the_session_in_sync_failed_instead_of_a_500()
    {
        var w = await _t.SetUpAsync("LS19");
        await EnableAsync(factory, w);
        Provider(_t).NextException = new HttpRequestException("connection reset");

        var created = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));

        Assert.Equal(LiveSessionStatuses.SyncFailed, created.Status);
        Assert.Single(await ListAsync(_t, w.Teacher, w));
    }

    [Fact]
    public async Task Lists_and_reads_never_contain_join_or_start_urls_and_the_row_holds_them_protected()
    {
        var w = await _t.SetUpAsync("LS14");
        await EnableAsync(factory, w);
        var created = Assert.Single(await CreateAsync(_t, w.Teacher, w, Single()));

        string[] bodies =
        [
            await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLiveSessions(w.ClassCourse.Id))).Content.ReadAsStringAsync(),
            await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.LiveSession(created.Id))).Content.ReadAsStringAsync(),
            await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.LiveSession(created.Id))).Content.ReadAsStringAsync(),
        ];

        Assert.All(bodies, b =>
        {
            Assert.DoesNotContain("fake.zoom.test", b);
            Assert.DoesNotContain("joinUrl", b, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("startUrl", b, StringComparison.OrdinalIgnoreCase);
        });
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.Set<LiveSession>().SingleAsync(s => s.Id == created.Id);
        Assert.False(string.IsNullOrEmpty(row.JoinUrlProtected));
        Assert.DoesNotContain("fake.zoom.test", row.JoinUrlProtected);
        Assert.DoesNotContain("fake.zoom.test", row.StartUrlProtected);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        var w = await _t.SetUpAsync("LS15");
        await EnableAsync(factory, w);
        var url = Routes.ClassCourseLiveSessions(w.ClassCourse.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await _t.SendAsync(w.Teacher, HttpMethod.Post, url, Single(minutes: 10))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.SendAsync(w.Teacher, HttpMethod.Post, url, Single(minutes: 500))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.SendAsync(w.Teacher, HttpMethod.Post, url, Single(title: new string('x', 201)))).StatusCode);
        var wrongRule = Single() with { Module = Module(rule: CompletionRules.Submit) };
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.SendAsync(w.Teacher, HttpMethod.Post, url, wrongRule)).StatusCode);
        var unknownLesson = Single() with { LessonKey = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.SendAsync(w.Teacher, HttpMethod.Post, url, unknownLesson)).StatusCode);
        Assert.Empty(await ListAsync(_t, w.Teacher, w));
        Assert.Empty(Provider(_t).Meetings);
    }
}
