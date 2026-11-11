using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M2;

/// <summary>
/// M2 exit demo 1 (plan Task 35, #176): staff lifecycle in Arabic (RTL) and English. Runs as the seeded school group's
/// Org Admin (KG and Primary stages with sections), with unique people per test (<see cref="M2Names"/>).
/// </summary>
public sealed class StaffTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task School_admin_creates_teacher_who_activates_from_email_and_sees_the_section_context(string culture)
    {
        var names = M2Names.New();
        var email = names.Email("teacher");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();

        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.CreateAsync(new StaffAdminPage.NewStaff(
            names.NameAr("معلم"), names.NameEn("Teacher"), email, names.Mobile,
            HomeUnitName: Pick(culture, "رياض الأطفال", "KG"),
            RoleName: Pick(culture, "المعلم", "Teacher"),
            RoleUnitName: Pick(culture, "الفصل 1", "Section 1"),
            RoleUnitParent: Pick(culture, "رياض الأطفال", "KG")));
        await Expect(staff.Snackbar).ToContainTextAsync(Resx.In(culture, "Staff_Created"));
        await staff.SearchAsync(email);
        await Expect(staff.Row(email)).ToBeVisibleAsync();

        // The activation email lets the new teacher set a password and sign in; they see only their section.
        await SignOutAsync();
        using var mail = new MailpitClient();
        var link = await mail.WaitForAccountLinkAsync(email);
        await new ResetPasswordPage(Page).SetPasswordAsync(link, names.NewPassword);
        await new LoginPage(Page).SignInAsync(email, names.NewPassword);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Expect(shell.ContextSwitcher).ToContainTextAsync(Pick(culture, "الفصل 1", "Section 1"));
        Assert.DoesNotContain("clients", await shell.VisibleNavKeysAsync());
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Duplicate_email_in_a_sibling_stage_is_refused_with_the_field_message(string culture)
    {
        var names = M2Names.New();
        var email = names.Email("dup");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.CreateAsync(new StaffAdminPage.NewStaff(
            names.NameAr("معلم"), names.NameEn("Teacher"), email, null,
            Pick(culture, "رياض الأطفال", "KG"), Pick(culture, "المعلم", "Teacher"), Pick(culture, "الفصل 1", "Section 1"),
            RoleUnitParent: Pick(culture, "رياض الأطفال", "KG")));

        // The same email under the other stage is refused (checks are client-wide, D26) and names only the field.
        await staff.FillAndSaveAsync(new StaffAdminPage.NewStaff(
            names.NameAr("آخر"), names.NameEn("Other"), email, null,
            Pick(culture, "المرحلة الابتدائية", "Primary"), Pick(culture, "المعلم", "Teacher"), Pick(culture, "المرحلة الابتدائية", "Primary")));

        await Expect(staff.DialogError).ToContainTextAsync(Resx.In(culture, "Error_duplicate_person_email"));
        await AssertDirectionAsync(culture);
    }

    /// <remarks>
    /// The job is system-wide: <c>staff-expiry</c> runs for every client and every staff member of the shared test
    /// database, not only the person created here. Running it for the far days (+11) may also expire leftover
    /// people of earlier runs, which is harmless. This test therefore asserts only on its own person.
    /// </remarks>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Staff_expiring_soon_notifies_admins_then_expired_staff_is_blocked_after_the_job_run(string culture)
    {
        var names = M2Names.New();
        var email = names.Email("expiring");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.CreateAsync(NewTeacher(culture, names, email, expiresOn: today.AddDays(10)));
        await staff.OpenProfileAsync(email);
        await Expect(staff.ProfileExpires).ToContainTextAsync(today.AddDays(10).ToString("yyyy-MM-dd"));

        // The daily job sends one pre-expiry notice (14 days by default) to the admins above the home unit.
        await JobsClient.RunAsync(Fixture, "staff-expiry", today);
        await Page.ReloadAsync();
        await new ShellPage(Page).WaitReadyAsync();
        await Page.GetByTestId("notif-toggle").ClickAsync();
        await Expect(Page.Locator("[data-testid^='notif-item-']").First).ToContainTextAsync(Pick(culture, names.NameAr("معلم"), names.NameEn("Teacher")));
        await Page.Keyboard.PressAsync("Escape");

        // The teacher activates, then the job runs for the day after the last usable day: expired, and sign-in says so.
        await SignOutAsync();
        await ActivateAsync(email, names.NewPassword);
        await SignOutAsync();
        await JobsClient.RunAsync(Fixture, "staff-expiry", today.AddDays(11));
        var login = new LoginPage(Page);
        await Page.GotoAsync("/");
        await login.SubmitAsync(email, names.NewPassword);
        await Expect(login.Error).ToContainTextAsync(Resx.Account(culture, "AccountExpired"));
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await Page.Locator("html").GetAttributeAsync("dir"));
    }

    /// <summary>Un-skip when the staff status API (#198) is on main.</summary>
    private const string NeedsStatusApi = "Needs the staff edit/status API (#198) on main";

    [Theory(Skip = NeedsStatusApi)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Admin_deactivates_teacher_with_reason_teacher_sees_blocked_message_then_reactivated_can_log_in(string culture)
    {
        var names = M2Names.New();
        var email = names.Email("deact");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.CreateAsync(NewTeacher(culture, names, email));
        await SignOutAsync();
        await ActivateAsync(email, names.NewPassword);
        await SignOutAsync();

        await SignInAsSeededAdminAsync();
        await staff.OpenProfileAsync(email);
        await staff.ChangeStatusAsync("left the school");
        await Expect(staff.ProfileHistory).ToContainTextAsync("left the school");

        await SignOutAsync();
        var login = new LoginPage(Page);
        await Page.GotoAsync("/");
        await login.SubmitAsync(email, names.NewPassword);
        await Expect(login.Error).ToContainTextAsync(Resx.Account(culture, "AccountDeactivated"));

        await SignOutAsync();
        await SignInAsSeededAdminAsync();
        await staff.OpenProfileAsync(email);
        await staff.ChangeStatusAsync("returned");
        await SignOutAsync();
        await new LoginPage(Page).SignInAsync(email, names.NewPassword);
        await new ShellPage(Page).WaitReadyAsync();
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_shares_teacher_to_a_sibling_stage_and_the_list_shows_them_as_shared(string culture)
    {
        var names = M2Names.New();
        var email = names.Email("share");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.CreateAsync(NewTeacher(culture, names, email));
        await staff.OpenProfileAsync(email);

        await staff.ShareAsync(Pick(culture, "المعلم", "Teacher"), Pick(culture, "المرحلة الابتدائية", "Primary"));

        await Expect(staff.ProfileRoles).ToContainTextAsync(Pick(culture, "المرحلة الابتدائية", "Primary"));
        await Expect(staff.ProfileRoles.Locator("[data-testid^='remove-share-']")).ToHaveCountAsync(1);
        await AssertDirectionAsync(culture);
    }

    private static StaffAdminPage.NewStaff NewTeacher(string culture, M2Names names, string email, DateOnly? expiresOn = null) =>
        new(names.NameAr("معلم"), names.NameEn("Teacher"), email, null,
            Pick(culture, "رياض الأطفال", "KG"), Pick(culture, "المعلم", "Teacher"), Pick(culture, "الفصل 1", "Section 1"), expiresOn, RoleUnitParent: Pick(culture, "رياض الأطفال", "KG"));

    /// <summary>The new person follows the emailed link, sets a password and signs in once (the account is then active).</summary>
    private async Task ActivateAsync(string email, string password)
    {
        using var mail = new MailpitClient();
        var link = await mail.WaitForAccountLinkAsync(email);
        await new ResetPasswordPage(Page).SetPasswordAsync(link, password);
    }

    private static string Pick(string culture, string ar, string en) => culture == "ar" ? ar : en;

    private async Task SignInAsSeededAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        // The context is picked after the shell renders; opening a page before that answers 403 and shows "no access".
        await Expect(shell.ContextSwitcher).ToBeVisibleAsync();
        // ... and stored: a page opened by URL reads the context from local storage (cold-start races, see #275).
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
    }

    /// <summary>Signs out of the app and the identity server so the next sign-in starts clean.</summary>
    private async Task SignOutAsync()
    {
        await Page.Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
    }

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
}
