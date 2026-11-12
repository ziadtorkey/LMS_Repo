using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Classes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Classes;

public sealed class DistributionMatrixPageTests : ClassesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeClassCoursesApi _classCourses = new();
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public DistributionMatrixPageTests() => Services.AddSingleton<IClassCoursesApi>(_classCourses);

    private async Task<IRenderedComponent<DistributionMatrixPage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Classes.View, Permissions.Classes.ManageCourses] : permissions);
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        var page = Render<DistributionMatrixPage>();
        // Choose the school: the fake classes (Grade 1 A / B) belong to it.
        await page.InvokeAsync(() => page.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        await page.WaitForElementAsync("[data-testid=matrix-grid]", Wait);
        return page;
    }

    [Fact]
    public async Task Matrix_shows_cells_where_the_class_has_the_course_and_marks_optional()
    {
        var page = await RenderPageAsync();

        Assert.True(page.Find("input[data-testid=cell-1A-MATH101]").HasAttribute("checked"));
        Assert.True(page.Find("input[data-testid=cell-1A-SCI101]").HasAttribute("checked"));
        Assert.False(page.Find("input[data-testid=cell-1B-SCI101]").HasAttribute("checked"));
        Assert.NotEmpty(page.FindAll("[data-testid=optional-1A-SCI101]")); // Science is optional in 1A
        Assert.Empty(page.FindAll("[data-testid=optional-1A-MATH101]"));
        Assert.Contains("2026/2027", page.Find("[data-testid=matrix-year]").TextContent);
    }

    [Fact]
    public async Task Matrix_toggle_adds_and_removes_class_course()
    {
        var page = await RenderPageAsync();

        await page.Find("input[data-testid=cell-1B-SCI101]").ChangeAsync(new() { Value = true });
        await page.WaitForAssertionAsync(() => Assert.Single(_classCourses.Adds), Wait);
        Assert.Equal(FakeClasses.Grade1B.Id, _classCourses.Adds[0].ClassId);
        Assert.Equal([FakeCourseIds.For("SCI101")], _classCourses.Adds[0].Request.CourseIds);
        await page.WaitForAssertionAsync(() => Assert.True(page.Find("input[data-testid=cell-1B-SCI101]").HasAttribute("checked")), Wait);

        var added = _classCourses.All.First(c => c.ClassId == FakeClasses.Grade1B.Id && c.CourseCode == "SCI101");
        await page.Find("input[data-testid=cell-1B-SCI101]").ChangeAsync(new() { Value = false });
        await page.WaitForAssertionAsync(() => Assert.Equal([added.Id], _classCourses.Removed), Wait);
        await page.WaitForAssertionAsync(() => Assert.False(page.Find("input[data-testid=cell-1B-SCI101]").HasAttribute("checked")), Wait);
    }

    [Fact]
    public async Task Refused_removal_shows_the_message_and_keeps_the_cell()
    {
        var page = await RenderPageAsync();
        _classCourses.Failure = new ApiProblemException(409, "class_course_in_use", null, null);

        await page.Find("input[data-testid=cell-1A-MATH101]").ChangeAsync(new() { Value = false });

        await _snackbars.WaitForAssertionAsync(() => Assert.Contains("cannot be removed", _snackbars.Markup), Wait);
        await page.WaitForAssertionAsync(() => Assert.True(page.Find("input[data-testid=cell-1A-MATH101]").HasAttribute("checked")), Wait);
    }

    [Fact]
    public async Task Assign_to_all_calls_bulk_assign()
    {
        var page = await RenderPageAsync();

        _ = page.Find("[data-testid=assign-all-ARB101]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Single(_classCourses.BulkAssigns), Wait);
        var request = _classCourses.BulkAssigns[0];
        Assert.Equal((FakeUnits.SchoolId, YearId, FakeCourseIds.For("ARB101"), false), (request.OrgUnitId, request.AcademicYearId, request.CourseId, request.IsOptional));
        await _snackbars.WaitForAssertionAsync(() => Assert.Contains("2 classes got the course", _snackbars.Markup), Wait);
        await page.WaitForAssertionAsync(() => Assert.True(page.Find("input[data-testid=cell-1B-ARB101]").HasAttribute("checked")), Wait);
    }

    [Fact]
    public async Task Grid_scrolls_inside_its_own_box_with_sticky_headers()
    {
        var page = await RenderPageAsync();

        var grid = page.Find("[data-testid=matrix-grid]");
        Assert.Contains("overflow: auto", grid.GetAttribute("style"));
        Assert.Contains("max-width: 100%", grid.GetAttribute("style"));
        Assert.Contains("position: sticky", page.Find("thead th").GetAttribute("style"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Without_ManageCourses_the_cells_are_read_only()
    {
        var page = await RenderPageAsync(Permissions.Classes.View);

        Assert.True(page.Find("input[data-testid=cell-1A-MATH101]").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-testid=assign-all-ARB101]"));
    }

    [Fact]
    public async Task Page_is_forbidden_without_Classes_View_and_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Staff.View);
        RenderProviders();
        var forbidden = Render<DistributionMatrixPage>();
        forbidden.WaitForAssertion(() => Assert.Contains("You do not have permission", forbidden.Markup));

        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Classes.View, Permissions.Classes.ManageCourses);
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<DistributionMatrixPage>());
        Assert.Contains("مصفوفة التوزيع", rtl.WaitForElement("[data-testid=matrix-title]").TextContent);
        Assert.Contains("اختر وحدة", rtl.Find("[data-testid=matrix-choose]").TextContent);
    }
}
