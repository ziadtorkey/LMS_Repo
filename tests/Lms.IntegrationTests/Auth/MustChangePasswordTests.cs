using System.Net;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Auth;

/// <summary>D24: a temporary password must be replaced at first sign-in, before any token is issued (Review Focus 5).</summary>
[Collection(ApiCollection.Name)]
public sealed class MustChangePasswordTests(LmsApiFactory factory) : IDisposable
{
    private const string NewPassword = "N3wStrongPass";
    private readonly AuthTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<User> MustChangeUserAsync(string? email = null)
    {
        var user = await _host.CreateUserAsync(email);
        using var scope = _host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
        stored.MustChangePassword = true;
        Assert.True((await users.UpdateAsync(stored)).Succeeded);
        return stored;
    }

    private async Task<bool> MustChangeAsync(Guid id)
    {
        using var scope = _host.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(id.ToString()))!.MustChangePassword;
    }

    [Fact]
    public async Task Must_change_user_gets_no_authorization_code_until_changed()
    {
        var user = await MustChangeUserAsync();
        using var browser = _host.NewBrowser();
        var (_, challenge) = AuthTestHost.NewPkcePair();
        var authorizeUrl = AuthTestHost.AuthorizeUrl(challenge);

        var login = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password, authorizeUrl);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("/Account/ChangePassword", login.Headers.Location!.ToString());
        // going straight to the authorize endpoint does not help: no code, back to the change page
        var authorize = await browser.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var location = authorize.Headers.Location!.ToString();
        Assert.Contains("/Account/ChangePassword", location);
        Assert.DoesNotContain("code=", location);
        Assert.DoesNotContain(AuthTestHost.RedirectUri, location);
    }

    [Fact]
    public async Task Change_clears_flag_and_continues_to_app()
    {
        var user = await MustChangeUserAsync();
        using var browser = _host.NewBrowser();
        var (verifier, challenge) = AuthTestHost.NewPkcePair();
        var authorizeUrl = AuthTestHost.AuthorizeUrl(challenge);
        var login = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password, authorizeUrl);
        var changeUrl = login.Headers.Location!.ToString();

        var wrongCurrent = await AuthTestHost.PostFormAsync(browser, changeUrl, Fields("wrong-password", NewPassword, NewPassword));
        var mismatch = await AuthTestHost.PostFormAsync(browser, changeUrl, Fields(AuthTestHost.Password, NewPassword, "Different1"));
        var weak = await AuthTestHost.PostFormAsync(browser, changeUrl, Fields(AuthTestHost.Password, "short", "short"));
        Assert.All(new[] { wrongCurrent, mismatch, weak }, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.True(await MustChangeAsync(user.Id)); // still flagged after every refusal

        var changed = await AuthTestHost.PostFormAsync(browser, changeUrl, Fields(AuthTestHost.Password, NewPassword, NewPassword));

        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.False(await MustChangeAsync(user.Id));
        var continued = await browser.GetAsync(changed.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, continued.StatusCode);
        Assert.StartsWith(AuthTestHost.RedirectUri, continued.Headers.Location!.ToString()); // the authorize request now issues its code
        Assert.Contains("code=", continued.Headers.Location!.Query);
        Assert.NotNull(verifier);
        // the old temporary password no longer works
        using var other = _host.NewBrowser();
        var old = await AuthTestHost.PostLoginAsync(other, user.Email!, AuthTestHost.Password, loginUrl: "/Account/Login?culture=en&ui-culture=en");
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        var again = await AuthTestHost.PostLoginAsync(other, user.Email!, NewPassword);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\evil.example")]
    public async Task Change_never_redirects_to_a_foreign_return_url(string returnUrl)
    {
        var user = await MustChangeUserAsync();
        using var browser = _host.NewBrowser();
        await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password);
        var url = "/Account/ChangePassword?ReturnUrl=" + Uri.EscapeDataString(returnUrl);

        var changed = await AuthTestHost.PostFormAsync(browser, url, Fields(AuthTestHost.Password, NewPassword, NewPassword));

        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal("/", changed.Headers.Location!.ToString()); // only a local path is followed; anything else falls back to the root
    }

    [Fact]
    public async Task Student_without_an_email_signs_in_with_the_user_name()
    {
        var code = ("s" + Guid.NewGuid().ToString("N")[..10]).ToLowerInvariant() + "@school-x"; // D47: code@short code
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.CreateAsync(new User { UserName = code, DisplayNameEn = "Student", MustChangePassword = true }, AuthTestHost.Password)).Succeeded);
        }

        using var browser = _host.NewBrowser();
        var login = await AuthTestHost.PostLoginAsync(browser, code.ToUpperInvariant(), AuthTestHost.Password); // case-insensitive

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("/Account/ChangePassword", login.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Ordinary_user_is_not_sent_to_the_change_page()
    {
        var user = await _host.CreateUserAsync();
        using var browser = _host.NewBrowser();

        var login = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password, "/home");

        Assert.Equal("/home", login.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Change_password_page_renders_rtl_in_arabic_and_needs_a_session()
    {
        var user = await MustChangeUserAsync();
        using var browser = _host.NewBrowser();
        var anonymous = await browser.GetAsync("/Account/ChangePassword");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Contains("/Account/Login", anonymous.Headers.Location!.ToString());

        await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password);
        var html = await (await browser.GetAsync("/Account/ChangePassword?culture=ar&ui-culture=ar")).Content.ReadAsStringAsync();

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Equal("اختر كلمة مرور جديدة", System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "<h1>([^<]*)</h1>").Groups[1].Value));
        Assert.Contains("لقد سجلت الدخول بكلمة مرور مؤقتة", System.Net.WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Refresh_grant_is_refused_while_the_flag_is_set()
    {
        var user = await _host.CreateUserAsync();
        var (_, refresh) = await _host.SignInForTokensAsync(user);
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            stored.MustChangePassword = true;
            await users.UpdateAsync(stored);
        }

        using var browser = _host.NewBrowser();
        var response = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "lms-wasm", ["refresh_token"] = refresh,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_grant", await response.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, string> Fields(string current, string next, string confirm) => new()
    {
        ["Input.Current"] = current, ["Input.NewPassword"] = next, ["Input.Confirm"] = confirm,
    };
}
