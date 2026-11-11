using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>M1 exit demo 3 (plan Task 38): audit viewer, notifications and blocked sign-in messages.</summary>
public sealed class AuditAndNotificationsTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>
    /// FR-SEC-07: with the right password, a blocked account sees why it cannot sign in (exact copy from the account
    /// resources), stays on the login page and gets no session. Seed users: deactivated-e2e, expired-e2e, withdrawn-e2e.
    /// </summary>
    [Theory]
    [InlineData("ar", "deactivated-e2e", "AccountDeactivated")]
    [InlineData("en", "deactivated-e2e", "AccountDeactivated")]
    [InlineData("ar", "expired-e2e", "AccountExpired")]
    [InlineData("en", "expired-e2e", "AccountExpired")]
    [InlineData("ar", "withdrawn-e2e", "AccountWithdrawn")]
    [InlineData("en", "withdrawn-e2e", "AccountWithdrawn")]
    public async Task Deactivated_user_sees_blocked_message_on_login(string culture, string user, string messageKey)
    {
        await StartAsync(test: $"{nameof(Deactivated_user_sees_blocked_message_on_login)}-{culture}-{user}");
        var login = new LoginPage(Page);
        await Page.GotoAsync($"/Account/Login?culture={culture}&ui-culture={culture}");
        await login.Email.WaitForAsync();
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await Page.Locator("html").GetAttributeAsync("dir"));

        await login.SubmitAsync(E2ESettings.SeedUser(user), Settings.Password);

        await Expect(login.Error).ToHaveTextAsync(Resx.Account(culture, messageKey));
        Assert.Contains("/Account/Login", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await Page.Context.CookiesAsync(), c => c.Name.StartsWith(".AspNetCore.Identity", StringComparison.Ordinal));
    }
}
