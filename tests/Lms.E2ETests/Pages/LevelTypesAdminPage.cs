using Lms.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The client root admin's level types page (/level-types, FE Task 24, #81).</summary>
public sealed class LevelTypesAdminPage(IPage page, string culture)
{
    public ILocator Title => page.GetByTestId("level-types-title");
    public ILocator Error => page.GetByTestId("level-error");
    public ILocator Rows => page.Locator("tr[data-testid^='level-row-']");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/level-types");
        await Title.WaitForAsync();
    }

    /// <summary>Adds a level type through the dialog; returns once the new row is shown.</summary>
    public async Task AddAsync(string nameAr, string nameEn, bool canHostClasses)
    {
        var before = await Rows.CountAsync();
        await page.GetByTestId("level-new").ClickAsync();
        await Field("level-name-ar").FillAsync(nameAr);
        await Field("level-name-en").FillAsync(nameEn);
        if (canHostClasses)
        {
            await page.GetByLabel(Resx.In(culture, "LevelTypes_CanHostClasses")).CheckAsync();
        }

        await page.GetByTestId("dialog-save").ClickAsync();
        await Assertions.Expect(Rows).ToHaveCountAsync(before + 1);
        await DismissSnackbarsAsync();
    }

    /// <summary>
    /// The "Saved." snackbar sits over the top-right action button and pauses while hovered (the mouse rests there after
    /// clicking Save). Move the mouse away so its timer runs, then wait until it has gone (its close button re-renders
    /// while animating, so clicking it is racy).
    /// </summary>
    private async Task DismissSnackbarsAsync()
    {
        await page.Mouse.MoveAsync(0, 0);
        await Assertions.Expect(page.Locator(".mud-snackbar"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
    }

    /// <summary>The level names in display order, in the current UI language.</summary>
    public async Task<IReadOnlyList<string>> NamesInOrderAsync()
    {
        var names = new List<string>();
        foreach (var row in await Rows.AllAsync())
        {
            names.Add((await row.Locator("td").Nth(1).InnerTextAsync()).Trim());
        }

        return names;
    }

    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input").First;
}
