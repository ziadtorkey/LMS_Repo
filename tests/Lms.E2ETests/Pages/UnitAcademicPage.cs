using System.Text.RegularExpressions;
using Lms.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The Academic tab of a unit's settings page (/org-units/{id}, FE Task 26, #114).</summary>
public sealed class UnitAcademicPage(IPage page, string culture)
{
    public ILocator TransferPath => page.GetByTestId("transfer-path");

    public async Task OpenAsync(string unitId)
    {
        await page.GotoAsync($"/org-units/{unitId}");
        await page.GetByTestId("unit-tabs").WaitForAsync();
        await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = Resx.In(culture, "UnitSettings_Academic") }).ClickAsync();
        await page.GetByTestId("flag-transfer").WaitForAsync();
    }

    /// <summary>Marks the unit as a level-transfer unit, optionally picks the next level (by shown name), and saves.</summary>
    public async Task MarkTransferAsync(string? nextLevelName = null)
    {
        var toggle = page.Locator("input[data-testid='flag-transfer'], [data-testid='flag-transfer'] input").First;
        if (!await toggle.IsCheckedAsync())
        {
            await toggle.CheckAsync();
        }

        if (nextLevelName is not null)
        {
            await page.GetByTestId("next-level").ClickAsync();
            await page.Locator(".mud-popover-open .mud-list-item")
                .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^\\s*{Regex.Escape(nextLevelName)}\\s*$") })
                .First.ClickAsync();
        }

        await page.GetByTestId("academic-save").ClickAsync();
        var saved = page.Locator(".mud-snackbar");
        var error = page.GetByTestId("academic-error");
        await Assertions.Expect(saved.Or(error).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        if (await error.IsVisibleAsync())
        {
            throw new InvalidOperationException($"Academic save failed: {await error.InnerTextAsync()}");
        }

        await page.Mouse.MoveAsync(0, 0);
    }
}
