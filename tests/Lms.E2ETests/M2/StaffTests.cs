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
    /// <summary>Un-skipped as the pieces reach main: staff API (#187), edit/status (#198, #190), share (#196), job trigger (#201).</summary>
    private const string NeedsStaffApi = "Needs the staff API (#187) on main";

    [Theory(Skip = NeedsStaffApi)]
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
            RoleUnitName: Pick(culture, "الفصل 1", "Section 1")));
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

    [Theory(Skip = NeedsStaffApi)]
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
            Pick(culture, "رياض الأطفال", "KG"), Pick(culture, "المعلم", "Teacher"), Pick(culture, "الفصل 1", "Section 1")));

        // The same email under the other stage is refused (checks are client-wide, D26) and names only the field.
        await staff.FillAndSaveAsync(new StaffAdminPage.NewStaff(
            names.NameAr("آخر"), names.NameEn("Other"), email, null,
            Pick(culture, "المرحلة الابتدائية", "Primary"), Pick(culture, "المعلم", "Teacher"), Pick(culture, "المرحلة الابتدائية", "Primary")));

        await Expect(staff.DialogError).ToContainTextAsync(Resx.In(culture, "Error_duplicate_person_email"));
        await AssertDirectionAsync(culture);
    }

    private static string Pick(string culture, string ar, string en) => culture == "ar" ? ar : en;

    private async Task SignInAsSeededAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
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
