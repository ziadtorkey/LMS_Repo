using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>
/// Drives MudBlazor inputs the way a user does. Selects open a popover list; date pickers are read-only inputs that open a
/// calendar (Gregorian, Latin digits in both languages), so a date is picked by stepping months from the one shown.
/// </summary>
public static class MudControls
{
    /// <summary>Opens a MudSelect and clicks the option containing <paramref name="optionText"/>.</summary>
    public static async Task SelectAsync(IPage page, ILocator select, string optionText)
    {
        await select.ClickAsync();
        var option = page.Locator(".mud-popover-open .mud-list-item").Filter(new LocatorFilterOptions { HasText = optionText }).First;
        await option.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Opens the date picker of an empty input (the calendar starts at the current month), steps to the target month
    /// and clicks the day. The picker closes when the input loses focus, so the caller clicks elsewhere afterwards.
    /// </summary>
    public static async Task PickDateAsync(IPage page, ILocator input, DateOnly date)
    {
        await input.ClickAsync();
        var picker = page.Locator(".mud-picker-open").Last;
        await picker.WaitForAsync();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var steps = (date.Year - today.Year) * 12 + date.Month - today.Month;
        var nav = picker.Locator(steps >= 0 ? ".mud-picker-nav-button-next" : ".mud-picker-nav-button-prev");
        for (var i = 0; i < Math.Abs(steps); i++)
        {
            await nav.ClickAsync();
        }

        var day = picker.Locator("button.mud-picker-calendar-day:not(.mud-adjacent-month):not(.mud-hidden)")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^\\s*{date.Day}\\s*$") });
        await day.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(input).ToHaveValueAsync(new Regex(".+"));
    }

    /// <summary>Closes an open picker popover by clicking a neutral spot (the dialog title) and waits until it is gone.</summary>
    public static async Task ClosePickersAsync(IPage page)
    {
        if (await page.Locator(".mud-picker-open").CountAsync() == 0)
        {
            return;
        }

        await page.Locator(".mud-dialog-title, .mud-dialog-content").First.ClickAsync(new LocatorClickOptions { Position = new Position { X = 2, Y = 2 } });
        await Microsoft.Playwright.Assertions.Expect(page.Locator(".mud-picker-open")).ToHaveCountAsync(0);
    }
}
