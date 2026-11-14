using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>
/// M5 timetable and attendance page objects (plan Task 43, #621). DRAFT: the pages do not exist yet (Tasks 31 to 34). Locators use the
/// CSS classes of the design spec (<c>docs/design/m5-assessment-timetable-attendance.md</c> section 9, which the FE must use) where the
/// spec fixes them, and <c>data-testid</c>s that are proposed here and must be confirmed with FE2 in Task 39 (marked "proposed").
/// </summary>
public sealed class TimetableSetupPage(IPage page)
{
    public ILocator Title => page.GetByTestId("tt-setup-title"); // proposed
    public ILocator AddPeriod => page.GetByTestId("tt-add-period"); // proposed
    public ILocator PeriodRows => page.Locator("[data-testid^='tt-period-']"); // proposed
    public ILocator WorkingDay(DayOfWeek day) => page.GetByTestId($"tt-day-{day}"); // proposed, a toggle chip with aria-pressed
    public ILocator ConflictBlock => page.GetByTestId("tt-mode-block"); // proposed
    public ILocator ConflictWarn => page.GetByTestId("tt-mode-warn"); // proposed
    public ILocator Save => page.GetByTestId("tt-setup-save"); // proposed

    public async Task OpenAsync(Guid unitId)
    {
        await page.GotoAsync($"/org-units/{unitId}/timetable-setup"); // route proposed
        await Title.WaitForAsync();
    }

    public async Task SetWorkingDaysAsync(params DayOfWeek[] days)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var on = days.Contains(day);
            if (await WorkingDay(day).GetAttributeAsync("aria-pressed") != (on ? "true" : "false"))
            {
                await WorkingDay(day).ClickAsync();
            }
        }
    }
}

/// <summary>The weekly timetable builder and read-only views (<c>lms-tt</c>, <c>lms-slot</c>; spec section 4).</summary>
public sealed class TimetableBuilderPage(IPage page)
{
    public ILocator Grid => page.Locator(".lms-tt"); // spec class
    public ILocator Slots => page.Locator(".lms-slot:not(.empty)"); // spec classes
    public ILocator EmptyCells => page.Locator(".lms-slot.empty"); // spec classes: "Add a lesson"
    public ILocator ConflictSlots => page.Locator(".lms-slot.conflict"); // spec class
    public ILocator StateChip => page.GetByTestId("tt-state"); // proposed: Draft / Published
    public ILocator Publish => page.GetByTestId("tt-publish"); // proposed
    public ILocator PublishConfirm => page.GetByTestId("tt-publish-confirm"); // proposed
    public ILocator ConflictsPanel => page.GetByTestId("tt-conflicts"); // proposed ("Conflicts (n)" in warn mode)
    public ILocator Error => page.GetByTestId("tt-error"); // proposed: the server's refusal text in block mode
    public ILocator SubstituteBadges => page.Locator("[data-testid^='tt-badge-substitute']"); // proposed
    public ILocator CancelledBadges => page.Locator("[data-testid^='tt-badge-cancelled']"); // proposed

    // The slot dialog (course, teacher, room as plain text).
    public ILocator DialogCourse => page.GetByTestId("slot-course"); // proposed
    public ILocator DialogTeacher => page.GetByTestId("slot-teacher"); // proposed
    public ILocator DialogRoom => page.GetByTestId("slot-room"); // proposed
    public ILocator DialogSave => page.GetByTestId("slot-save"); // proposed

    public async Task OpenAsync(Guid classId)
    {
        await page.GotoAsync($"/classes/{classId}/timetable"); // route proposed
        await Grid.WaitForAsync();
    }

    /// <summary>The cell of a period row and a day column (periods are rows, working days are columns in reading order).</summary>
    public ILocator Cell(int periodIndex, DayOfWeek day) => page.Locator($"[data-period='{periodIndex}'][data-day='{day}']"); // proposed

    /// <summary>Adds a lesson in a cell through the slot dialog (the "+" of an empty cell).</summary>
    public async Task AddLessonAsync(int periodIndex, DayOfWeek day, string course, string teacher, string room)
    {
        await Cell(periodIndex, day).Locator(".lms-slot.empty").ClickAsync();
        await DialogCourse.FillAsync(course);
        await DialogTeacher.FillAsync(teacher);
        await DialogRoom.FillAsync(room);
        await DialogSave.ClickAsync();
    }
}

/// <summary>Today's sessions (<c>/attendance</c>, <c>lms-session</c> cards; spec section 5.1).</summary>
public sealed class AttendanceTodayPage(IPage page)
{
    public ILocator Sessions => page.Locator(".lms-session-card"); // #636
    public ILocator Date => page.GetByTestId("attendance-date"); // #636
    public ILocator Empty => page.GetByTestId("att-empty"); // proposed
    public ILocator SubstituteChip(Guid sessionId) => page.GetByTestId($"session-substitute-{sessionId}"); // #636 chip
    public ILocator EditWindowClosed(Guid sessionId) => page.GetByTestId($"session-locked-{sessionId}"); // #636 chip
    public ILocator Take(Guid sessionId) => page.GetByTestId($"session-primary-{sessionId}"); // #636 (Take attendance / Review)
    public ILocator Card(Guid sessionId) => page.GetByTestId($"session-state-{sessionId}"); // #636 state chip of the card

    public async Task OpenAsync()
    {
        await page.GotoAsync("/attendance");
        await page.Locator(".lms-session-card").Or(Empty).First.WaitForAsync();
    }
}

/// <summary>Taking attendance (<c>/attendance/sessions/{id}</c>; rows <c>lms-att-row</c>, four labelled buttons, sticky bar; spec section 5.2).</summary>
public sealed class AttendanceTakePage(IPage page)
{
    public ILocator Rows => page.Locator(".lms-att-row"); // spec class
    public ILocator MarkAllPresent => page.GetByTestId("att-mark-all"); // #637
    public ILocator NextUnmarked => page.GetByTestId("att-next"); // #637
    public ILocator Search => page.GetByTestId("att-search"); // #637
    public ILocator Bar => page.Locator(".lms-att-bar"); // spec class
    public ILocator Summary => page.GetByTestId("att-summary"); // #637 summary bar
    public ILocator Save => page.GetByTestId("att-save"); // #637
    public ILocator Reason => page.GetByTestId("att-reason"); // #637 required after the edit window
    public ILocator DraftBanner => page.GetByTestId("att-draft-banner"); // #637
    public ILocator Error => page.GetByTestId("att-save-error"); // #637

    /// <summary>The row of a student (test id proposed: att-row-{studentId}).</summary>
    public ILocator Row(Guid studentId) => page.GetByTestId($"att-row-{studentId}");

    /// <summary>One of the four status buttons of a row (#637: att-{studentId}-{Present|Absent|Late|Excused}).</summary>
    public ILocator Status(Guid studentId, string status) => page.GetByTestId($"att-{studentId}-{status}");

    public async Task OpenAsync(Guid sessionId)
    {
        await page.GotoAsync($"/attendance/sessions/{sessionId}");
        await Rows.First.WaitForAsync();
    }

    /// <summary>True when the page does not scroll sideways (the 360 px rule of the design spec).</summary>
    public async Task<bool> NoHorizontalScrollAsync() =>
        await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth");
}
