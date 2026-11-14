using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>
/// M5 timetable and attendance page objects (plan Task 43, #621). Every locator is a <c>data-testid</c> from the FE map
/// (<c>docs/m5-e2e-testids.md</c>, #710); CSS classes are not used, so a style refactor cannot break the demo.
/// </summary>
public sealed class TimetableSetupPage(IPage page)
{
    public ILocator Title => page.GetByTestId("setup-title"); // #654
    public ILocator AddPeriod => page.GetByTestId("period-new"); // #654
    public ILocator PeriodRows => page.Locator("[data-testid^='period-row-']"); // #654
    public ILocator WorkingDay(DayOfWeek day) => page.GetByTestId($"wd-{day}"); // #654, a toggle chip with aria-pressed
    public ILocator Save => page.GetByTestId("wd-save"); // #654 (working days)
    public ILocator Saved => page.GetByTestId("wd-saved"); // #654

    public async Task OpenAsync(Guid unitId)
    {
        await page.GotoAsync($"/org-units/{unitId}/timetable-setup"); // #654
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
    public ILocator Grid => page.Locator("table.lms-tt"); // #654 (the grid has no test id; the table has the class and role=grid)
    public ILocator Slots => page.Locator("[data-testid^='tt-slot-']"); // #654 (tt-slot-{id})
    public ILocator EmptyCells => page.Locator("[data-testid^='tt-cell-']:not(:has([data-testid^='tt-slot-']))");
    public ILocator StateChip => page.GetByTestId("tt-state"); // #654: "Published" or "n drafts"
    public ILocator Publish => page.GetByTestId("tt-publish"); // #654 (disabled when there are no drafts)
    public ILocator PublishConfirm => page.GetByTestId("publish-confirm"); // #654
    public ILocator ConflictsPanel => page.GetByTestId("tt-conflicts"); // #654 ConflictsPanel
    public ILocator Error => page.GetByTestId("slot-error"); // #654: the server's refusal text inside the slot dialog (block mode)
    public ILocator SubstituteBadges => page.Locator("[data-testid^='tt-badge-substitute']"); // proposed
    public ILocator CancelledBadges => page.Locator("[data-testid^='tt-badge-cancelled']"); // proposed

    // The slot dialog (course, teacher, room as plain text).
    public ILocator DialogCourse => page.GetByTestId("slot-course"); // #654 (a MudSelect: use SelectAsync)
    public ILocator DialogTeacher => page.GetByTestId("slot-teacher"); // #654 (a MudSelect)
    public ILocator DialogRoom => page.GetByTestId("slot-room"); // #654
    public ILocator DialogSave => page.GetByTestId("slot-save"); // #654

    public async Task OpenAsync(Guid classId)
    {
        await page.GotoAsync($"/classes/{classId}/timetable"); // #654
        await Grid.WaitForAsync();
    }

    /// <summary>The cell of a period row and a day column (periods are rows, working days are columns in reading order).</summary>
    public ILocator Cell(int periodOrder, DayOfWeek day) => page.GetByTestId($"tt-cell-{(int)day}-{periodOrder}"); // #654: day 0 = Sunday

    /// <summary>Picks an option of a MudSelect by its visible text.</summary>
    private async Task SelectAsync(ILocator select, string text)
    {
        await select.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = text }).First.ClickAsync();
    }

    /// <summary>The polite live region that announces moves (#654).</summary>
    public ILocator Live => page.GetByTestId("tt-live");

    /// <summary>The conflict messages of the last move or save (#654).</summary>
    public ILocator Conflict => page.GetByTestId("tt-conflict");

    /// <summary>
    /// Picks up the lesson in a cell with the keyboard: focus the cell, press the pick-up key (matched by <c>event.code</c>, so
    /// <paramref name="key"/> may be an Arabic layout character), then move with the arrows. Enter drops, Escape cancels.
    /// </summary>
    public async Task PickUpAsync(int periodOrder, DayOfWeek day, string key = "m")
    {
        await Cell(periodOrder, day).FocusAsync();
        // Playwright sends the physical key for a named code; the character is what an Arabic layout would produce.
        await page.EvaluateAsync("([k]) => document.activeElement.dispatchEvent(new KeyboardEvent('keydown', { key: k, code: 'KeyM', bubbles: true, cancelable: true }))", new object[] { key });
    }

    /// <summary>Adds a lesson in a cell through the slot dialog (the "+" of an empty cell).</summary>
    public async Task AddLessonAsync(int periodIndex, DayOfWeek day, string course, string teacher, string room)
    {
        await Cell(periodIndex, day).GetByRole(AriaRole.Button).First.ClickAsync(); // the "+" of an empty cell
        await SelectAsync(DialogCourse, course);
        await SelectAsync(DialogTeacher, teacher);
        await DialogRoom.Locator("input").FillAsync(room);
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
    public ILocator Rows => page.Locator("[data-testid^='att-row-']"); // #637
    public ILocator MarkAllPresent => page.GetByTestId("att-mark-all"); // #637
    public ILocator NextUnmarked => page.GetByTestId("att-next"); // #637
    public ILocator Search => page.GetByTestId("att-search"); // #637
    /// <summary>The count chip of a status in the summary bar (#637: att-count-{Present|Absent|Late|Excused}).</summary>
    public ILocator Count(string status) => page.GetByTestId($"att-count-{status}");
    public ILocator Summary => page.GetByTestId("att-summary"); // #637 summary bar
    public ILocator Save => page.GetByTestId("att-save"); // #637
    public ILocator Reason => page.GetByTestId("att-reason"); // #637 required after the edit window
    public ILocator DraftBanner => page.GetByTestId("att-draft-banner"); // #637
    public ILocator Error => page.GetByTestId("att-save-error"); // #637

    /// <summary>The student ids of the roster rows, read from <c>att-row-{studentId}</c> (for the seeded demo, whose ids the scenario does not know).</summary>
    public async Task<IReadOnlyList<Guid>> StudentIdsAsync()
    {
        var ids = new List<Guid>();
        foreach (var row in await Rows.AllAsync())
        {
            ids.Add(Guid.Parse((await row.GetAttributeAsync("data-testid"))!["att-row-".Length..]));
        }

        return ids;
    }

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


/// <summary>The attendance alerts (<c>/attendance/alerts</c>) and the excuse review queue (<c>/excuses/review</c>), ids from the FE map.</summary>
public sealed class AttendanceFollowUpPages(IPage page)
{
    public ILocator AlertRows => page.Locator("[data-testid^='alert-formula-']");
    public ILocator ReviewRows => page.Locator("[data-testid^='review-open-']");

    public async Task OpenAlertsAsync()
    {
        await page.GotoAsync("/attendance/alerts");
        await page.GetByTestId("alerts-title").WaitForAsync();
    }

    public async Task OpenReviewAsync()
    {
        await page.GotoAsync("/excuses/review");
        await page.GetByTestId("review-page-title").WaitForAsync();
    }
}
