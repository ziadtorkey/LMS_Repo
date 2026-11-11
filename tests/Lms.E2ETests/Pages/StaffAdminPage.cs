using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The staff list (/staff) and its create dialog (FE Task 24, #170).</summary>
public sealed class StaffAdminPage(IPage page)
{
    public sealed record NewStaff(
        string NameAr, string NameEn, string Email, string? Mobile, string HomeUnitName, string RoleName, string RoleUnitName, DateOnly? ExpiresOn = null, string? RoleUnitParent = null);

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

    /// <summary>Opens the person's profile from the list (searching for the email first).</summary>
    public async Task OpenProfileAsync(string email)
    {
        await OpenAsync();
        await SearchAsync(email);
        await Row(email).Locator("[data-testid^='staff-link-']").ClickAsync();
        await page.GetByTestId("profile-name").WaitForAsync();
    }

    public ILocator ProfileExpires => page.GetByTestId("profile-expires");
    public ILocator ProfileRoles => page.GetByTestId("profile-roles");
    public ILocator ProfileHistory => page.GetByTestId("profile-history");

    /// <summary>Deactivates or reactivates from the profile with a reason; waits for the dialog to close.</summary>
    public async Task ChangeStatusAsync(string reason)
    {
        await page.GetByTestId("action-status").ClickAsync();
        // A multi-line MudTextField renders two textareas (one is a hidden auto-size shadow): take the visible one.
        await page.Locator("textarea[data-testid='status-reason'], [data-testid='status-reason'] textarea, [data-testid='status-reason'] input")
            .Locator("visible=true").First.FillAsync(reason);
        // The dialog closes before the request finishes; wait for the server's answer or a following navigation
        // cancels the request mid-flight and the status is never stored.
        var response = await page.RunAndWaitForResponseAsync(
            async () => await page.GetByTestId("status-confirm").ClickAsync(),
            r => r.Url.Contains("/status", StringComparison.Ordinal) && r.Request.Method == "PUT");
        if (!response.Ok)
        {
            throw new InvalidOperationException($"Changing the status answered {response.Status}.");
        }

        await page.GetByTestId("status-confirm").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    /// <summary>Shares the person to another unit with a role from the profile; waits for the dialog to close.</summary>
    public async Task ShareAsync(string roleName, string unitName)
    {
        await page.GetByTestId("action-share").ClickAsync();
        await PickAsync("staff-role-0", roleName);
        await PickAsync("staff-role-unit-0", unitName);
        await page.GetByTestId("share-save").ClickAsync();
        await page.GetByTestId("share-save").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

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

        if (staff.ExpiresOn is { } expires)
        {
            await PickDateAsync("staff-expires", expires);
        }

        await PickAsync("staff-home-unit", staff.HomeUnitName);
        await PickAsync("staff-role-0", staff.RoleName);
        await PickAsync("staff-role-unit-0", staff.RoleUnitName, staff.RoleUnitParent);
        await page.GetByTestId("dialog-save").ClickAsync();
    }

    /// <summary>Creates the person and waits for the dialog to close.</summary>
    public async Task CreateAsync(NewStaff staff)
    {
        await FillAndSaveAsync(staff);
        await page.GetByTestId("dialog-save").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.Mouse.MoveAsync(0, 0);
    }

    /// <summary>
    /// Picks a date in a MudDatePicker (its input is read-only): opens the calendar on the browser's current month,
    /// pages forward to the target month and clicks the day (a click closes the popup).
    /// </summary>
    private async Task PickDateAsync(string testId, DateOnly date)
    {
        await page.Locator($"input[data-testid='{testId}']").ClickAsync();
        var popup = page.Locator(".mud-popover-open .mud-picker-calendar-content");
        await popup.WaitForAsync();
        var today = DateTime.Today;
        var months = (date.Year - today.Year) * 12 + date.Month - today.Month;
        for (var i = 0; i < months; i++)
        {
            await popup.Locator(".mud-picker-nav-button-next").ClickAsync();
        }

        await popup.Locator(".mud-picker-calendar-day.mud-day:not(.mud-adjacent-month)")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^\\s*" + date.Day + "\\s*$") })
            .First.ClickAsync();
        await popup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    /// <summary>
    /// Opens a MudSelect and picks the option whose text is exactly <paramref name="text"/> (indent ignored). Several
    /// units can share a name ("Section 1" under each stage), and the list order follows the culture's sort, so with
    /// <paramref name="afterText"/> the pick is the first match that comes after the option with that text.
    /// </summary>
    private async Task PickAsync(string testId, string text, string? afterText = null)
    {
        await page.GetByTestId(testId).ClickAsync();
        var items = page.Locator(".mud-popover-open .mud-list-item");
        await items.First.WaitForAsync();
        var texts = (await items.AllInnerTextsAsync()).Select(t => t.Trim()).ToList();
        var start = afterText is null ? 0 : texts.IndexOf(afterText);
        if (start < 0)
        {
            throw new InvalidOperationException($"Option '{afterText}' not found in {testId}: {string.Join(" | ", texts)}");
        }

        var index = texts.FindIndex(start, t => t == text);
        if (index < 0)
        {
            throw new InvalidOperationException($"Option '{text}' not found after '{afterText}' in {testId}: {string.Join(" | ", texts)}");
        }

        await items.Nth(index).ClickAsync();
    }

    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input").First;
}
