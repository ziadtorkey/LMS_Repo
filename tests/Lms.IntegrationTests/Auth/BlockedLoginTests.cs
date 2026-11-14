using System.Net;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class BlockedLoginTests(LmsApiFactory factory) : IDisposable
{
    private readonly AuthTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<string> LoginMessageAsync(UserStatus status, string culture)
    {
        var user = await _host.CreateUserAsync(status: status);
        using var browser = _host.NewBrowser();
        var url = $"/Account/Login?culture={culture}&ui-culture={culture}";

        var response = await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password, loginUrl: url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(response.Headers, h => h.Key == "Set-Cookie");
        return AuthTestHost.TestId(await response.Content.ReadAsStringAsync(), "login-error");
    }

    [Fact]
    public async Task Expired_user_sees_AccountExpired_message()
    {
        Assert.Equal("Your account has expired. Please contact your administrator.", await LoginMessageAsync(UserStatus.Expired, "en"));
        Assert.Equal("انتهت صلاحية حسابك. يرجى التواصل مع مسؤول النظام.", await LoginMessageAsync(UserStatus.Expired, "ar"));
    }

    [Fact]
    public async Task Withdrawn_user_sees_AccountWithdrawn_message()
    {
        Assert.Equal("This account is no longer active. Please contact your school or university.",
            await LoginMessageAsync(UserStatus.Withdrawn, "en"));
        Assert.Equal("هذا الحساب لم يعد نشطًا. يرجى التواصل مع المدرسة أو الجامعة.",
            await LoginMessageAsync(UserStatus.Withdrawn, "ar"));
    }

    [Fact]
    public async Task Deactivated_user_message_is_unchanged()
    {
        Assert.Equal("This account is deactivated. Please contact your administrator.",
            await LoginMessageAsync(UserStatus.Deactivated, "en"));
    }

    [Fact]
    public async Task Blocked_account_message_is_only_shown_after_the_correct_password()
    {
        var user = await _host.CreateUserAsync(status: UserStatus.Withdrawn);
        using var browser = _host.NewBrowser();
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        var wrong = await AuthTestHost.PostLoginAsync(browser, user.Email!, "Wrong-password1");

        // Same generic message as for an unknown user: the status of the account is not revealed.
        Assert.Contains("Invalid email or password", AuthTestHost.TestId(await wrong.Content.ReadAsStringAsync(), "login-error"));
    }

    [Fact]
    public async Task Blocked_login_is_audited_with_the_status_as_reason()
    {
        var user = await _host.CreateUserAsync(status: UserStatus.Expired);
        using var browser = _host.NewBrowser();
        await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.AuditEntries.SingleAsync(x => x.UserId == user.Id && x.EntityType == "LoginFailed");

        Assert.Contains("Expired", entry.AfterJson);
    }

    [Fact]
    public async Task Expired_user_cannot_use_an_existing_access_token()
    {
        var user = await _host.CreateUserAsync();
        var (access, _) = await _host.SignInForTokensAsync(user);
        using var api = _host.NewBrowser();
        api.DefaultRequestHeaders.Authorization = new("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/auth/whoami")).StatusCode);

        await _host.SetStatusAsync(user, UserStatus.Expired);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/auth/whoami")).StatusCode);
    }
}
