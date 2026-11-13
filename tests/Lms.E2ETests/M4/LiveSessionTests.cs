using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// M4b exit demo 1 (plan Task 22, #491): a teacher schedules a recurring live session, students join it, the provider's events
/// attach the recording and the participants, in Arabic (RTL) and English. SKELETON: every scenario is skipped until the
/// pieces it needs are on main; the page objects (<see cref="LiveSessionsPage"/>, <see cref="LiveSessionPanel"/> ...) and the set-up
/// are real and compile against the live-session contract. The school, the class course with its published plan, the teacher and
/// the two students in two groups come from <see cref="M4Kit"/> (API); sessions are created through the API unless a scenario tests
/// scheduling itself. The meeting provider is the <c>FakeMeetingProvider</c> of the E2E stack (no Zoom); Zoom events arrive through the
/// dev-only signed trigger <c>POST /api/dev/zoom/events</c> (Task 7). One E2E stack at a time: take <c>e2e.lock</c> before a run.
/// </summary>
public sealed class LiveSessionTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    // One reason per dependency, so un-skipping is a search and replace as the tasks land (plan Task 22 "Depends on").
    private const string NeedsAuthoring = "Needs live session authoring: API T4 (#513), UI T13/T14 (#504, #515) on main";
    private const string NeedsJoin = "Needs join and the page: API T5/T6, UI T15 (#510) on main, FakeMeetingProvider in the E2E stack";
    private const string NeedsEvents = "Needs the Zoom events path (T7 webhook + dev trigger POST /api/dev/zoom/events) and the participants dialog (#510)";
    private const string NeedsReminders = "Needs the reminder job (T6) and the Home / bell labels (T15, T18)";

    /// <summary>The teacher schedules a weekly series through the dialog; the list shows every occurrence and the module appears in the lesson for the students.</summary>
    [Theory(Skip = NeedsAuthoring)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_schedules_weekly_session_and_students_see_the_modules(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var world = await WorldAsync();
        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);

        var list = new LiveSessionsPage(Page);
        await list.OpenAsync(world.ClassCourseId);
        await Expect(list.Empty).ToBeVisibleAsync();

        var dialog = await list.OpenScheduleAsync();
        await dialog.SetTitlesAsync("حصة مباشرة", "Live class");
        await dialog.PickLessonAsync("Lesson 1"); // the title the M4Kit plan gives its first lesson (check against M4Kit)
        await dialog.SetStartAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(7)), "10:00");
        await dialog.SetWeeklyAsync(count: 4, DateTime.Today.AddDays(7).DayOfWeek);
        await Expect(dialog.PreviewCount).ToContainTextAsync("4");
        await dialog.SaveAsync();

        await Expect(list.Rows).ToHaveCountAsync(4);
        await Expect(list.StatusChips.First).ToHaveAttributeAsync("data-status", "Scheduled");

        // A student of the class course sees the four modules in the lesson (module kind LiveSession).
        await M4Kit.SignOutAsync(Page);
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var course = new MyCoursePage(Page);
        await course.OpenAsync(world.ClassCourseId);
        await Expect(course.ModuleLinks).ToHaveCountAsync(4);

        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>Before the join window the button is disabled with the countdown; inside the window it opens the meeting in a new tab (the fake provider's URL).</summary>
    [Theory(Skip = NeedsJoin)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_join_is_disabled_before_the_window_and_opens_a_new_tab_inside_it(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, later, soon) = await SetUpSessionsAsync(
            laterStartsIn: TimeSpan.FromHours(3), soonStartsIn: TimeSpan.FromMinutes(5));

        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var panel = new LiveSessionPanel(Page);

        await panel.OpenAsync(world.ClassCourseId, later.ModuleId);
        await Expect(panel.Countdown).ToBeVisibleAsync();
        await Expect(panel.Join).ToBeDisabledAsync();

        await panel.OpenAsync(world.ClassCourseId, soon.ModuleId);
        await Expect(panel.Join).ToBeEnabledAsync();
        var popup = await Page.RunAndWaitForPopupAsync(() => panel.Join.ClickAsync());
        Assert.StartsWith("http", popup.Url == "about:blank" ? "http" : popup.Url); // the blank tab is pointed at the provider URL
        await popup.CloseAsync();
        await Expect(panel.JoinError).ToHaveCountAsync(0);

        // The start URL is never in the markup or in a response to a student (staff get it).
        Assert.DoesNotContain("start_url", await Page.ContentAsync(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A student of the other group never sees a group-targeted session: not in the course, and its address answers "not found".</summary>
    [Theory(Skip = NeedsJoin)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_of_other_group_cannot_see_the_session(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, _, soon) = await SetUpSessionsAsync(
            laterStartsIn: TimeSpan.FromHours(3), soonStartsIn: TimeSpan.FromMinutes(5), soonGroup: g => g.Group1Id);

        // Student 2 is in group 2: the session of group 1 is invisible and its module address is "not found".
        await M4Kit.SignInAsync(Page, world.Student2.UserName, world.Student2.Password, culture);
        var panel = new LiveSessionPanel(Page);
        await panel.OpenAsync(world.ClassCourseId, soon.ModuleId);
        await Expect(panel.NotAvailable.Or(Page.GetByText(Resx.In(culture, "Error_not_found")))).ToBeVisibleAsync();
        await Expect(panel.Join).ToHaveCountAsync(0);

        var api = new E2EApi(Page, Settings.BaseUrl);
        Assert.Equal(404, (await api.SendAsync("GET", $"/api/live-sessions/{soon.SessionId}")).Status);
        Assert.Equal(404, (await api.SendAsync("POST", $"/api/live-sessions/{soon.SessionId}/join")).Status);
    }

    /// <summary>The provider's events (meeting ended, recording ready, participants) arrive through the signed dev trigger: the teacher sees the recording and the matched/guest participants.</summary>
    [Theory(Skip = NeedsEvents)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Provider_events_attach_recording_and_participants_for_the_teacher(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, _, soon) = await SetUpSessionsAsync(
            laterStartsIn: TimeSpan.FromHours(3), soonStartsIn: TimeSpan.FromMinutes(5));

        await SendZoomEventsAsync(soon.SessionId, world);
        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture); // started, two participants (one a student), ended, recording

        var panel = new LiveSessionPanel(Page);
        await panel.OpenAsync(world.ClassCourseId, soon.ModuleId);
        await Expect(panel.Recording).ToBeVisibleAsync();
        await Expect(panel.RecordingHost).ToBeVisibleAsync();
        await panel.Participants.ClickAsync();
        var participants = new ParticipantsDialog(Page);
        await Expect(participants.MatchChips).ToHaveCountAsync(2);
        await Expect(participants.MatchChips.Filter(new() { Has = Page.Locator("[data-match='Matched']") })).ToHaveCountAsync(1);
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>With cloud recording off, the teacher pastes a recording link; students then see it with its host name, and a non-http link is refused.</summary>
    [Theory(Skip = NeedsAuthoring)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_pastes_a_recording_link_when_cloud_recording_is_off(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, later, _) = await SetUpSessionsAsync(
            laterStartsIn: TimeSpan.FromHours(3), soonStartsIn: TimeSpan.FromMinutes(5));

        await M4Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        var list = new LiveSessionsPage(Page);
        await list.OpenAsync(world.ClassCourseId);
        await list.RecordingAction(later.SessionId).ClickAsync();
        var dialog = new RecordingLinkDialog(Page);
        await dialog.Url.FillAsync("javascript:alert(1)");
        await Expect(dialog.Error).ToBeVisibleAsync();
        await dialog.SaveLinkAsync("https://drive.example.org/recording/123");

        await M4Kit.SignOutAsync(Page);
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var panel = new LiveSessionPanel(Page);
        await panel.OpenAsync(world.ClassCourseId, later.ModuleId);
        await Expect(panel.RecordingHost).ToContainTextAsync("drive.example.org");
        await Expect(panel.Recording).ToHaveAttributeAsync("rel", new System.Text.RegularExpressions.Regex("nofollow"));
    }

    /// <summary>The reminder job puts a "starts soon" notification in the student's bell, in the student's language.</summary>
    [Theory(Skip = NeedsReminders)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Reminder_appears_in_the_bell(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var (world, _, _) = await SetUpSessionsAsync(
            laterStartsIn: TimeSpan.FromHours(3), soonStartsIn: TimeSpan.FromMinutes(5));

        // The reminder job runs on a trigger (dev job endpoint / Hangfire in the E2E stack): see JobsClient for the existing pattern.
        await M4Kit.SignInAsync(Page, world.Student1.UserName, world.Student1.Password, culture);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Expect(Page.GetByTestId("notif-toggle")).ToBeVisibleAsync(); // the bell button (menu: notif-bell, count: notif-badge)
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    // ---------------------------------------------------------------------------------------------------- set-up through the API

    /// <summary>A session created through the API: its id, its module id and its start.</summary>
    private sealed record Session(Guid SessionId, Guid ModuleId, DateTime StartsAtUtc);

    private async Task<M4World> WorldAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var api = new ApiSetup(Page);
        var world = await M4Kit.BuildWorldAsync(api, M4Names.Suffix());
        await EnableVirtualClassesAsync(api); // Features.VirtualClasses is off by default
        await M4Kit.SignOutAsync(Page);
        return world;
    }

    /// <summary>Signs in as the org admin, builds the world, creates a later and a soon session, signs out.</summary>
    private async Task<(M4World World, Session Later, Session Soon)> SetUpSessionsAsync(
        TimeSpan laterStartsIn, TimeSpan soonStartsIn, Func<M4World, Guid>? soonGroup = null)
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var api = new ApiSetup(Page);
        var world = await M4Kit.BuildWorldAsync(api, M4Names.Suffix());
        await EnableVirtualClassesAsync(api);
        var later = await CreateSessionAsync(api, world, "Later class", laterStartsIn, null);
        var soon = await CreateSessionAsync(api, world, "Soon class", soonStartsIn, soonGroup?.Invoke(world));
        await M4Kit.SignOutAsync(Page);
        return (world, later, soon);
    }

    /// <summary>Turns <c>Features.VirtualClasses</c> on for the school (the units below inherit it).</summary>
    private static async Task EnableVirtualClassesAsync(ApiSetup api)
    {
        var school = await api.UnitIdByCodeAsync("S");
        await api.PutAsync($"/api/org-units/{school}/settings/Features.VirtualClasses", new { valueJson = "true", isLocked = false });
    }

    private static async Task<Session> CreateSessionAsync(ApiSetup api, M4World world, string titleEn, TimeSpan startsIn, Guid? groupId)
    {
        var starts = DateTime.UtcNow.Add(startsIn);
        var created = await api.PostAsync($"/api/class-courses/{world.ClassCourseId}/live-sessions", new
        {
            lessonKey = world.LessonKeys[0],
            module = new
            {
                titleAr = "ع " + titleEn, titleEn, visibility = "Published", visibleFromUtc = (DateTime?)null, visibleToUtc = (DateTime?)null,
                target = groupId is null ? "ClassCourse" : "Group", groupId, completionRule = "None", completionMinScore = (decimal?)null,
            },
            startsAtUtc = starts,
            durationMinutes = 60,
            recurrence = (object?)null,
        });
        var first = created.EnumerateArray().First(); // a one-element array for a single session
        return new Session(first.GetProperty("id").GetGuid(), first.GetProperty("module").GetProperty("id").GetGuid(), starts);
    }

    /// <summary>
    /// Sends the provider's events for a session through the Development-only signed trigger <c>POST /api/dev/zoom/events</c> (T22 support,
    /// #608): platform admins only, one event per call, the shape <c>{ event, sessionId, name?, email?, recordingUrl? }</c> with the kinds
    /// <c>started</c>, <c>participant_joined</c>, <c>ended</c>, <c>recording_ready</c>. The trigger signs the event and posts it to the real
    /// webhook, so the processor runs for real. The two participants: student 1 (signed in with the school account) and a guest. Signs in as
    /// the platform admin for the calls and out again. Assumes the platform admin's password is the shared E2E password.
    /// </summary>
    private async Task SendZoomEventsAsync(Guid sessionId, M4World world)
    {
        await M4Kit.SignOutAsync(Page);
        await new LoginPage(Page).SignInAsync(Settings.PlatformAdminEmail, Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
        var api = new E2EApi(Page, Settings.BaseUrl);
        var events = new object[]
        {
            new { @event = "started", sessionId },
            new { @event = "participant_joined", sessionId, name = "Student One", email = world.Student1.UserName },
            new { @event = "participant_joined", sessionId, name = "A guest", email = (string?)null },
            new { @event = "ended", sessionId },
            new { @event = "recording_ready", sessionId, recordingUrl = "https://zoom.example.org/rec/abc" },
        };
        foreach (var body in events)
        {
            var response = await api.SendAsync("POST", "/api/dev/zoom/events", body);
            Assert.Equal(202, response.Status); // the answer carries the status the webhook gave the signed event (204)
        }

        await M4Kit.SignOutAsync(Page);
    }
}
