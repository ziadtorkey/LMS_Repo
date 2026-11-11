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

    /// <summary>
    /// Copies the branch under the target unit (picked by its shown name) with a code suffix and new root names; returns
    /// the dialog's result text (units created) once the copy is done and the dialog is closed.
    /// </summary>
    public async Task<string> CopyBranchAsync(string code, string targetName, string suffix, string nameAr, string nameEn)
    {
        var id = await RevealAsync(code);
        await page.GetByTestId($"org-copy-{id}").ClickAsync();
        await page.GetByTestId("copy-target").ClickAsync();
        await page.Locator(".mud-popover-open .mud-list-item")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^\\s*" + Regex.Escape(targetName) + "\\s*$") })
            .First.ClickAsync();
        await Field("copy-suffix").FillAsync(suffix);
        await Field("copy-name-ar").FillAsync(nameAr);
        await Field("copy-name-en").FillAsync(nameEn);
        await page.GetByTestId("dialog-save").ClickAsync();
        var result = page.GetByTestId("copy-result");
        await result.WaitForAsync();
        var text = await result.InnerTextAsync();
        await page.GetByTestId("dialog-close").ClickAsync();
        await page.GetByTestId("dialog-close").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        return text;
    }

    /// <summary>
    /// Saves the branch as a private template, replacing <paramref name="literal"/> in its names and codes by the
    /// <paramref name="token"/> placeholder; returns once the dialog has closed.
    /// </summary>
    public async Task SaveAsTemplateAsync(string code, string nameAr, string nameEn, string literal, string token)
    {
        var id = await RevealAsync(code);
        await page.GetByTestId($"org-template-{id}").ClickAsync();
        await Field("template-name-ar").FillAsync(nameAr);
        await Field("template-name-en").FillAsync(nameEn);
        await page.GetByTestId("template-add-replacement").ClickAsync();
        await Field("replace-literal-0").FillAsync(literal);
        await Field("replace-token-0").FillAsync(token);
        await Field("replace-token-0").PressAsync("Tab"); // the field commits on blur
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
        var save = page.GetByTestId("dialog-save");
        await Assertions.Expect(save).ToBeEnabledAsync();
        await save.ClickAsync();
        var error = page.GetByTestId("dialog-error");
        try
        {
            await save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            if (await error.IsVisibleAsync())
            {
                throw new InvalidOperationException($"Dialog save failed: {await error.InnerTextAsync()}");
            }

            throw;
        }

        await page.Mouse.MoveAsync(0, 0);
    }

    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input").First;
}
