using Lms.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The platform admin's client list (/clients) and its create dialog (FE Task 24, #81).</summary>
public sealed class ClientsAdminPage(IPage page, string culture)
{
    public sealed record NewClient(
        string NameAr, string NameEn, string Type, string RootLevelAr, string RootLevelEn, string RootCode,
        string AdminNameAr, string AdminNameEn, string AdminEmail);

    public ILocator Title => page.GetByTestId("clients-title");
    public ILocator DialogError => page.GetByTestId("dialog-error");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/clients");
        await Title.WaitForAsync();
    }

    /// <summary>Fills the create dialog and saves; returns once the dialog has closed.</summary>
    public async Task CreateAsync(NewClient client)
    {
        await page.GetByTestId("clients-new").ClickAsync();
        await Field("client-name-ar").FillAsync(client.NameAr);
        await Field("client-name-en").FillAsync(client.NameEn);

        // MudSelect: open it, then pick the localized option text.
        await page.GetByTestId("client-type").ClickAsync();
        await page.Locator(".mud-popover-open .mud-list-item")
            .Filter(new LocatorFilterOptions { HasText = Resx.In(culture, $"ClientType_{client.Type}") })
            .First.ClickAsync();

        await Field("root-level-ar").FillAsync(client.RootLevelAr);
        await Field("root-level-en").FillAsync(client.RootLevelEn);
        await Field("root-code").FillAsync(client.RootCode);
        await Field("admin-name-ar").FillAsync(client.AdminNameAr);
        await Field("admin-name-en").FillAsync(client.AdminNameEn);
        await Field("admin-email").FillAsync(client.AdminEmail);

        await page.GetByTestId("dialog-save").ClickAsync();
        await page.GetByTestId("dialog-save").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    /// <summary>Searches the list and returns the visible row containing <paramref name="text"/>.</summary>
    public async Task<ILocator> FindRowAsync(string text)
    {
        await Field("clients-search").FillAsync(text);
        await page.GetByTestId("clients-search-button").ClickAsync();
        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = text });
        await row.First.WaitForAsync();
        return row.First;
    }

    /// <summary>MudBlazor puts data-testid on the input element itself or on its wrapper; target the input either way.</summary>
    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input").First;
}
