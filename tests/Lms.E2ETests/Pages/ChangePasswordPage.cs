using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The server-rendered forced change-password page (/Account/ChangePassword, D24) a student with a default password lands on.</summary>
public sealed class ChangePasswordPage(IPage page)
{
    public static readonly Regex Url = new("/Account/ChangePassword", RegexOptions.IgnoreCase);

    public ILocator Error => page.GetByTestId("change-error");

    public Task WaitUntilShownAsync() => page.WaitForURLAsync(Url);

    /// <summary>Submits the form and returns once the server's answer (a re-rendered page or the redirect) has loaded.</summary>
    public async Task SubmitAsync(string current, string next, string confirm)
    {
        await page.GetByTestId("change-current").FillAsync(current);
        await page.GetByTestId("change-new").FillAsync(next);
        await page.GetByTestId("change-confirm").FillAsync(confirm);
        await page.EvaluateAsync("() => { window.__lmsBeforeSubmit = true; }");
        await page.GetByTestId("change-submit").ClickAsync();
        // A failed change re-renders the same URL: wait for the new document (see LoginPage.SubmitAsync).
        await page.WaitForFunctionAsync("() => window.__lmsBeforeSubmit !== true && document.readyState === 'complete'");
    }

    /// <summary>The student's first sign-in: user name + default password, then the new password; returns once the app is shown.</summary>
    public static async Task FirstSignInAsync(IPage page, string userName, string defaultPassword, string newPassword)
    {
        await new LoginPage(page).SignInAsync(userName, defaultPassword);
        var change = new ChangePasswordPage(page);
        await change.WaitUntilShownAsync();
        await change.SubmitAsync(defaultPassword, newPassword, newPassword);
        await new ShellPage(page).WaitReadyAsync();
    }
}
