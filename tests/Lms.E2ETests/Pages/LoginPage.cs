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

    /// <summary>
    /// Submits credentials for a failure case and returns only once the server's re-rendered page has replaced the
    /// current one. The failed login re-renders at the same URL, so neither the URL, a load state nor the response
    /// event tells the old document from the new one; instead the current document is marked before the click and
    /// we wait for a loaded document without the mark. Otherwise the next attempt could be typed into the page that
    /// is about to be replaced, and a lockout attempt would be lost.
    /// </summary>
    public async Task SubmitAsync(string email, string password)
    {
        await Email.FillAsync(email);
        await Password.FillAsync(password);
        await page.EvaluateAsync("() => { window.__lmsBeforeSubmit = true; }");
        await Submit.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.__lmsBeforeSubmit !== true && document.readyState === 'complete'");
        await Email.WaitForAsync();
    }
}
