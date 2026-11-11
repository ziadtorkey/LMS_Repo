using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>The organization tree (/org-units). Nodes carry their unit id in <c>org-node-{id}</c>.</summary>
public sealed class OrgTreePage(IPage page)
{
    public const string NodePrefix = "org-node-";

    public async Task GotoAsync()
    {
        await page.GotoAsync("/org-units");
        await page.GetByTestId("org-title").WaitForAsync();
        await page.Locator($"[data-testid^='{NodePrefix}']").First.WaitForAsync();
    }

    /// <summary>Searches the tree for <paramref name="name"/> (the search reveals the match with its ancestors).</summary>
    public async Task SearchAsync(string? name)
    {
        await page.GetByTestId("org-search").FillAsync(name ?? string.Empty);
        if (!string.IsNullOrEmpty(name))
        {
            await Node(name).WaitForAsync();
        }
    }

    /// <summary>The node whose label is exactly <paramref name="name"/> (seeded or test-created unit names are unique).</summary>
    public ILocator Node(string name) =>
        page.Locator($"[data-testid^='{NodePrefix}']")
            .Filter(new LocatorFilterOptions { Has = page.GetByText(name, new PageGetByTextOptions { Exact = true }) });

    public async Task<Guid> IdOfAsync(string name)
    {
        var testId = await Node(name).First.GetAttributeAsync("data-testid");
        return Guid.Parse(testId![NodePrefix.Length..]);
    }

    /// <summary>Adds a child unit under <paramref name="parentName"/> and returns the new unit's id.</summary>
    public async Task<Guid> AddChildAsync(string parentName, string nameAr, string nameEn, string code, string levelText)
    {
        await SearchAsync(parentName);
        var parentId = await IdOfAsync(parentName);
        await page.GetByTestId($"org-add-{parentId}").ClickAsync();
        await page.GetByTestId("unit-name-ar").FillAsync(nameAr);
        await page.GetByTestId("unit-name-en").FillAsync(nameEn);
        await page.GetByTestId("unit-code").FillAsync(code);
        await MudControls.SelectAsync(page, page.GetByTestId("unit-level-type"), levelText);
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);
        return await FindAsync(nameAr, nameEn);
    }

    /// <summary>Moves unit <paramref name="unitName"/> under <paramref name="newParentName"/> through the move dialog.</summary>
    public async Task MoveAsync(string unitName, string newParentName)
    {
        await SearchAsync(unitName);
        var unitId = await IdOfAsync(unitName);
        await page.GetByTestId($"org-move-{unitId}").ClickAsync();
        await MudControls.SelectAsync(page, page.GetByTestId("move-parent"), newParentName);
        await page.GetByTestId("move-confirm-text").WaitForAsync();
        await page.GetByTestId("dialog-save").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);
    }

    /// <summary>Finds a unit by name in whichever language the page shows.</summary>
    private async Task<Guid> FindAsync(string nameAr, string nameEn)
    {
        var lang = await page.Locator("html").GetAttributeAsync("lang");
        var name = lang == "en" ? nameEn : nameAr;
        await SearchAsync(name);
        return await IdOfAsync(name);
    }
}
