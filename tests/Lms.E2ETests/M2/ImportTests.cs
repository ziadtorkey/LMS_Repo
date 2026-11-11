using System.Text.Json;
using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M2;

/// <summary>
/// M2 exit demo 2 (plan Task 36, #177): staff Excel import in Arabic (RTL) and English. Runs as the seeded school
/// group's Org Admin; each test imports teachers under the seeded KG section with unique emails (<see cref="M2Names"/>),
/// so reruns on the same database never collide. Files are saved under the test's own temp folder.
/// </summary>
public sealed class ImportTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const int RowCount = 20;

    private string _folder = string.Empty;

    private string File(string name)
    {
        _folder = _folder.Length == 0 ? Directory.CreateTempSubdirectory("lms-e2e-import-").FullName : _folder;
        return Path.Combine(_folder, name);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Staff_import_with_one_bad_row_shows_preview_errors_and_error_report_then_fixed_file_imports_all(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var wizard = new ImportWizardPage(Page);
        await wizard.OpenAsync();

        // The template, filled with 20 teachers; row 5 reuses an email that already exists in the client.
        var template = await wizard.DownloadTemplateAsync(culture, File($"template-{culture}.xlsx"));
        var badRow = 5;
        var rows = Rows(names, RowCount, badEmailAt: badRow, badEmail: E2ESettings.SeedUser("teacher"));
        XlsxFiles.FillStaffTemplate(template, File("with-error.xlsx"), rows);
        await wizard.NextAsync();
        await wizard.UploadAsync(File("with-error.xlsx"), Pick(culture, "مدرسة التجمع", "School Altagamo3"));

        // Invalid: nothing can be committed, and the report marks exactly the bad row.
        await Expect(wizard.Blocked).ToBeVisibleAsync();
        await Expect(wizard.CommitButton).ToBeDisabledAsync();
        var report = XlsxFiles.ReadErrorReport(await wizard.DownloadErrorReportAsync(File("report.xlsx")));
        Assert.Equal(Pick(culture, "الأخطاء", "Errors"), report.LastHeader);
        var errorRow = Assert.Single(report.ErrorRows);
        Assert.Equal(badRow + 1, errorRow.Key); // worksheet row numbers start with the header at 1
        Assert.Contains(Resx.In(culture, "Error_duplicate_person_email"), errorRow.Value.Errors);

        // Fix the file and import: all 20 people exist, each got an invitation.
        await wizard.DiscardAsync();
        XlsxFiles.FillStaffTemplate(template, File("fixed.xlsx"), Rows(names, RowCount));
        await wizard.UploadAsync(File("fixed.xlsx"), Pick(culture, "مدرسة التجمع", "School Altagamo3"));
        await Expect(wizard.CommitButton).ToBeEnabledAsync();
        await wizard.CommitAsync();
        await Expect(wizard.Done).ToBeVisibleAsync();

        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.SearchAsync(names.Suffix);
        await Expect(Page.Locator("tbody tr").Filter(new() { HasText = names.Suffix })).ToHaveCountAsync(RowCount);
        using var mail = new MailpitClient();
        for (var i = 1; i <= RowCount; i++)
        {
            await mail.WaitForAccountLinkAsync(names.Email($"imp{i}"));
        }

        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Arabic_template_headers_are_accepted(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var wizard = new ImportWizardPage(Page);
        await wizard.OpenAsync();

        // Whatever the UI language, a file with Arabic headers validates.
        var template = await wizard.DownloadTemplateAsync("ar", File("template-ar.xlsx"));
        XlsxFiles.FillStaffTemplate(template, File("arabic.xlsx"), Rows(names, 3));
        await wizard.NextAsync();
        await wizard.UploadAsync(File("arabic.xlsx"), Pick(culture, "مدرسة التجمع", "School Altagamo3"));

        await Expect(wizard.Blocked).ToHaveCountAsync(0);
        await Expect(wizard.CommitButton).ToBeEnabledAsync();
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Commit_after_a_conflicting_person_appears_imports_nothing(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var wizard = new ImportWizardPage(Page);
        await wizard.OpenAsync();
        var template = await wizard.DownloadTemplateAsync(culture, File("template.xlsx"));
        XlsxFiles.FillStaffTemplate(template, File("valid.xlsx"), Rows(names, 5));
        await wizard.NextAsync();
        await wizard.UploadAsync(File("valid.xlsx"), Pick(culture, "مدرسة التجمع", "School Altagamo3"));
        await Expect(wizard.CommitButton).ToBeEnabledAsync(); // Valid: nothing is stored yet

        // Someone else creates the person of the last row between preview and commit (through the API, in the same client).
        var api = new ApiSetup(Page);
        var roles = await api.GetAsync("/api/roles");
        var teacherRole = roles.EnumerateArray().First(r => r.GetProperty("builtInKey").GetString() == "Teacher").GetProperty("id").GetGuid();
        var unit = await api.UnitIdByCodeAsync("KG-S1");
        await api.PostAsync("/api/staff", new
        {
            NameAr = "سبق إنشاؤه",
            NameEn = "Created first",
            Email = names.Email("imp5"),
            HomeOrgUnitId = unit,
            Roles = new[] { new { RoleId = teacherRole, OrgUnitId = unit } },
        });

        // The commit re-validates and refuses: the other four rows are not created, and nobody gets an invitation.
        await wizard.CommitAsync();
        await Expect(wizard.Failed.Or(wizard.Blocked).First).ToBeVisibleAsync();
        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.SearchAsync(names.Suffix);
        await Expect(staff.Row(names.Email("imp5"))).ToBeVisibleAsync(); // the one created through the API
        for (var i = 1; i <= 4; i++)
        {
            await Expect(staff.Row(names.Email($"imp{i}"))).ToHaveCountAsync(0);
        }

        using var mail = new MailpitClient();
        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal(0, await mail.CountAsync(names.Email($"imp{i}")));
        }
    }

    /// <summary>Teachers at the seeded KG section; the email of row <paramref name="badEmailAt"/> can be replaced.</summary>
    private static List<XlsxFiles.StaffRow> Rows(M2Names names, int count, int? badEmailAt = null, string? badEmail = null) =>
        Enumerable.Range(1, count).Select(i => new XlsxFiles.StaffRow(
            names.NameAr($"موظف {i}"), names.NameEn($"Imported {i}"),
            i == badEmailAt ? badEmail! : names.Email($"imp{i}"), "Teacher", "KG-S1")).ToList();

    private static string Pick(string culture, string ar, string en) => culture == "ar" ? ar : en;

    private async Task SignInAsSeededAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        // The context is picked after the shell renders and stored: a page opened by URL reads it from local storage.
        await Expect(shell.ContextSwitcher).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
    }
}
