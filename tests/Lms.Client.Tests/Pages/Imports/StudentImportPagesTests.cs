using System.Globalization;
using Bunit;
using Lms.Client.Components.Imports;
using Lms.Client.Pages.Classes;
using Lms.Client.Pages.Students;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Imports;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Imports;

public sealed class StudentImportPagesTests : StudentsTestContext
{
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(8); // the wizard polls every 2 s
    private readonly StubImportsApi _imports = new();

    public StudentImportPagesTests() => Services.AddSingleton<IImportsApi>(_imports);

    private async Task GrantImportAsync() => await GrantAsync(Permissions.Students.Import);

    private async Task CommitThroughWizardAsync(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut)
    {
        _imports.Statuses = [ImportStatuses.Valid];
        await (await cut.WaitForElementAsync("[data-testid=import-next]")).ClickAsync(new());
        var upload = cut.FindComponent<InputFile>();
        await cut.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[8], "students.xlsx")));
        await cut.WaitForElement("[data-testid=import-upload]:not([disabled])").ClickAsync(new());
        await (await cut.WaitForElementAsync("[data-testid=import-commit]:not([disabled])", Slow)).ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());
    }

    [Fact]
    public async Task Student_page_uses_students_kind_and_explains_class_code_and_default_password()
    {
        Culture.Current = new CultureInfo("en");
        await GrantImportAsync();
        RenderProviders();
        var cut = Render<StudentImportPage>();

        cut.WaitForElement("[data-testid=import-wizard]");
        var help = cut.Find("[data-testid=student-import-help]").TextContent;
        Assert.Contains("ClassCode", help);
        Assert.Contains("DefaultPassword", help);
        Assert.Contains("generated", help);
        await cut.Find("[data-testid=import-template]").ClickAsync(new());
        Assert.Equal(ImportKinds.Students, _imports.LastKind);
    }

    [Fact]
    public async Task Pages_are_forbidden_without_their_import_permission()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Students.View);
        RenderProviders();

        var students = Render<StudentImportPage>();
        var classes = Render<ClassImportPage>();

        students.WaitForAssertion(() => Assert.Contains("You do not have permission", students.Markup));
        classes.WaitForAssertion(() => Assert.Contains("You do not have permission", classes.Markup));
        Assert.Empty(students.FindAll("[data-testid=import-wizard]"));
        Assert.Empty(classes.FindAll("[data-testid=import-wizard]"));
    }

    [Fact]
    public async Task Each_import_page_follows_its_own_permission()
    {
        Culture.Current = new CultureInfo("en");
        RenderProviders();

        // Students.Import alone: the student import opens, the class placement import does not.
        await GrantAsync(Permissions.Students.Import);
        var students = Render<StudentImportPage>();
        var classes = Render<ClassImportPage>();
        students.WaitForElement("[data-testid=import-wizard]");
        classes.WaitForAssertion(() => Assert.Contains("You do not have permission", classes.Markup));

        // Enrollment.Import alone: the other way round.
        await GrantAsync(Permissions.Enrollment.Import);
        var students2 = Render<StudentImportPage>();
        var classes2 = Render<ClassImportPage>();
        classes2.WaitForElement("[data-testid=import-wizard]");
        students2.WaitForAssertion(() => Assert.Contains("You do not have permission", students2.Markup));
    }

    [Fact]
    public async Task Credentials_button_shown_once_after_commit_with_the_one_time_warning()
    {
        Culture.Current = new CultureInfo("en");
        await GrantImportAsync();
        RenderProviders();
        var cut = Render<StudentImportPage>();
        await CommitThroughWizardAsync(cut);

        await cut.WaitForElementAsync("[data-testid=credentials-sheet]", Slow);
        Assert.Contains("downloaded once", cut.Find("[data-testid=credentials-once-warning]").TextContent);

        await cut.Find("[data-testid=credentials-download]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Equal(1, _imports.CredentialDownloads));
        await cut.WaitForAssertionAsync(() => Assert.True(cut.Find("[data-testid=credentials-download]").HasAttribute("disabled")));
        Assert.Contains("downloaded", cut.Find("[data-testid=credentials-message]").TextContent);
        JSInterop.VerifyInvoke("lmsDownloadFile", 1);
    }

    [Fact]
    public async Task Credentials_already_taken_shows_that_it_is_gone()
    {
        Culture.Current = new CultureInfo("en");
        _ = _imports.DownloadCredentialsAsync(ImportKinds.Students, Guid.NewGuid()); // someone took the single download
        Render<MudPopoverProvider>();
        var cut = Render<CredentialsSheetButton>(p => p.Add(b => b.Kind, ImportKinds.Students).Add(b => b.BatchId, Guid.NewGuid()));

        await cut.Find("[data-testid=credentials-download]").ClickAsync(new());

        Assert.Contains("no longer available", (await cut.WaitForElementAsync("[data-testid=credentials-message]")).TextContent);
        Assert.True(cut.Find("[data-testid=credentials-download]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Class_import_page_uses_class_students_kind_with_the_current_year_banner()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Enrollment.Import, Permissions.AcademicYears.View);
        RenderProviders();
        var cut = Render<ClassImportPage>();
        cut.WaitForElement("[data-testid=import-wizard]");
        Assert.Empty(cut.FindAll("[data-testid=class-import-year]")); // the years load after the page rendered

        await Services.GetRequiredService<IYearStore>().LoadAsync();

        var year = Services.GetRequiredService<IYearStore>().Selected;
        Assert.NotNull(year);
        await cut.WaitForAssertionAsync(() => Assert.Contains(year!.NameEn, cut.Find("[data-testid=class-import-year]").TextContent));
        Assert.Contains("class code", cut.Find("[data-testid=class-import-help]").TextContent);
        await cut.Find("[data-testid=import-template]").ClickAsync(new());
        Assert.Equal(ImportKinds.ClassStudents, _imports.LastKind);
    }

    [Fact]
    public async Task Sensitive_column_is_masked_in_preview_table_and_error_tooltip()
    {
        Culture.Current = new CultureInfo("en");
        Render<MudPopoverProvider>();
        ImportColumnDto[] columns =
        [
            new("nameEn", "الاسم", "Name", true, null),
            new("defaultPassword", "كلمة المرور", "Default password", false, null, Sensitive: true),
        ];
        // A faulty server that echoes the raw value must still not reach the screen.
        var rows = new[]
        {
            new ImportRowDto(2, new Dictionary<string, string?> { ["nameEn"] = "Ann", ["defaultPassword"] = "Secret#123" }, []),
            new ImportRowDto(3, new Dictionary<string, string?> { ["nameEn"] = "Bob", ["defaultPassword"] = "Hunter2!" },
                [new ImportRowErrorDto("defaultPassword", "weak_password", "Password 'Hunter2!' is too weak")]),
            new ImportRowDto(4, new Dictionary<string, string?> { ["nameEn"] = "Cy", ["defaultPassword"] = null }, []),
        };
        var cut = Render<ImportPreviewTable>(p => p
            .Add(t => t.Columns, columns)
            .Add(t => t.LoadRows, (page, size, errorsOnly, _) => Task.FromResult(new PagedResult<ImportRowDto>(rows, rows.Length, page, size))));

        cut.WaitForElement("[data-testid=import-row-3]");
        Assert.DoesNotContain("Secret#123", cut.Markup);
        Assert.DoesNotContain("Hunter2!", cut.Markup);
        Assert.Contains(ImportMasks.Sensitive, cut.Find("[data-testid=import-row-2]").ParentElement!.TextContent);
        var errored = cut.Find("[data-testid=import-cell-3-defaultPassword]");
        Assert.Equal(ImportMasks.Sensitive, errored.TextContent);
        Assert.DoesNotContain("Hunter2", errored.GetAttribute("title"));
        // An empty sensitive cell stays empty (no mask).
        Assert.DoesNotContain(ImportMasks.Sensitive, cut.Find("[data-testid=import-row-4]").ParentElement!.TextContent);
    }

    [Fact]
    public async Task Student_list_import_buttons_follow_their_own_permission()
    {
        await GrantAsync(Permissions.Students.View);
        RenderProviders();
        var plain = Render<StudentListPage>();
        plain.WaitForElement("[data-testid=students-title]");
        Assert.Empty(plain.FindAll("[data-testid=student-import]"));
        Assert.Empty(plain.FindAll("[data-testid=class-import]"));

        await GrantAsync(Permissions.Students.View, Permissions.Students.Import);
        var studentsOnly = Render<StudentListPage>();
        Assert.EndsWith("students/import", studentsOnly.WaitForElement("[data-testid=student-import]").GetAttribute("href"));
        Assert.Empty(studentsOnly.FindAll("[data-testid=class-import]"));

        await GrantAsync(Permissions.Students.View, Permissions.Enrollment.Import);
        var classesOnly = Render<StudentListPage>();
        Assert.EndsWith("classes/import", classesOnly.WaitForElement("[data-testid=class-import]").GetAttribute("href"));
        Assert.Empty(classesOnly.FindAll("[data-testid=student-import]"));
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Students.Import, Permissions.Enrollment.Import);
        RenderProviders();
        var students = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<StudentImportPage>());
        students.WaitForElement("[data-testid=import-wizard]");
        Assert.Contains("استيراد الطلاب", students.Find("[data-testid=student-import-title]").TextContent); // the page header carries the title
        Assert.Contains("ClassCode", students.Find("[data-testid=student-import-help]").TextContent);

        var classes = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<ClassImportPage>());
        classes.WaitForElement("[data-testid=import-wizard]");
        Assert.Contains("إضافة طلاب إلى فصول", classes.Find("[data-testid=class-import-title]").TextContent);
    }
}
