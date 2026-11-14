using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The gradebook of a class course (M5 plan Tasks 22-26). Test ids follow FE1's section of docs/m5-e2e-testids.md (#710): shell (#630), grid (#660, #663),
/// settings panel (#664).
/// </summary>
public sealed class GradebookPage(IPage page)
{
    // Shell
    public ILocator Tab => page.GetByTestId("tab-gradebook");

    public ILocator Title => page.GetByTestId("gradebook-title");

    public ILocator Forbidden => page.GetByTestId("gradebook-forbidden");

    // Grid
    public ILocator Grid => page.GetByTestId("gradebook-grid");

    public ILocator GridWrap => page.GetByTestId("gradebook-grid-wrap");

    public ILocator WeightsChip => page.GetByTestId("weights-chip");

    public ILocator PublishButton => page.GetByTestId("gradebook-publish");

    public ILocator Hint => page.GetByTestId("gradebook-hint");

    public ILocator LivePolite => page.GetByTestId("grid-live-polite");

    public ILocator LiveAssertive => page.GetByTestId("grid-live-assertive");

    public ILocator ItemHeader(Guid itemId) => page.GetByTestId($"item-{itemId}");

    public ILocator ItemState(Guid itemId) => page.GetByTestId($"item-state-{itemId}");

    public ILocator Cell(Guid studentId, Guid itemId) => page.GetByTestId($"cell-{studentId}-{itemId}");

    public ILocator CellInput(Guid studentId, Guid itemId) => page.GetByTestId($"edit-{studentId}-{itemId}");

    public ILocator Final(Guid studentId) => page.GetByTestId($"final-{studentId}");

    public ILocator Band(Guid studentId) => page.GetByTestId($"band-{studentId}");

    public ILocator StudentButton(Guid studentId) => page.GetByTestId($"student-{studentId}");

    // Settings panel (the gear in the header)
    public ILocator SettingsButton => page.GetByTestId("gradebook-settings");

    public ILocator Config => page.GetByTestId("gradebook-config");

    public ILocator CategoryName(int n, string language) => page.GetByTestId($"cat-name-{language}-{n}");

    public ILocator CategoryWeight(int n) => page.GetByTestId($"cat-weight-{n}");

    public ILocator AddCategory => page.GetByTestId("cat-add");

    public ILocator SaveCategories => page.GetByTestId("cat-save");

    public ILocator AddItem => page.GetByTestId("item-add");

    public ILocator ItemTitle(string language) => page.GetByTestId($"item-title-{language}");

    public ILocator ItemMax => page.GetByTestId("item-max");

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

    /// <summary>Opens the settings panel (categories, items, scale) and waits for it.</summary>
    public async Task OpenSettingsAsync()
    {
        await SettingsButton.ClickAsync();
        await Expect(Config).ToBeVisibleAsync();
    }

    /// <summary>Types a score into a manual cell: Enter starts the edit, the value is typed, Enter saves and stays; waits for the polite save announcement.</summary>
    public async Task TypeScoreAsync(Guid studentId, Guid itemId, string text)
    {
        var cell = Cell(studentId, itemId);
        await cell.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await CellInput(studentId, itemId).FillAsync(text);
        await page.Keyboard.PressAsync("Enter");
        await Expect(cell).ToContainTextAsync(text.Replace(',', '.'));
    }
}
