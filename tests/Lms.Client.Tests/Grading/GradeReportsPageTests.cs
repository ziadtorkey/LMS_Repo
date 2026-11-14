using System.Globalization;
using Bunit;
using Lms.Client.Pages.Grading;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Grading;

/// <summary>The grade reports page (M5 Task 30, FR-GRD-04): the as-of time, the Excel export, bilingual, RTL.</summary>
public sealed class GradeReportsPageTests : StaffTestContext
{
    private readonly FakeGradebookApi _api = new();
    private readonly string _studentUrl = $"grade-reports?kind=student&studentId={FakeGradebookApi.StudentIds[0]}";

    public GradeReportsPageTests()
    {
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IClassesApi>(new FakeClassesApi());
        Services.AddSingleton<IClassCoursesApi>(new FakeClassCoursesApi());
    }

    private async Task<IRenderedComponent<GradeReportsPage>> RenderAsync(string culture = "en", string? url = null, params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions.Length == 0 ? [Permissions.Grades.ViewReports] : permissions);
        RenderProviders();
        Services.GetRequiredService<NavigationManager>().NavigateTo(url ?? _studentUrl);
        return Render<GradeReportsPage>();
    }

    [Fact]
    public async Task Reports_show_the_as_of_time()
    {
        var page = await RenderAsync();

        var asOf = page.WaitForElement("[data-testid=reports-as-of]").TextContent;
        Assert.Contains("Calculated at", asOf);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}", asOf);
        Assert.NotEmpty(page.FindAll("tbody tr"));
    }

    [Fact]
    public async Task Report_export_downloads()
    {
        var page = await RenderAsync();
        page.WaitForElement("[data-testid=reports-as-of]");

        await page.Find("[data-testid=reports-export]").ClickAsync(new());

        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "lmsDownloadFile");
        Assert.Equal("grades-student.xlsx", call.Arguments[0]);
    }

    [Fact]
    public async Task Without_a_choice_the_page_asks_for_one_and_exports_nothing()
    {
        var page = await RenderAsync(url: "grade-reports");

        Assert.NotNull(page.WaitForElement("[data-testid=reports-pick]"));
        Assert.True(page.Find("[data-testid=reports-export]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_page_needs_the_reports_permission()
    {
        var page = await RenderAsync("en", null, Permissions.Grades.View);

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=reports-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync("ar");

        Assert.Equal("تقارير الدرجات", page.WaitForElement("[data-testid=reports-title]").TextContent.Trim());
        Assert.Contains("تم الحساب في", page.WaitForElement("[data-testid=reports-as-of]").TextContent);
        Assert.Contains("dir=\"ltr\"", page.Find("[data-testid=reports-as-of]").InnerHtml); // the time stays left to right
    }
}
