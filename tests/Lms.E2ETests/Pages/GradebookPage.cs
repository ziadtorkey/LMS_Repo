using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The gradebook of a class course (M5 plan Tasks 22-26). The shell and the tab exist in #630 (<c>gradebook-title</c>, <c>gradebook-soon</c>,
/// <c>gradebook-forbidden</c>, <c>tab-gradebook</c> of <c>class-course-tabs</c>); the grid ids below are expectations to align with Tasks 24-26 when
/// those pages land.
/// </summary>
public sealed class GradebookPage(IPage page)
{
    // Shell (#630)
    public ILocator Tabs => page.GetByTestId("class-course-tabs");

    public ILocator Tab => page.GetByTestId("tab-gradebook");

    public ILocator Title => page.GetByTestId("gradebook-title");

    public ILocator GridSoon => page.GetByTestId("gradebook-soon");

    public ILocator Forbidden => page.GetByTestId("gradebook-forbidden");

    // Grid (Tasks 24-26): to be aligned
    public ILocator Grid => page.GetByTestId("gradebook-grid");

    public ILocator WeightsChip => page.GetByTestId("gradebook-weights");

    public ILocator PublishButton => page.GetByTestId("gradebook-publish");

    public ILocator Cell(Guid studentId, Guid itemId) => page.GetByTestId($"grade-cell-{studentId}-{itemId}");

    /// <summary>Opens the gradebook by URL and waits for either the page title or the forbidden notice (a student has no Grades.View).</summary>
    public async Task OpenAsync(Guid classCourseId)
    {
        await page.GotoAsync($"/class-courses/{classCourseId}/gradebook");
        await Expect(Title.Or(Forbidden)).ToBeVisibleAsync();
    }

    /// <summary>From any tab of the class course (the plan, for example): click the Gradebook tab.</summary>
    public async Task OpenFromTabsAsync()
    {
        await Tab.ClickAsync();
        await Expect(Title).ToBeVisibleAsync();
    }
}
