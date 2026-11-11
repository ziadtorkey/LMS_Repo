using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>
/// M1 exit demo 2, access half (plan Task 37, #72): the seeded school client's admin manages roles (custom role, renamed
/// built-in label). Assigning a role to a person is covered by the M2 staff demo (#176), because since D31 roles are
/// assigned to existing staff members. Ar and en. Every test uses unique names and cleans
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

    private async Task SignInAsOrgAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
    }
}
