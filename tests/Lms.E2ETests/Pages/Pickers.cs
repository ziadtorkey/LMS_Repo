using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>Helpers for MudBlazor pickers shared by the M3 page objects.</summary>
public static class Pickers
{
    /// <summary>
    /// Opens a MudSelect and picks the option whose text is exactly <paramref name="text"/> (indent ignored). Several
    /// units can share a name ("Section 1" under each stage) and the list order follows the culture's sort, so with
    /// <paramref name="afterText"/> the pick is the first match after the option with that text.
    /// </summary>
    public static async Task PickAsync(IPage page, string testId, string text, string? afterText = null)
    {
        await page.GetByTestId(testId).ClickAsync();
        await ClickOptionAsync(page, testId, text, afterText);
    }

    /// <summary>Picks an option of a multi-selection MudSelect; the popup stays open, so it is closed with Escape.</summary>
    public static async Task PickInMultiAsync(IPage page, string testId, string text, string? afterText = null)
    {
        await page.GetByTestId(testId).ClickAsync();
        await ClickOptionAsync(page, testId, text, afterText);
        await page.Keyboard.PressAsync("Escape");
    }

    /// <summary>
    /// Picks a date in a MudDatePicker (its input is read-only): opens the calendar on the browser's current month,
    /// pages forward to the target month and clicks the day (a click closes the popup).
    /// </summary>
    public static async Task PickDateAsync(IPage page, string testId, DateOnly date)
    {
        await page.Locator($"input[data-testid='{testId}']").ClickAsync();
        var popup = page.Locator(".mud-popover-open .mud-picker-calendar-content");
        await popup.WaitForAsync();
        var today = DateTime.Today;
        var months = (date.Year - today.Year) * 12 + date.Month - today.Month;
        for (var i = 0; i < months; i++)
        {
            await popup.Locator(".mud-picker-nav-button-next").ClickAsync();
        }

        await popup.Locator(".mud-picker-calendar-day.mud-day:not(.mud-adjacent-month)")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^\\s*" + date.Day + "\\s*$") })
            .First.ClickAsync();
        await popup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    private static async Task ClickOptionAsync(IPage page, string testId, string text, string? afterText)
    {
        var items = page.Locator(".mud-popover-open .mud-list-item");
        await items.First.WaitForAsync();
        var texts = (await items.AllInnerTextsAsync()).Select(t => t.Trim()).ToList();
        var start = afterText is null ? 0 : texts.IndexOf(afterText);
        if (start < 0)
        {
            throw new InvalidOperationException($"Option '{afterText}' not found in {testId}: {string.Join(" | ", texts)}");
        }

        var index = texts.FindIndex(start, t => t == text);
        if (index < 0)
        {
            throw new InvalidOperationException($"Option '{text}' not found in {testId}: {string.Join(" | ", texts)}");
        }

        await items.Nth(index).ClickAsync();
    }
}
