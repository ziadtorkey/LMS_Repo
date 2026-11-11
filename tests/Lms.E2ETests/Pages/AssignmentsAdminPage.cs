using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>The role assignments admin page (/assignments, FE Task 30).</summary>
public sealed class AssignmentsAdminPage(IPage page)
{
    public async Task OpenAsync()
    {
        await page.GotoAsync("/assignments");
        await Expect(page.GetByTestId("assign-new")).ToBeVisibleAsync();
    }

    /// <summary>Assigns <paramref name="roleKey"/> (a built-in role key, e.g. Teacher) at the unit with this code.</summary>
    public async Task AssignAsync(string email, string nameAr, string nameEn, string roleKey, string unitCode)
    {
        await page.GetByTestId("assign-new").ClickAsync();
        await Field("assign-email").FillAsync(email);
        await Field("assign-name-ar").FillAsync(nameAr);
        await Field("assign-name-en").FillAsync(nameEn);
        await Pick("assign-role", $"assign-role-option-{roleKey}");
        await Pick("assign-unit", $"assign-unit-option-{unitCode}");
        await page.GetByTestId("assign-save").ClickAsync();
        await Expect(page.GetByTestId("assign-save")).ToHaveCountAsync(0);
    }

    /// <summary>Lists the assignments of the whole tree (the page opens on the root unit without its children).</summary>
    public async Task IncludeUnitsBelowAsync()
    {
        await page.Locator("input[data-testid='include-below'], [data-testid='include-below'] input").First.CheckAsync();
    }

    public ILocator Row(string email) => page.Locator("tbody tr", new PageLocatorOptions { HasTextString = email });

    private async Task Pick(string selectTestId, string optionTestId)
    {
        await page.Locator($"[data-testid='{selectTestId}']").First.ClickAsync();
        await page.GetByTestId(optionTestId).ClickAsync();
    }

    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input").First;
}
