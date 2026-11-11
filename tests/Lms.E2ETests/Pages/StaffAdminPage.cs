using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The staff list (/staff) and its create dialog (FE Task 24, #170).</summary>
public sealed class StaffAdminPage(IPage page)
{
    public sealed record NewStaff(
        string NameAr, string NameEn, string Email, string? Mobile, string HomeUnitName, string RoleName, string RoleUnitName);

    public ILocator Title => page.GetByTestId("staff-title");
    public ILocator DialogError => page.GetByTestId("dialog-error");
    public ILocator Snackbar => page.Locator(".mud-snackbar");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/staff");
        await Title.WaitForAsync();
    }

    /// <summary>The list row of the person whose email is shown (emails are unique in a client).</summary>
    public ILocator Row(string email) => page.Locator("tr").Filter(new LocatorFilterOptions { HasText = email });

    public async Task SearchAsync(string text)
    {
        var search = Field("staff-search");
        await search.FillAsync(text);
        await search.PressAsync("Enter");
    }

    /// <summary>Fills the create dialog and saves. When the save is refused the dialog stays open (see <see cref="DialogError"/>).</summary>
    public async Task FillAndSaveAsync(NewStaff staff)
    {
        await page.GetByTestId("staff-new").ClickAsync();
        await Field("staff-name-ar").FillAsync(staff.NameAr);
        await Field("staff-name-en").FillAsync(staff.NameEn);
        await Field("staff-email").FillAsync(staff.Email);
        if (staff.Mobile is not null)
        {
            await Field("staff-mobile").FillAsync(staff.Mobile);
        }

        await PickAsync("staff-home-unit", staff.HomeUnitName);
        await PickAsync("staff-role-0", staff.RoleName);
        await PickAsync("staff-role-unit-0", staff.RoleUnitName);
        await page.GetByTestId("dialog-save").ClickAsync();
    }

    /// <summary>Creates the person and waits for the dialog to close.</summary>
    public async Task CreateAsync(NewStaff staff)
    {
        await FillAndSaveAsync(staff);
        await page.GetByTestId("dialog-save").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.Mouse.MoveAsync(0, 0);
    }

    /// <summary>Opens a MudSelect and picks the option whose text is exactly <paramref name="text"/> (indent ignored).</summary>
    private async Task PickAsync(string testId, string text)
    {
        await page.GetByTestId(testId).ClickAsync();
        await page.Locator(".mud-popover-open .mud-list-item")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^\\s*" + Regex.Escape(text) + "\\s*$") })
            .First.ClickAsync();
    }

    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input").First;
}
