using System.Net;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class PasswordResetTests(LmsApiFactory factory)
{
    private const string NewPassword = "N3w-Passw0rd";
    private const string ConfirmationEn = "If an account exists for this email, we have sent a password reset link.";
    private const string LinkInvalidEn = "This link is invalid or has expired. Request a new one.";

    private static HttpClient English(HttpClient browser)
    {
        browser.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        return browser;
    }

    private static Task<HttpResponseMessage> RequestReset(HttpClient browser, string email) =>
        AuthTestHost.PostFormAsync(browser, "/Account/ForgotPassword", new() { ["Input.Email"] = email });

    private static Dictionary<string, string> NewPasswordFields(string password, string? confirm = null) =>
        new() { ["Input.Password"] = password, ["Input.ConfirmPassword"] = confirm ?? password };

    private async Task<List<AuditEntry>> EventsAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.AuditEntries.Where(x => x.UserId == userId && x.Action == AuditAction.SecurityEvent)
            .OrderBy(x => x.TimestampUtc).ToListAsync();
    }

    [Fact]
    public async Task Forgot_password_for_known_email_sends_one_reset_mail()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        using var browser = English(host.NewBrowser());

        var response = await RequestReset(browser, user.Email!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ConfirmationEn, AuthTestHost.TestId(await response.Content.ReadAsStringAsync(), "forgot-confirmation"));
        var mail = Assert.Single(host.Emails.To(user.Email!));
        Assert.Equal("Reset your LMS password", mail.Subject);
        Assert.Contains($"/Account/ResetPassword?userId={user.Id}&", AuthTestHost.FirstLink(mail));
        Assert.Contains("token=", AuthTestHost.FirstLink(mail));
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_shows_same_confirmation_and_sends_nothing()
    {
        var host = new AuthTestHost(factory);
        using var browser = English(host.NewBrowser());

        var response = await RequestReset(browser, "nobody-here@lms.test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ConfirmationEn, AuthTestHost.TestId(await response.Content.ReadAsStringAsync(), "forgot-confirmation"));
        Assert.Empty(host.Emails.Sent);
    }

    [Fact]
    public async Task Reset_with_valid_token_changes_password_and_link_cannot_be_reused()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        using var browser = English(host.NewBrowser());
        await RequestReset(browser, user.Email!);
        var link = AuthTestHost.FirstLink(host.Emails.To(user.Email!).Single());

        var form = await browser.GetAsync(link);
        Assert.Contains("name=\"Input.Password\"", await form.Content.ReadAsStringAsync());

        var done = await AuthTestHost.PostFormAsync(browser, link, NewPasswordFields(NewPassword));
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.Equal("Your password was changed. You can sign in now.",
            AuthTestHost.TestId(await done.Content.ReadAsStringAsync(), "reset-success"));

        // The new password works, the old one does not.
        Assert.Equal(HttpStatusCode.Redirect, (await AuthTestHost.PostLoginAsync(browser, user.Email!, NewPassword)).StatusCode);
        using var other = English(host.NewBrowser());
        Assert.Equal(HttpStatusCode.OK, (await AuthTestHost.PostLoginAsync(other, user.Email!, AuthTestHost.Password)).StatusCode);

        // The same link is now dead, on GET and on POST.
        var reused = await other.GetAsync(link);
        Assert.Equal(LinkInvalidEn, AuthTestHost.TestId(await reused.Content.ReadAsStringAsync(), "reset-error"));
        // (a dead link renders no form, so the antiforgery token comes from another page of the same browser)
        var again = await AuthTestHost.PostFormAsync(
            other, link, NewPasswordFields("An0ther-Passw0rd"), tokenFrom: "/Account/ForgotPassword");
        Assert.Contains("reset-error", await again.Content.ReadAsStringAsync());
        // The password was not changed a second time.
        Assert.Equal(HttpStatusCode.Redirect, (await AuthTestHost.PostLoginAsync(other, user.Email!, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_with_expired_token_shows_LinkInvalid()
    {
        var host = new AuthTestHost(factory, resetLifespan: TimeSpan.FromSeconds(1));
        var user = await host.CreateUserAsync();
        using var browser = English(host.NewBrowser());
        await RequestReset(browser, user.Email!);
        var link = AuthTestHost.FirstLink(host.Emails.To(user.Email!).Single());

        await Task.Delay(TimeSpan.FromSeconds(3));

        var response = await browser.GetAsync(link);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(LinkInvalidEn, AuthTestHost.TestId(html, "reset-error"));
        Assert.DoesNotContain("name=\"Input.Password\"", html);
    }

    [Fact]
    public async Task Reset_clears_lockout()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        using var browser = English(host.NewBrowser());
        for (var i = 0; i < 5; i++)
        {
            await AuthTestHost.PostLoginAsync(browser, user.Email!, "Wrong-password1");
        }

        Assert.Equal(HttpStatusCode.OK, (await AuthTestHost.PostLoginAsync(browser, user.Email!, AuthTestHost.Password)).StatusCode);
        await RequestReset(browser, user.Email!);
        var link = AuthTestHost.FirstLink(host.Emails.To(user.Email!).Single());

        await AuthTestHost.PostFormAsync(browser, link, NewPasswordFields(NewPassword));

        Assert.Equal(HttpStatusCode.Redirect, (await AuthTestHost.PostLoginAsync(browser, user.Email!, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Invitation_link_sets_first_password_and_user_can_log_in()
    {
        var host = new AuthTestHost(factory);
        var email = $"invited-{Guid.NewGuid():N}@lms.test";
        User user;
        using (var scope = host.Services.CreateScope())
        {
            user = new User { UserName = email, Email = email, DisplayNameEn = "Invited", DisplayNameAr = "مدعو" };
            Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(user)).Succeeded);
            await scope.ServiceProvider.GetRequiredService<IAccountEmails>().SendInvitationAsync(user, "en");
        }

        var mail = Assert.Single(host.Emails.To(email));
        Assert.Equal("Your LMS account is ready", mail.Subject);
        var link = AuthTestHost.FirstLink(mail);
        Assert.Contains("invite=1", link);
        using var browser = English(host.NewBrowser());

        var page = await browser.GetAsync(link);
        Assert.Contains("Welcome", await page.Content.ReadAsStringAsync());
        var done = await AuthTestHost.PostFormAsync(browser, link, NewPasswordFields(NewPassword));

        Assert.Contains("reset-success", await done.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await AuthTestHost.PostLoginAsync(browser, email, NewPassword)).StatusCode);
        Assert.Contains(await EventsAsync(user.Id), e => e.EntityType == "InvitationAccepted");

        // Single use.
        using var other = English(host.NewBrowser());
        Assert.Equal(LinkInvalidEn, AuthTestHost.TestId(await (await other.GetAsync(link)).Content.ReadAsStringAsync(), "reset-error"));
    }

    [Fact]
    public async Task Invitation_token_cannot_be_used_on_the_reset_flow()
    {
        var host = new AuthTestHost(factory);
        var email = $"invited-{Guid.NewGuid():N}@lms.test";
        using (var scope = host.Services.CreateScope())
        {
            var user = new User { UserName = email, Email = email, DisplayNameEn = "Invited", DisplayNameAr = "مدعو" };
            await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(user);
            await scope.ServiceProvider.GetRequiredService<IAccountEmails>().SendInvitationAsync(user, "en");
        }

        var link = AuthTestHost.FirstLink(host.Emails.To(email).Single()).Replace("&invite=1", string.Empty);
        using var browser = English(host.NewBrowser());

        var html = await (await browser.GetAsync(link)).Content.ReadAsStringAsync();

        Assert.Equal(LinkInvalidEn, AuthTestHost.TestId(html, "reset-error"));
    }

    [Fact]
    public async Task Token_does_not_work_for_another_user()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        var other = await host.CreateUserAsync();
        using var browser = English(host.NewBrowser());
        await RequestReset(browser, user.Email!);
        var link = AuthTestHost.FirstLink(host.Emails.To(user.Email!).Single()).Replace(user.Id.ToString(), other.Id.ToString());

        var html = await (await browser.GetAsync(link)).Content.ReadAsStringAsync();

        Assert.Equal(LinkInvalidEn, AuthTestHost.TestId(html, "reset-error"));
    }

    [Fact]
    public async Task Mismatched_or_weak_passwords_are_refused_without_consuming_the_link()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        using var browser = English(host.NewBrowser());
        await RequestReset(browser, user.Email!);
        var link = AuthTestHost.FirstLink(host.Emails.To(user.Email!).Single());

        var mismatch = await AuthTestHost.PostFormAsync(browser, link, NewPasswordFields(NewPassword, "Different-Passw0rd"));
        Assert.Equal("Passwords do not match.", AuthTestHost.TestId(await mismatch.Content.ReadAsStringAsync(), "reset-error"));

        var weak = await AuthTestHost.PostFormAsync(browser, link, NewPasswordFields("short"));
        Assert.Contains("reset-error", await weak.Content.ReadAsStringAsync());

        // The link still works afterwards.
        var ok = await AuthTestHost.PostFormAsync(browser, link, NewPasswordFields(NewPassword));
        Assert.Contains("reset-success", await ok.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_writes_PasswordReset_security_event()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        using var browser = English(host.NewBrowser());
        await RequestReset(browser, user.Email!);
        var link = AuthTestHost.FirstLink(host.Emails.To(user.Email!).Single());
        await AuthTestHost.PostFormAsync(browser, link, NewPasswordFields(NewPassword));

        var events = await EventsAsync(user.Id);

        Assert.Equal(["PasswordResetRequested", "PasswordReset"], events.Select(e => e.EntityType));
        Assert.All(events, e => Assert.Equal(AuthTestHost.RemoteIp, e.IpAddress));
        Assert.DoesNotContain(NewPassword, string.Concat(events.Select(e => e.AfterJson)));
    }

    [Fact]
    public async Task Forgot_password_during_a_mail_outage_still_shows_the_same_confirmation()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        host.Emails.Fail = true;
        using var browser = English(host.NewBrowser());

        var response = await RequestReset(browser, user.Email!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ConfirmationEn, AuthTestHost.TestId(await response.Content.ReadAsStringAsync(), "forgot-confirmation"));
    }

    [Fact]
    public async Task Unknown_email_writes_no_PasswordResetRequested_event()
    {
        var host = new AuthTestHost(factory);
        using var browser = English(host.NewBrowser());
        await RequestReset(browser, "ghost-account@lms.test");

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.DoesNotContain(
            await db.AuditEntries.Where(x => x.EntityType == "PasswordResetRequested").ToListAsync(),
            e => (e.AfterJson ?? string.Empty).Contains("ghost-account"));
    }

    [Fact]
    public async Task Sixth_forgot_password_post_in_a_minute_gets_429()
    {
        var host = new AuthTestHost(factory, permitLimit: 5, remoteIp: "198.51.100.77");
        using var browser = English(host.NewBrowser());

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await RequestReset(browser, "nobody-here@lms.test")).StatusCode);
        }

        var sixth = await RequestReset(browser, "nobody-here@lms.test");

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.Contains("Too many attempts", await sixth.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_posts_are_rate_limited_too()
    {
        var host = new AuthTestHost(factory, permitLimit: 5, remoteIp: "198.51.100.78");
        using var browser = English(host.NewBrowser());

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await AuthTestHost.PostLoginAsync(browser, "nobody@lms.test", "Wrong-password1")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await AuthTestHost.PostLoginAsync(browser, "nobody@lms.test", "Wrong-password1")).StatusCode);
    }

    [Fact]
    public async Task Reset_page_renders_rtl_in_arabic()
    {
        var host = new AuthTestHost(factory);
        var user = await host.CreateUserAsync();
        using var browser = host.NewBrowser();
        await RequestReset(browser, user.Email!);
        var link = AuthTestHost.FirstLink(host.Emails.To(user.Email!).Single());

        var reset = WebUtility.HtmlDecode(await (await browser.GetAsync(link + "&culture=ar&ui-culture=ar")).Content.ReadAsStringAsync());
        var forgot = WebUtility.HtmlDecode(await (await browser.GetAsync("/Account/ForgotPassword?culture=ar&ui-culture=ar")).Content.ReadAsStringAsync());

        Assert.Contains("dir=\"rtl\"", reset);
        Assert.Contains("كلمة المرور الجديدة", reset);
        Assert.Contains("dir=\"rtl\"", forgot);
        Assert.Contains("إرسال رابط إعادة التعيين", forgot);
    }

    [Fact]
    public async Task Login_page_links_to_forgot_password()
    {
        var host = new AuthTestHost(factory);
        using var browser = English(host.NewBrowser());

        var html = await (await browser.GetAsync("/Account/Login")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/Account/ForgotPassword\"", html);
        Assert.Contains("Forgot password?", html);
    }
}
