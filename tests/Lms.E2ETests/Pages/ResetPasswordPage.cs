using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The server-rendered set/reset password page reached from an invitation or reset email (#82).</summary>
public sealed class ResetPasswordPage(IPage page)
{
    public ILocator Success => page.GetByTestId("reset-success");
    public ILocator Error => page.GetByTestId("reset-error");

    /// <summary>Opens the emailed link (path + query) on the E2E base URL, sets the password and waits for success.</summary>
    public async Task SetPasswordAsync(string linkPathAndQuery, string password)
    {
        await page.GotoAsync(linkPathAndQuery);
        await page.GetByTestId("reset-password").FillAsync(password);
        await page.GetByTestId("reset-confirm").FillAsync(password);
        await page.GetByTestId("reset-submit").ClickAsync();
        await Success.WaitForAsync();
    }
}
