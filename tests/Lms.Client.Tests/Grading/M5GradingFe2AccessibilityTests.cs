using System.Globalization;
using Bunit;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Tests.Gradebook;
using Lms.Client.Pages.Grading;
using Lms.Client.Pages.Me;
using Lms.Client.Pages.Settings;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit.Abstractions;

namespace Lms.Client.Tests.Grading;

/// <summary>
/// The cross-page half of the M5 accessibility sweep (Task 39, X-10) for the grading pages that FE2 built on the FE1 gradebook: the publish dialog (T27), the column import
/// dialog (T28), the student grades page (T29), the grade reports page and the grading settings group (T30). Each is scanned with <see cref="A11yScan"/> in Arabic and English:
/// named controls, labelled fields, one h1 on a page, labelled stacked cells, nothing focusable inside aria-hidden.
/// </summary>
public sealed class M5GradingFe2AccessibilityTests : StaffTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private readonly ITestOutputHelper _output;
    private readonly FakeGradebookApi _api = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public M5GradingFe2AccessibilityTests(ITestOutputHelper output)
    {
        _output = output;
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IGradebookUnit>(new FakeGradebookUnit());
        Services.AddSingleton<IViewport>(new FakeViewport());
        Services.AddSingleton<IClassesApi>(new FakeClassesApi());
        Services.AddSingleton<IClassCoursesApi>(new FakeClassCoursesApi());
        Services.AddSingleton<Lms.Client.Services.Api.IGuardiansApi>(new FakeGuardiansApi());
    }

    private async Task PrepareAsync(string culture, params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
    }

    private void AssertClean(IRenderedComponent<IComponent> cut, string where, bool requireOneHeading = true)
    {
        var problems = A11yScan.Problems(cut, requireOneHeading);
        foreach (var p in problems) _output.WriteLine($"{where}: {p}");
        Assert.Empty(problems);
    }

    private IRenderedComponent<GradebookPage> OpenGradebook()
    {
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=gradebook-loading]")), Wait);
        return page;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_publish_dialog_is_clean(string culture)
    {
        await PrepareAsync(culture, Permissions.Grades.View, Permissions.Grades.Manage, Permissions.Grades.Publish);
        var page = OpenGradebook();

        await page.Find("[data-testid=gradebook-publish]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=publish-submit]");

        AssertClean(_dialogs, "publish dialog", requireOneHeading: false);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_import_dialog_is_clean_before_and_after_a_refused_file(string culture)
    {
        await PrepareAsync(culture, Permissions.Grades.View, Permissions.Grades.Manage, Permissions.Grades.Publish);
        var page = OpenGradebook();

        await page.Find("[data-testid=gradebook-import]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=import-file]");
        AssertClean(_dialogs, "import dialog", requireOneHeading: false);

        var upload = _dialogs.FindComponent<InputFile>();
        await _dialogs.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[16], "grades.csv")));
        _dialogs.WaitForElement("[data-testid=import-error]");
        AssertClean(_dialogs, "import dialog with an error", requireOneHeading: false);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_student_grades_page_is_clean(string culture)
    {
        await PrepareAsync(culture, Permissions.Grades.ViewOwn, Permissions.Students.ViewChildren);
        await _api.PublishAsync(Cc, new PublishItemsRequest([FakeGradebookApi.AutoItemId, FakeGradebookApi.ManualItemId], PublishModes.Now, null, true));

        var page = Render<MyGradesPage>();
        page.WaitForElement("[data-testid=grades-title]");
        page.WaitForElement($"[data-testid=grades-item-{FakeGradebookApi.AutoItemId}]");

        AssertClean(page, "student grades");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_grade_reports_page_is_clean(string culture)
    {
        await PrepareAsync(culture, Permissions.Grades.ViewReports);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"grade-reports?kind=student&studentId={FakeGradebookApi.StudentIds[0]}");

        var page = Render<GradeReportsPage>();
        page.WaitForElement("[data-testid=reports-as-of]");

        AssertClean(page, "grade reports");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_grade_settings_group_is_clean(string culture)
    {
        await PrepareAsync(culture, Permissions.Settings.View);

        var group = Render<GradeSettingsGroup>(p => p.Add(x => x.UnitId, Guid.NewGuid()).Add(x => x.CanEdit, true));
        group.WaitForElement("[data-testid=grade-setting-decimals]");

        AssertClean(group, "grade settings", requireOneHeading: false);
    }
}
