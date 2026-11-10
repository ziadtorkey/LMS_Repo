using System.Net;
using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(LmsApiFactory factory)
{
    private readonly AuthTestHost _host = new(factory);

    private static string Relative(Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    private async Task<List<AuditEntry>> AuditFor(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.AuditEntries.Where(x => x.UserId == userId).OrderBy(x => x.TimestampUtc).ToListAsync();
    }

    [Fact]
    public async Task Password_grant_is_rejected()
    {
        var user = await _host.CreateUserAsync();
        using var client = _host.NewBrowser();

        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "lms-wasm",
            ["username"] = user.Email!,
            ["password"] = AuthTestHost.Password,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unsupported_grant_type",
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Login_with_valid_credentials_redirects_back_to_authorize_and_yields_a_token()
    {
        var user = await _host.CreateUserAsync(platformAdmin: true);
        using var browser = _host.NewBrowser();
        var (verifier, challenge) = AuthTestHost.NewPkcePair();

        // 1. Not logged in: authorize sends the browser to the login page, remembering where to come back.
        var authorize = await browser.GetAsync(AuthTestHost.AuthorizeUrl(challenge));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var loginUrl = Relative(authorize.Headers.Location!);
        Assert.StartsWith("/Account/Login", loginUrl);

        // 2. Valid credentials redirect to the original authorize request.
        var returnUrl = Uri.UnescapeDataString(loginUrl[(loginUrl.IndexOf("ReturnUrl=", StringComparison.OrdinalIgnoreCase) + 10)..]);
        var login = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password, returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("/connect/authorize", Relative(login.Headers.Location!));

        // 3. Authorize now issues a code for the registered redirect uri.
        var back = await browser.GetAsync(login.Headers.Location);
        Assert.True(back.StatusCode == HttpStatusCode.Redirect, await back.Content.ReadAsStringAsync());
        var callback = back.Headers.Location!;
        Assert.StartsWith(AuthTestHost.RedirectUri, callback.OriginalString);
        var code = System.Web.HttpUtility.ParseQueryString(callback.Query)["code"];
        Assert.False(string.IsNullOrEmpty(code));

        // 4. The code is exchanged for tokens; the access token carries sub, name and platform_admin.
        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "lms-wasm",
            ["code"] = code!,
            ["redirect_uri"] = AuthTestHost.RedirectUri,
            ["code_verifier"] = verifier,
        }));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        var accessToken = JsonDocument.Parse(await token.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString();

        using var api = _host.NewBrowser();
        api.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        var info = JsonDocument.Parse(await (await api.GetAsync("/connect/userinfo")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(user.Id.ToString(), info.GetProperty("sub").GetString());
        Assert.Equal("Test User", info.GetProperty("name").GetString());
        Assert.Equal("true", info.GetProperty("platform_admin").GetString());
    }

    [Fact]
    public async Task Userinfo_without_a_token_is_rejected()
    {
        using var client = _host.NewBrowser();

        var response = await client.GetAsync("/connect/userinfo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Five_failed_logins_lock_account_for_15_minutes()
    {
        var user = await _host.CreateUserAsync();
        using var browser = _host.NewBrowser();

        for (var i = 0; i < 5; i++)
        {
            var failed = await AuthTestHost.PostLoginAsync(browser, user.Email!, "Wrong-password1");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        // Even the right password is now refused, with the same generic message.
        var locked = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.NotEmpty(AuthTestHost.ExtractError(await locked.Content.ReadAsStringAsync()));

        using var scope = _host.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(user.Id.ToString());
        var remaining = stored!.LockoutEnd!.Value - DateTimeOffset.UtcNow;
        Assert.InRange(remaining, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in_and_sees_blocked_message()
    {
        var user = await _host.CreateUserAsync(status: UserStatus.Deactivated);
        using var browser = _host.NewBrowser();
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        var response = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deactivated", AuthTestHost.ExtractError(await response.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(response.Headers, h => h.Key == "Set-Cookie");
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_get_same_message()
    {
        var user = await _host.CreateUserAsync();
        using var browser = _host.NewBrowser();

        var unknown = await AuthTestHost.PostLoginAsync(browser, "nobody@lms.test", AuthTestHost.Password);
        var wrong = await AuthTestHost.PostLoginAsync(browser, user.Email!, "Wrong-password1");

        var unknownMessage = AuthTestHost.ExtractError(await unknown.Content.ReadAsStringAsync());
        Assert.NotEmpty(unknownMessage);
        Assert.Equal(unknownMessage, AuthTestHost.ExtractError(await wrong.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task External_returnUrl_is_rejected()
    {
        var user = await _host.CreateUserAsync();
        using var browser = _host.NewBrowser();

        var login = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password, "https://evil.example/steal");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_writes_security_audit_events_with_the_client_ip()
    {
        var user = await _host.CreateUserAsync();
        using var browser = _host.NewBrowser();

        await AuthTestHost.PostLoginAsync(browser, user.Email!, "Wrong-password1");
        await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password);

        var events = (await AuditFor(user.Id)).Where(x => x.Action == AuditAction.SecurityEvent).ToList();

        Assert.Equal(["LoginFailed", "LoginSucceeded"], events.Select(x => x.EntityType));
        Assert.All(events, x => Assert.Equal(AuthTestHost.RemoteIp, x.IpAddress));
        Assert.Contains("BadPassword", events[0].AfterJson);
    }

    [Fact]
    public async Task Password_change_writes_no_hash_to_audit_json()
    {
        var user = await _host.CreateUserAsync();
        string hashBefore, hashAfter;
        using (var scope = _host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            hashBefore = stored.PasswordHash!;
            var result = await users.ChangePasswordAsync(stored, AuthTestHost.Password, "N3w-password-ok");
            Assert.True(result.Succeeded);
            hashAfter = stored.PasswordHash!;
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.Where(x => x.EntityId == user.Id).ToListAsync();

        Assert.NotEmpty(entries); // the user itself is audited
        var json = string.Concat(entries.Select(x => x.BeforeJson + x.AfterJson));
        Assert.DoesNotContain(hashBefore, json);
        Assert.DoesNotContain(hashAfter, json);
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("SecurityStamp", json);
        Assert.DoesNotContain("ConcurrencyStamp", json);
        Assert.Contains("DisplayNameAr", json);
    }

    [Fact]
    public async Task Login_inputs_carry_test_ids_and_page_is_rtl_in_arabic_and_ltr_in_english()
    {
        using var browser = _host.NewBrowser();

        var arabic = await (await browser.GetAsync("/Account/Login?culture=ar&ui-culture=ar")).Content.ReadAsStringAsync();
        var english = await (await browser.GetAsync("/Account/Login?culture=en&ui-culture=en")).Content.ReadAsStringAsync();

        arabic = WebUtility.HtmlDecode(arabic);
        Assert.Contains("dir=\"rtl\"", arabic);
        Assert.Contains("تسجيل الدخول", arabic);
        Assert.Contains("dir=\"ltr\"", english);
        Assert.Contains("Sign in", english);
        foreach (var id in new[] { "login-email", "login-password", "login-submit" })
        {
            Assert.Contains($"data-testid=\"{id}\"", arabic);
        }
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        using var client = _host.NewBrowser();

        var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
