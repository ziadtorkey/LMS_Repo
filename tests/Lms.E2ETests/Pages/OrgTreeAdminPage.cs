using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The organization tree (/org-units, FE Task 25/26, #112/#114). Nodes are found by their unit code (unique within a
/// client) because names repeat ("Section 1" under several stages) and node test ids carry server-generated ids.
/// </summary>
public sealed class OrgTreeAdminPage(IPage page)
{
    public ILocator Title => page.GetByTestId("org-title");
    public ILocator Error => page.GetByTestId("org-error");
    private ILocator Nodes => page.Locator("[data-testid^='org-node-']");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/org-units");
        await Title.WaitForAsync();
        await Assertions.Expect(Nodes.First).ToBeVisibleAsync();
    }

    /// <summary>The node whose code chip reads exactly <paramref name="code"/>.</summary>
    public ILocator Node(string code) => Nodes.Filter(new LocatorFilterOptions
    {
        Has = page.Locator(".mud-chip").Filter(new LocatorFilterOptions
        {
            HasTextRegex = new Regex($"^\\s*{Regex.Escape(code)}\\s*$"),
        }),
    });

    /// <summary>Searches for the code so the node and its ancestors are shown expanded; returns the node's unit id.</summary>
    public async Task<string> RevealAsync(string code)
    {
        await SearchAsync(code);
        await Assertions.Expect(Node(code)).ToBeVisibleAsync();
        return (await Node(code).GetAttributeAsync("data-testid"))!["org-node-".Length..];
    }

    public async Task SearchAsync(string text)
    {
        var search = page.Locator("input[data-testid='org-search'], [data-testid='org-search'] input").First;
        await search.FillAsync(text);
        await search.PressAsync("Enter");
    }

    /// <summary>Adds a child unit through the dialog; returns once the dialog has closed.</summary>
    public async Task AddChildAsync(string parentCode, string nameAr, string nameEn, string code, string levelName)
    {
        var parentId = await RevealAsync(parentCode);
        await page.GetByTestId($"org-add-{parentId}").ClickAsync();
        await Field("unit-name-ar").FillAsync(nameAr);
        await Field("unit-name-en").FillAsync(nameEn);
        await Field("unit-code").FillAsync(code);
        await page.GetByTestId("unit-level-type").ClickAsync();
        await page.Locator(".mud-popover-open .mud-list-item")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^\\s*{Regex.Escape(levelName)}\\s*$") })
            .First.ClickAsync();
        await SaveDialogAsync();
    }

    /// <summary>Moves the unit under the new parent (picked by its shown name) through the move dialog.</summary>
    public async Task MoveAsync(string code, string newParentName)
    {
        var id = await RevealAsync(code);
        await page.GetByTestId($"org-move-{id}").ClickAsync();
        await page.GetByTestId("move-parent").ClickAsync();
        await page.Locator(".mud-popover-open .mud-list-item")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^\\s*{Regex.Escape(newParentName)}\\s*$") })
            .First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("move-confirm-text")).ToBeVisibleAsync();
        await SaveDialogAsync();
    }

    /// <summary>Clicks delete on the unit and confirms; the caller checks the outcome.</summary>
    public async Task DeleteAsync(string code)
    {
        var id = await RevealAsync(code);
        await page.GetByTestId($"org-delete-{id}").ClickAsync();
        await page.GetByTestId("confirm-yes").ClickAsync();
    }

    /// <summary>The code of the unit the tree shows <paramref name="code"/> nested under (null for a root).</summary>
    public async Task<string?> ParentCodeAsync(string code)
    {
        await RevealAsync(code);
        return await Node(code).EvaluateAsync<string?>(
            """
            el => {
              const parentItem = el.closest('li')?.parentElement?.closest('li');
              const parentNode = parentItem?.querySelector('[data-testid^="org-node-"]');
              return parentNode?.querySelector('.mud-chip')?.textContent.trim() ?? null;
            }
            """);
    }

    public async Task<string> UnitIdAsync(string code) => await RevealAsync(code);

    private async Task SaveDialogAsync()
    {
        await page.GetByTestId("dialog-save").ClickAsync();
        await page.GetByTestId("dialog-save").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.Mouse.MoveAsync(0, 0);
    }

    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input").First;
}
