using System.Net;
using Lms.IntegrationTests.Auth;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class ExternalLoginPageTests(LmsApiFactory factory)
{
    private readonly AuthTestHost _host = new(factory);

    [Fact]
    public async Task Login_page_renders_rtl_in_arabic_with_sso_link()
    {
        using var browser = _host.NewBrowser();

        var html = await (await browser.GetAsync("/Account/Login?culture=ar&ui-culture=ar")).Content.ReadAsStringAsync();

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Equal("تسجيل الدخول بحساب مؤسستك", AuthTestHost.TestId(html, "external-login-link"));
        Assert.Contains("/Account/ExternalLogin", html);
        var english = await (await browser.GetAsync("/Account/Login?culture=en&ui-culture=en")).Content.ReadAsStringAsync();
        Assert.Equal("Sign in with your organization account", AuthTestHost.TestId(english, "external-login-link"));
    }

    [Fact]
    public async Task Unknown_domain_and_known_domain_without_sso_show_same_text()
    {
        var user = await _host.CreateUserAsync(); // a real user whose domain (lms.test) has no provider
        using var browser = _host.NewBrowser();

        async Task<string> MessageAsync(string email)
        {
            var response = await AuthTestHost.PostFormAsync(browser, "/Account/ExternalLogin?culture=en&ui-culture=en", new Dictionary<string, string> { ["Email"] = email });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return AuthTestHost.TestId(await response.Content.ReadAsStringAsync(), "external-error");
        }

        var unknown = await MessageAsync("someone@no-such-domain-" + Guid.NewGuid().ToString("N")[..6] + ".example");
        var known = await MessageAsync(user.Email!);

        Assert.Equal("Your organization does not use single sign-on. Sign in with your password.", unknown);
        Assert.Equal(unknown, known);
    }

    [Theory]
    [InlineData("external_account_not_found", "We could not find an account for this identity. Contact your administrator.")]
    [InlineData("external_account_ambiguous", "More than one account matches this identity. Contact your administrator.")]
    [InlineData("external_failed", "Sign-in with your organization failed. Try again.")]
    [InlineData("account_deactivated", "This account is deactivated. Please contact your administrator.")]
    [InlineData("account_expired", "Your account has expired. Please contact your administrator.")]
    [InlineData("account_withdrawn", "This account is no longer active. Please contact your school or university.")]
    public async Task External_failures_show_in_the_login_error_element(string code, string expected)
    {
        using var browser = _host.NewBrowser();

        var html = await (await browser.GetAsync($"/Account/Login?culture=en&ui-culture=en&externalError={code}")).Content.ReadAsStringAsync();

        Assert.Equal(expected, AuthTestHost.TestId(html, "login-error"));
    }

    [Fact]
    public async Task Unknown_error_code_shows_nothing()
    {
        using var browser = _host.NewBrowser();

        var html = await (await browser.GetAsync("/Account/Login?externalError=<script>alert(1)</script>")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("login-error", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task Callback_without_an_external_session_goes_back_to_login_with_a_failure()
    {
        using var browser = _host.NewBrowser();

        var response = await browser.GetAsync("/Account/ExternalCallback?returnUrl=%2Fhome");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("/Account/Login", location);
        Assert.Contains("externalError=external_failed", location);
    }

    [Fact]
    public async Task Direct_link_to_a_missing_provider_shows_the_no_provider_message()
    {
        using var browser = _host.NewBrowser();

        var html = await (await browser.GetAsync("/Account/ExternalLogin/no-such-provider?culture=en&ui-culture=en")).Content.ReadAsStringAsync();

        Assert.Equal("Your organization does not use single sign-on. Sign in with your password.", AuthTestHost.TestId(html, "external-error"));
    }
}
