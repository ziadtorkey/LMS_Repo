using System.Diagnostics;
using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M5;

/// <summary>
/// M5 exit demo 2 (plan Task 43, #621): a timetable is built and published with a conflict, sessions are generated, attendance is taken
/// at 360 px; in Arabic (RTL) and English. SKELETON: every scenario is skipped until the pieces it needs are on main (Tasks 13 to 17 for
/// the API, 31 to 34 and 39 for the pages); the page objects (<see cref="TimetableSetupPage"/>, <see cref="TimetableBuilderPage"/>,
/// <see cref="AttendanceTodayPage"/>, <see cref="AttendanceTakePage"/>) compile now and their proposed test ids are confirmed with FE2 in
/// Task 39. Set-up goes through the API (<see cref="M5Kit"/>); only what a scenario tests is driven through the UI. One E2E stack at a
/// time: take <c>e2e.lock</c> before a run.
/// </summary>
public sealed class TimetableAttendanceTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    // One reason per dependency so un-skipping is a search and replace as the tasks land (plan Task 43 "Depends on").
    private const string NeedsTimetable = "Needs the timetable API (Tasks 13 to 15) and the set-up and builder pages (Task 31) on main";
    private const string NeedsSessions = "Needs session generation on publish (Task 16) and the today and take pages (Tasks 33, 34) on main";
    private const string NeedsExceptions = "Needs timetable exceptions with a substitute (Tasks 15, 32) and attendance (Tasks 17, 33)";
    private const string NeedsWindow = "Needs the edit window and Attendance.Edit with a reason (Task 17, L14) and the take page (Task 34)";
    private const string NeedsGuardian = "Needs the excuse and absence notification path (Tasks 18, 19) and ChildAttendancePage (Task 36)";

    [Theory(Skip = NeedsTimetable)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Admin_sets_periods_and_working_days(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000); // the WASM app takes a while to boot after a full page load
        var world = await SignInAsAdminAndBuildAsync(culture);

        var setup = new TimetableSetupPage(Page);
        await setup.OpenAsync(world.SchoolId);
        await setup.SetWorkingDaysAsync(DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday);
        await setup.AddPeriod.ClickAsync(); // period name and LTR times through the dialog (to be driven when the dialog's ids exist)
        await setup.Save.ClickAsync();
        await Expect(setup.PeriodRows.First).ToBeVisibleAsync();
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory(Skip = NeedsTimetable)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Admin_builds_a_timetable_and_block_mode_refuses_a_teacher_clash(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync(culture, conflictMode: "Block");

        var builder = new TimetableBuilderPage(Page);
        await builder.OpenAsync(world.ClassAId);
        await builder.AddLessonAsync(0, DayOfWeek.Sunday, world.CourseName, world.TeacherName, "R1");
        await Expect(builder.Slots).ToHaveCountAsync(1);

        // The same teacher in the same period in another class: refused with the server's words, the card is not created.
        await builder.OpenAsync(world.ClassBId);
        await builder.AddLessonAsync(0, DayOfWeek.Sunday, world.CourseName, world.TeacherName, "R2");
        await Expect(builder.Error).ToBeVisibleAsync();
        await Expect(builder.Slots).ToHaveCountAsync(0);

        // Direction: days mirror in Arabic, times stay left to right.
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>
    /// Keyboard move (FE2, #654): focus a lesson's cell, pick it up with M (also with an Arabic layout character: the key is matched by
    /// <c>event.code</c>), arrows to an empty cell, Enter drops; the lesson sits in the new cell and the live region says it moved.
    /// Escape mid-move leaves everything where it was.
    /// </summary>
    [Theory(Skip = NeedsTimetable)]
    [InlineData("ar", "ئ")] // the character the physical M key gives on an Arabic layout
    [InlineData("en", "m")]
    public async Task Lesson_moves_with_the_keyboard_and_escape_cancels(string culture, string pickUpKey)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync(culture, withSlots: true);

        var builder = new TimetableBuilderPage(Page);
        await builder.OpenAsync(world.ClassAId);
        var slotId = await builder.Slots.First.GetAttributeAsync("data-testid"); // tt-slot-{id}
        var from = (period: 1, day: DayOfWeek.Sunday); // the first lesson of the world is at the first period of the first day (to be set by M5Kit)

        // Escape mid-move: nothing moved, the card is still in its cell.
        await builder.PickUpAsync(from.period, from.day, pickUpKey);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(builder.Cell(from.period, from.day).GetByTestId(slotId!)).ToHaveCountAsync(1);

        // Pick up, move one period down, drop.
        await builder.PickUpAsync(from.period, from.day, pickUpKey);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Enter");
        await Expect(builder.Cell(from.period + 1, from.day).GetByTestId(slotId!)).ToHaveCountAsync(1);
        await Expect(builder.Cell(from.period, from.day).GetByTestId(slotId!)).ToHaveCountAsync(0);
        await Expect(builder.Live).Not.ToBeEmptyAsync(); // the move is announced
    }

    /// <summary>Phone, 360 px: a quick swipe scrolls the grid, a 300 ms press-and-hold starts a drag (acceptance item of the S3 spike, Task 31).</summary>
    [Fact(Skip = NeedsTimetable)]
    public async Task Touch_hold_starts_a_drag_and_a_swipe_scrolls_at_360px()
    {
        await StartAsync(culture: "en"); // a touch context (HasTouch) is needed: add the option to StartAsync when un-skipping
        await Page.SetViewportSizeAsync(360, 800);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync("en", withSlots: true);

        var builder = new TimetableBuilderPage(Page);
        await builder.OpenAsync(world.ClassAId);
        // To drive with CDP touch events (Input.dispatchTouchEvent): a swipe under 300 ms must change the scroll position and not move the card;
        // a hold of 300 ms or more followed by a move must move the card and not scroll. Needs the real builder (#654) and a touch context.
        Assert.True(await builder.Grid.IsVisibleAsync());
    }

    [Theory(Skip = NeedsSessions)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Publishing_generates_sessions_for_the_teacher(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync(culture, withSlots: true);

        var builder = new TimetableBuilderPage(Page);
        await builder.OpenAsync(world.ClassAId);
        await Expect(builder.StateChip).ToHaveAttributeAsync("data-state", "Draft");
        await builder.Publish.ClickAsync();
        await builder.PublishConfirm.ClickAsync(); // the dialog says sessions will be generated for the coming days
        await Expect(builder.StateChip).ToHaveAttributeAsync("data-state", "Published");

        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        var today = new AttendanceTodayPage(Page);
        await today.OpenAsync();
        await Expect(today.Sessions.First).ToBeVisibleAsync(); // a session of the generated week appears for the teacher (the test day is a working day)
    }

    [Theory(Skip = NeedsExceptions)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Substitute_takes_attendance_on_the_exception_date(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync(culture, withSlots: true, published: true, substituteToday: true);

        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, world.SubstituteEmail, Settings.Password, culture);
        var today = new AttendanceTodayPage(Page);
        await today.OpenAsync();
        await Expect(today.SubstituteChip(world.SessionId)).ToBeVisibleAsync();
        await today.Take(world.SessionId).ClickAsync();
        var take = new AttendanceTakePage(Page);
        await take.MarkAllPresent.ClickAsync();
        await take.Save.ClickAsync();
        await Expect(take.Error).ToHaveCountAsync(0);

        // The original teacher's own view of that date has no edit rights on the substituted session (IsSubstituteOn is valid for that date only).
        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        await today.OpenAsync();
        await Expect(today.Take(world.SessionId)).ToHaveCountAsync(0);
    }

    /// <summary>Phone, 360 px: mark all present, then two exceptions (one absent, one late) and save, in under 30 seconds, without sideways scroll.</summary>
    [Theory(Skip = NeedsSessions)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_takes_attendance_at_360px_with_two_exceptions_in_under_30_seconds(string culture)
    {
        await StartAsync(culture: culture);
        await Page.SetViewportSizeAsync(360, 800);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync(culture, withSlots: true, published: true);

        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        var take = new AttendanceTakePage(Page);
        await take.OpenAsync(world.SessionId);

        var clock = Stopwatch.StartNew();
        await take.MarkAllPresent.ClickAsync();
        await take.Status(world.StudentIds[0], "Absent").ClickAsync();
        await take.Status(world.StudentIds[1], "Late").ClickAsync();
        await Expect(take.Summary).ToContainTextAsync("1"); // counts: the polite status line
        await take.Save.ClickAsync();
        await Expect(take.Error).ToHaveCountAsync(0);
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"Taking attendance took {clock.Elapsed.TotalSeconds:0.0} s");
        Assert.True(await take.NoHorizontalScrollAsync(), "The take page scrolls sideways at 360 px");
        var box = await take.Save.BoundingBoxAsync();
        Assert.True(box is { Height: >= 44 }, "Save is a 44 px target"); // the design spec's phone target size
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    /// <summary>After the edit window the teacher cannot edit; a counselor can, with a required reason, and the change is recorded.</summary>
    [Theory(Skip = NeedsWindow)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_cannot_edit_after_the_window_and_counselor_can_with_a_reason(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync(culture, withSlots: true, published: true, takenAndWindowClosed: true);

        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, world.TeacherEmail, Settings.Password, culture);
        var today = new AttendanceTodayPage(Page);
        await today.OpenAsync();
        await Expect(today.EditWindowClosed(world.SessionId)).ToBeVisibleAsync();

        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, world.CounselorEmail, Settings.Password, culture);
        var take = new AttendanceTakePage(Page);
        await take.OpenAsync(world.SessionId);
        await take.Status(world.StudentIds[0], "Excused").ClickAsync();
        await take.Save.ClickAsync();
        await Expect(take.Error).ToBeVisibleAsync(); // a reason is required
        await take.Reason.FillAsync("Doctor's note seen");
        await take.Save.ClickAsync();
        await Expect(take.Error).ToHaveCountAsync(0);
    }

    /// <summary>The guardian sees the absence and the notice in the child's attendance, and never another child's rows.</summary>
    [Theory(Skip = NeedsGuardian)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Guardian_sees_the_absence_and_the_notice(string culture)
    {
        await StartAsync(culture: culture);
        Page.SetDefaultTimeout(45_000);
        var world = await SignInAsAdminAndBuildAsync(culture, withSlots: true, published: true, takenWithAbsence: true);

        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, world.GuardianUserName, world.GuardianPassword, culture);
        await Page.GotoAsync($"/me/children/{world.StudentIds[0]}/attendance"); // route per the design spec §5a (ChildAttendancePage, Task 36)
        await Expect(Page.GetByText(Resx.In(culture, "Attendance_Absent")).First).ToBeVisibleAsync();
        await Page.GotoAsync("/notifications");
        await Expect(Page.GetByText(Resx.In(culture, "Notif_Attendance_Absent")).First).ToBeVisibleAsync(); // key proposed: no grade or name beyond the label
    }

    // ---------------------------------------------------------------------------------------------------- set-up through the API

    private async Task<M5World> SignInAsAdminAndBuildAsync(
        string culture, string conflictMode = "Warn", bool withSlots = false, bool published = false, bool substituteToday = false,
        bool takenAndWindowClosed = false, bool takenWithAbsence = false)
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var world = await M5Kit.BuildWorldAsync(new ApiSetup(Page), M5Names.Suffix(), conflictMode, withSlots, published, substituteToday, takenAndWindowClosed, takenWithAbsence);
        await M5Kit.SignOutAsync(Page);
        await M5Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        return world;
    }
}
