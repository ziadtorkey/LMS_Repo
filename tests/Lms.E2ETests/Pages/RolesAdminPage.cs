using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>The roles and permissions admin page (/roles, FE Task 29).</summary>
public sealed class RolesAdminPage(IPage page)
{
    public ILocator Table => page.Locator("table");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/roles");
        // A full page load takes about 11 s before the shell renders (#275); the 5 s expect default is too short.
        await Table.WaitForAsync();
    }

    /// <summary>The table row whose text contains <paramref name="name"/> (current UI language).</summary>
    public ILocator Row(string name) => page.Locator("tbody tr", new PageLocatorOptions { HasTextString = name });

    /// <summary>Creates a custom role (Staff kind) with the given permissions and waits for it in the list.</summary>
    public async Task CreateAsync(string nameAr, string nameEn, string displayName, params string[] permissions)
    {
        await page.GetByTestId("role-new").ClickAsync();
        await page.GetByTestId("role-editor").WaitForAsync();
        await SetNamesAsync(nameAr, nameEn);
        foreach (var permission in permissions)
        {
            await page.Locator($"[data-testid='perm-{permission}']").CheckAsync();
        }

        await page.GetByTestId("role-save").ClickAsync();
        await Expect(Row(displayName)).ToBeVisibleAsync();
    }

    /// <summary>Opens the editor of the named role and replaces its names; waits for the renamed row.</summary>
    public async Task RenameAsync(string currentName, string nameAr, string nameEn, string newDisplayName)
    {
        await OpenEditorAsync(currentName);
        await SetNamesAsync(nameAr, nameEn);
        await page.GetByTestId("role-save").ClickAsync();
        await Expect(Row(newDisplayName)).ToBeVisibleAsync();
    }

    public async Task OpenEditorAsync(string name)
    {
        await Row(name).Locator("[data-testid^='role-edit-']").ClickAsync();
        await page.GetByTestId("role-editor").WaitForAsync();
    }

    public ILocator Permission(string permission) => page.Locator($"[data-testid='perm-{permission}']");

    public async Task CancelEditAsync()
    {
        await page.GetByTestId("role-cancel").ClickAsync();
        await Expect(Table).ToBeVisibleAsync();
    }

    /// <summary>Deletes a custom role through the confirmation dialog.</summary>
    public async Task DeleteAsync(string name)
    {
        await Row(name).Locator("[data-testid^='role-delete-']").ClickAsync();
        await page.GetByTestId("confirm-yes").ClickAsync();
        await Expect(Row(name)).ToHaveCountAsync(0);
    }

    private async Task SetNamesAsync(string nameAr, string nameEn)
    {
        await page.GetByTestId("name-ar").FillAsync(nameAr);
        await page.GetByTestId("name-en").FillAsync(nameEn);
    }
}
