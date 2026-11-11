using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>
/// M1 exit demo 2, access half (plan Task 37, #72): the seeded school client's admin manages roles and assigns a role
/// to a brand-new person, who accepts the invitation and signs in. Ar and en. Every test uses unique names and cleans
/// up after itself (custom role deleted, renamed built-in role restored), so reruns on the same database stay green.
/// </summary>
public sealed class AccessAdminTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    // Built-in labels of the school-group client (BuiltInRoles, Lms.Shared).
    private const string ManagerAr = "مدير المدرسة";
    private const string ManagerEn = "School Manager";

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_creates_Exam_Officer_custom_role_and_renames_Manager_label(string culture)
    {
        var names = M1Names.New();
        var (examAr, examEn) = ($"ضابط الامتحانات {names.Suffix}", $"Exam Officer {names.Suffix}");
        var (managerNewAr, managerNewEn) = ($"مدير {names.Suffix}", $"Principal {names.Suffix}");
        string Show(string ar, string en) => culture == "ar" ? ar : en;

        await StartAsync(culture: culture);
        await SignInAsOrgAdminAsync();
        var roles = new RolesAdminPage(Page);
        await roles.OpenAsync();

        // A custom role with one permission; reopening it shows the permission still ticked.
        await roles.CreateAsync(examAr, examEn, Show(examAr, examEn), "Audit.View");
        await roles.OpenEditorAsync(Show(examAr, examEn));
        await Expect(roles.Permission("Audit.View")).ToBeCheckedAsync();
        await Expect(roles.Permission("Settings.Update")).Not.ToBeCheckedAsync();
        await roles.CancelEditAsync();

        // The built-in Manager keeps its kind and cannot be deleted, but its label can change.
        await Expect(roles.Row(Show(ManagerAr, ManagerEn)).Locator("[data-testid^='role-delete-']")).ToBeDisabledAsync();
        var renamed = false;
        try
        {
            await roles.RenameAsync(Show(ManagerAr, ManagerEn), managerNewAr, managerNewEn, Show(managerNewAr, managerNewEn));
            renamed = true;
            await Expect(roles.Row(Show(ManagerAr, ManagerEn))).ToHaveCountAsync(0);
        }
        finally
        {
            if (renamed)
            {
                await roles.RenameAsync(Show(managerNewAr, managerNewEn), ManagerAr, ManagerEn, Show(ManagerAr, ManagerEn));
            }

            await roles.DeleteAsync(Show(examAr, examEn));
        }

        await Expect(roles.Row(Show(ManagerAr, ManagerEn))).ToBeVisibleAsync();
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Admin_assigns_teacher_role_to_new_email_teacher_accepts_invitation_and_sees_only_section_context(string culture)
    {
        var names = M1Names.New();
        var email = names.AdminEmail("teacher");

        await StartAsync(culture: culture);
        await SignInAsOrgAdminAsync();
        var assignments = new AssignmentsAdminPage(Page);
        await assignments.OpenAsync();
        await assignments.AssignAsync(email, "معلم اختبار", $"E2E Teacher {names.Suffix}", "Teacher", "KG-S1");

        await assignments.IncludeUnitsBelowAsync();
        var row = assignments.Row(email);
        await Expect(row).ToBeVisibleAsync();
        await Expect(row).ToContainTextAsync(culture == "ar" ? "المعلم" : "Teacher");
        await Expect(row).ToContainTextAsync(Resx.In(culture, "AssignmentStatus_Valid"));

        // The new person got an invitation, sets a password, and signs in.
        await Page.Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
        using var mail = new MailpitClient();
        var link = await mail.WaitForAccountLinkAsync(email);
        await new ResetPasswordPage(Page).SetPasswordAsync(link, names.NewPassword);
        await new LoginPage(Page).SignInAsync(email, names.NewPassword);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();

        // One assignment: no chooser, the section is the context, and admin menus are not offered.
        Assert.DoesNotContain("/choose-context", Page.Url, StringComparison.Ordinal);
        await Expect(shell.ContextSwitcher).ToContainTextAsync(culture == "ar" ? "الفصل 1" : "Section 1");
        await Expect(shell.ContextSwitcher).ToContainTextAsync(culture == "ar" ? "المعلم" : "Teacher");
        await Expect(shell.Nav("roles")).ToHaveCountAsync(0);
        await Expect(shell.Nav("audit")).ToHaveCountAsync(0);
        await Expect(shell.Nav("clients")).ToHaveCountAsync(0);
    }

    private async Task SignInAsOrgAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
    }
}
