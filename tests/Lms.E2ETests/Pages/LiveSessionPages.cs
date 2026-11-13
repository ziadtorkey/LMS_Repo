using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The staff live sessions tab of a class course (/class-courses/{id}/live-sessions, M4b Task 14, #515): the list of sessions with
/// Upcoming / Past, and the row actions. Locators follow the test ids of the page (draft: re-check against the merged page).
/// </summary>
public sealed class LiveSessionsPage(IPage page)
{
    public ILocator Title => page.GetByTestId("live-title");
    public ILocator Schedule => page.GetByTestId("live-schedule");
    public ILocator Table => page.GetByTestId("live-table");
    public ILocator Empty => page.GetByTestId("live-empty");
    public ILocator Forbidden => page.GetByTestId("live-forbidden");
    public ILocator ProviderNotConfigured => page.GetByTestId("live-provider-not-configured");
    public ILocator ProviderUnhealthy => page.GetByTestId("live-provider-unhealthy");
    public ILocator Upcoming => page.GetByTestId("live-filter-upcoming");
    public ILocator Past => page.GetByTestId("live-filter-past");

    /// <summary>Every session row of the table, in the order shown.</summary>
    public ILocator Rows => page.Locator("[data-testid^='live-row-']");

    public ILocator Row(Guid sessionId) => page.GetByTestId($"live-row-{sessionId}");

    public ILocator Edit(Guid sessionId) => page.GetByTestId($"live-edit-{sessionId}");
    public ILocator RecordingAction(Guid sessionId) => page.GetByTestId($"live-recording-{sessionId}");
    public ILocator Cancel(Guid sessionId) => page.GetByTestId($"live-cancel-{sessionId}");

    /// <summary>The "Weekly · 3 of 12" caption of a series row.</summary>
    public ILocator SeriesCaption(Guid sessionId) => page.GetByTestId($"live-series-{sessionId}");

    /// <summary>The status chip of every row, in order (<c>data-status</c> carries the status code).</summary>
    public ILocator StatusChips => Table.GetByTestId("live-status");

    public async Task OpenAsync(Guid classCourseId)
    {
        await page.GotoAsync($"/class-courses/{classCourseId}/live-sessions");
        await Title.WaitForAsync();
    }

    public async Task<LiveSessionDialog> OpenScheduleAsync()
    {
        await Schedule.ClickAsync();
        var dialog = new LiveSessionDialog(page);
        await dialog.Save.WaitForAsync();
        return dialog;
    }
}

/// <summary>The schedule / edit dialog: session, time, repeat (with its preview) and the shared module settings.</summary>
public sealed class LiveSessionDialog(IPage page)
{
    public ILocator Save => page.GetByTestId("live-save");
    public ILocator Error => page.GetByTestId("live-dialog-error");
    public ILocator TimeZone => page.GetByTestId("live-zone");

    // Titles come from the shared bilingual fields of the module form (module-title-ar / module-title-en), like the lesson plan dialogs.
    public ILocator TitleAr => page.GetByTestId("module-title-ar").Locator("input");
    public ILocator TitleEn => page.GetByTestId("module-title-en").Locator("input");

    public ILocator Lesson => page.GetByTestId("live-lesson");
    public ILocator Date => page.GetByTestId("live-date").Locator("input");
    public ILocator Time => page.GetByTestId("live-time").Locator("input");
    public ILocator Duration => page.GetByTestId("live-duration");

    public ILocator Once => page.GetByTestId("repeat-once");
    public ILocator Weekly => page.GetByTestId("repeat-weekly");
    public ILocator Interval => page.GetByTestId("repeat-interval");
    public ILocator Days => page.GetByTestId("repeat-days");
    public ILocator Day(DayOfWeek day) => page.GetByTestId($"repeat-day-{day}");
    public ILocator EndsOnDate => page.GetByTestId("ends-on-date");
    public ILocator EndsAfter => page.GetByTestId("ends-after");
    public ILocator EndsDate => page.GetByTestId("ends-date").Locator("input");
    public ILocator EndsCount => page.GetByTestId("ends-count").Locator("input");

    public ILocator PreviewCount => page.GetByTestId("recurrence-count");
    public ILocator PreviewDates => page.GetByTestId("recurrence-dates");
    public ILocator PreviewTooMany => page.GetByTestId("recurrence-too-many");

    public async Task SetTitlesAsync(string ar, string en)
    {
        await TitleAr.FillAsync(ar);
        await TitleEn.FillAsync(en);
    }

    /// <summary>Picks the lesson by its visible title.</summary>
    public Task PickLessonAsync(string lessonTitle) => MudControls.SelectAsync(page, Lesson, lessonTitle);

    /// <summary>
    /// Sets the date through the picker and the time by typing into the picker's input (24 h, <c>HH:mm</c>). The caller clicks elsewhere
    /// afterwards because a picker closes when it loses focus. Draft: confirm the time input is editable on the merged page.
    /// </summary>
    public async Task SetStartAsync(DateOnly date, string time)
    {
        await MudControls.PickDateAsync(page, Date, date);
        await MudControls.ClosePickersAsync(page);
        await Time.FillAsync(time);
        await Time.PressAsync("Enter");
    }

    /// <summary>Weekly on the given days, ending after <paramref name="count"/> sessions.</summary>
    public async Task SetWeeklyAsync(int count, params DayOfWeek[] days)
    {
        await Weekly.ClickAsync();
        foreach (var day in days)
        {
            if (await Day(day).GetAttributeAsync("aria-pressed") != "true")
            {
                await Day(day).ClickAsync();
            }
        }

        await EndsAfter.ClickAsync();
        await EndsCount.FillAsync(count.ToString());
        await EndsCount.PressAsync("Tab");
    }

    /// <summary>Saves and waits until the dialog has closed.</summary>
    public async Task SaveAsync()
    {
        await Save.ClickAsync();
        await Save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }
}

/// <summary>The "this session / this and following" prompt before editing or cancelling a session of a series.</summary>
public sealed class SeriesScopeDialog(IPage page)
{
    public ILocator Root => page.GetByTestId("series-scope");
    public ILocator This => page.GetByTestId("scope-this");
    public ILocator Following => page.GetByTestId("scope-following");
    public ILocator Continue => page.GetByTestId("scope-continue");
    public ILocator Back => page.GetByTestId("scope-back");
}

/// <summary>The recording link dialog (a teacher pastes a link when cloud recording is off).</summary>
public sealed class RecordingLinkDialog(IPage page)
{
    public ILocator Url => page.GetByTestId("recording-url").Locator("input");
    public ILocator Host => page.GetByTestId("recording-host");
    public ILocator Source => page.GetByTestId("recording-source");
    public ILocator Error => page.GetByTestId("recording-error");
    public ILocator Save => page.GetByTestId("recording-save");

    public async Task SaveLinkAsync(string url)
    {
        await Url.FillAsync(url);
        await Save.ClickAsync();
        await Save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }
}

/// <summary>The staff participants dialog (matched students and guests) opened from the session panel or the list.</summary>
public sealed class ParticipantsDialog(IPage page)
{
    public ILocator Root => page.GetByTestId("participants-dialog");
    public ILocator Table => page.GetByTestId("participants-table");
    public ILocator Empty => page.GetByTestId("participants-empty");
    public ILocator MatchChips => page.Locator("[data-testid^='participant-match-']");
    public ILocator Close => page.GetByTestId("participants-close");
}

/// <summary>
/// The live session module in the viewer (/class-courses/{id}/modules/{moduleId}, M4b Task 15, #510): the summary, the join zone with
/// its countdown, the join button and the states after the session (recording, none, cancelled).
/// </summary>
public sealed class LiveSessionPanel(IPage page)
{
    public ILocator Panel => page.GetByTestId("live-panel");
    public ILocator Title => page.GetByTestId("live-title");
    public ILocator Meta => page.GetByTestId("live-meta");
    public ILocator Status => page.GetByTestId("live-status");
    public ILocator JoinZone => page.GetByTestId("live-join-zone");
    public ILocator Countdown => page.GetByTestId("live-countdown");
    public ILocator Caption => page.GetByTestId("live-caption");
    public ILocator Join => page.GetByTestId("join-button");
    public ILocator JoinBlocked => page.GetByTestId("join-blocked");
    public ILocator JoinFallbackLink => page.GetByTestId("join-fallback-link");
    public ILocator JoinError => page.GetByTestId("join-error");
    public ILocator JoinRetry => page.GetByTestId("join-retry");
    public ILocator Ended => page.GetByTestId("live-ended");
    public ILocator Recording => page.GetByTestId("live-recording");
    public ILocator RecordingHost => page.GetByTestId("live-recording-host");
    public ILocator NoRecording => page.GetByTestId("live-no-recording");
    public ILocator Cancelled => page.GetByTestId("live-cancelled");
    public ILocator FeatureOff => page.GetByTestId("live-feature-off");
    public ILocator NotAvailable => page.GetByTestId("live-not-available");

    /// <summary>Staff only: the participants and recording-link actions.</summary>
    public ILocator Participants => page.GetByTestId("live-participants");
    public ILocator RecordingEdit => page.GetByTestId("live-recording-edit");

    public async Task OpenAsync(Guid classCourseId, Guid moduleId) =>
        await page.GotoAsync($"/class-courses/{classCourseId}/modules/{moduleId}");
}
