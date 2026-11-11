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
        await Expect(wizard.CommitButton).ToHaveCountAsync(0); // not offered while there are errors
        var report = XlsxFiles.ReadErrorReport(await wizard.DownloadErrorReportAsync(File("report.xlsx")));
        Assert.Equal(Pick(culture, "الأخطاء", "Errors"), report.LastHeader);
        var errorRow = Assert.Single(report.ErrorRows);
        Assert.Equal(badRow + 1, errorRow.Key); // worksheet row numbers start with the header at 1
        // The message names the column and says the person exists; the errored cells are filled #FFC7CE (plan constraint).
        Assert.Contains(Pick(culture, "البريد الإلكتروني", "Email"), errorRow.Value.Errors);
        Assert.Equal("ffffc7ce", errorRow.Value.Fill, ignoreCase: true);

        // Fix the file and import: all 20 people exist, each got an invitation.
        await wizard.DiscardAsync(); // back to the template step
        await wizard.NextAsync();
        XlsxFiles.FillStaffTemplate(template, File("fixed.xlsx"), Rows(names, RowCount));
        await wizard.UploadAsync(File("fixed.xlsx"), Pick(culture, "مدرسة التجمع", "School Altagamo3"));
        await Expect(wizard.CommitButton).ToBeEnabledAsync();
        await wizard.CommitAsync();
        await WaitForBatchAsync("fixed.xlsx", "Committed");

        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.SearchAsync(names.Suffix);
        await Expect(Page.Locator("tbody tr").Filter(new() { HasText = names.Suffix })).ToHaveCountAsync(RowCount, new() { Timeout = 20_000 });
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
            NameAr = names.NameAr("سبق إنشاؤه"), // names carry the suffix: the list search matches names, not emails
            NameEn = names.NameEn("Created first"),
            Email = names.Email("imp5"),
            HomeOrgUnitId = unit,
            Roles = new[] { new { RoleId = teacherRole, OrgUnitId = unit } },
        });

        // The commit re-validates and refuses: the other four rows are not created, and nobody gets an invitation.
        await wizard.CommitAsync();
        await WaitForBatchAsync("valid.xlsx", "Invalid"); // the job re-validated and refused
        var staff = new StaffAdminPage(Page);
        await staff.OpenAsync();
        await staff.SearchAsync(names.Suffix);
        await Expect(staff.Row(names.Email("imp5"))).ToBeVisibleAsync(new() { Timeout = 20_000 }); // the one created through the API
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

    /// <summary>
    /// Waits (up to 90 s) until the newest batch of the staff import with this file name reaches the status; the commit
    /// runs as a background job and the wizard does not show its end (#314).
    /// </summary>
    private async Task WaitForBatchAsync(string fileName, string status)
    {
        var api = new ApiSetup(Page);
        var deadline = DateTime.UtcNow.AddSeconds(90);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var batches = await api.GetAsync("/api/imports/staff");
            var items = batches.ValueKind == System.Text.Json.JsonValueKind.Array ? batches : batches.GetProperty("items");
            last = items.EnumerateArray()
                .Where(b => b.GetProperty("fileName").GetString() == fileName)
                .OrderByDescending(b => b.GetProperty("createdAt").GetDateTime())
                .Select(b => b.GetProperty("status").GetString()).FirstOrDefault();
            if (last == status)
            {
                return;
            }

            await Task.Delay(1500);
        }

        throw new TimeoutException($"The batch '{fileName}' did not reach '{status}' (last status: {last ?? "none"}).");
    }

    /// <summary>Un-skip when the wizard shows the outcome of a commit (#314).</summary>
    private const string NeedsWizardFix = "Wizard stays on the review step after Commit (#314)";

    [Theory(Skip = NeedsWizardFix)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Wizard_shows_the_done_step_after_a_successful_commit(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var wizard = new ImportWizardPage(Page);
        await wizard.OpenAsync();
        var template = await wizard.DownloadTemplateAsync(culture, File("template.xlsx"));
        XlsxFiles.FillStaffTemplate(template, File("valid.xlsx"), Rows(names, 3));
        await wizard.NextAsync();
        await wizard.UploadAsync(File("valid.xlsx"), Pick(culture, "مدرسة التجمع", "School Altagamo3"));

        await wizard.CommitAsync();

        await Expect(wizard.Done).ToBeVisibleAsync(new() { Timeout = 60_000 });
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
