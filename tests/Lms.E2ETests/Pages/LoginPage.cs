using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The server-rendered sign-in page (/Account/Login) reached through the OIDC authorize redirect.</summary>
public sealed class LoginPage(IPage page)
{
    public ILocator Email => page.GetByTestId("login-email");
    public ILocator Password => page.GetByTestId("login-password");
    public ILocator Submit => page.GetByTestId("login-submit");
    public ILocator Error => page.GetByTestId("login-error");

    /// <summary>Opens the app, follows the redirect to the login page and signs in; returns once back in the app.</summary>
    public async Task SignInAsync(string email, string password)
    {
        await page.GotoAsync("/");
        await Email.WaitForAsync();
        await Email.FillAsync(email);
        await Password.FillAsync(password);
        await Submit.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase)
                                          && !url.Contains("/authentication/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Submits credentials and stays on the login page (for failure cases).</summary>
    public async Task SubmitAsync(string email, string password)
    {
        await Email.FillAsync(email);
        await Password.FillAsync(password);
        await Submit.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }
}
