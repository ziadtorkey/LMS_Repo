using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Teaching;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Teaching;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Teaching;

public sealed class EligibilityPageTests : ClassesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeTeachingApi _teaching = new();
    private readonly FakeClassCoursesApi _classCourses = new();
    private readonly FakeCoursesApi _courses = new();
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public EligibilityPageTests()
    {
        Services.AddSingleton<ITeachingApi>(_teaching);
        Services.AddSingleton<IClassCoursesApi>(_classCourses);
        Services.AddSingleton<ICoursesApi>(_courses);
    }

    private async Task<IRenderedComponent<EligibilityPage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.TeacherAssignments.View, Permissions.TeacherAssignments.Manage] : permissions);
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        var page = Render<EligibilityPage>();
        page.WaitForAssertion(() => Assert.Contains("Sara Teacher", page.Markup), Wait);
        return page;
    }

    [Fact]
    public async Task Grid_lists_eligibilities_and_filters_by_course()
    {
        var page = await RenderPageAsync();

        Assert.Contains("Omar Teacher", page.Markup);
        Assert.Contains("Noor Teacher", page.Markup);
        var course = page.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "eligibility-filter-course");
        await page.InvokeAsync(() => course.Instance.ValueChanged.InvokeAsync(FakeCourseIds.For("SCI101")));

        await page.WaitForAssertionAsync(() => Assert.Equal(FakeCourseIds.For("SCI101"), _teaching.LastQuery!.CourseId), Wait);
        await page.WaitForAssertionAsync(() => Assert.DoesNotContain("Sara Teacher", page.Find(".mud-table-body").TextContent), Wait);
        Assert.Contains("Noor Teacher", page.Find(".mud-table-body").TextContent);
    }

    [Fact]
    public async Task Bulk_grant_sends_cross_product()
    {
        var page = await RenderPageAsync();
        _ = page.WaitForElement("[data-testid=eligibility-grant]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=grant-unit]");
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        var teachers = Staff.All.Where(s => s.Summary.Status == Lms.Shared.People.PersonStatuses.Active).Take(2).Select(s => s.Summary.UserId).ToList();
        foreach (var id in teachers) await (await Dialogs.WaitForElementAsync($"input[data-testid=grant-teacher-{id}]", Wait)).ChangeAsync(new() { Value = true });
        await Dialogs.Find("input[data-testid=grant-course-MATH101]").ChangeAsync(new() { Value = true });
        await Dialogs.Find("input[data-testid=grant-course-ARB101]").ChangeAsync(new() { Value = true });
        Assert.Contains("4 teacher-course pairs", Dialogs.Find("[data-testid=grant-pairs]").TextContent);

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Single(_teaching.Grants), Wait);
        var request = _teaching.Grants[0];
        Assert.Equal(FakeUnits.SchoolId, request.OrgUnitId);
        Assert.Equal(teachers.OrderBy(x => x), request.UserIds.OrderBy(x => x));
        Assert.Equal(new[] { FakeCourseIds.For("MATH101"), FakeCourseIds.For("ARB101") }.OrderBy(x => x), request.CourseIds.OrderBy(x => x));
    }

    [Fact]
    public async Task Grant_is_disabled_over_the_200_pair_limit_and_without_a_selection()
    {
        var page = await RenderPageAsync();
        _ = page.WaitForElement("[data-testid=eligibility-grant]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=grant-unit]");

        Assert.True(Dialogs.Find("[data-testid=dialog-save]").HasAttribute("disabled")); // nothing chosen
        Assert.Contains("0 teacher-course pairs (at most 200)", Dialogs.Find("[data-testid=grant-pairs]").TextContent);
    }

    [Fact]
    public async Task Revoke_asks_for_confirmation()
    {
        var page = await RenderPageAsync();
        var noor = _teaching.Eligibility.First(e => e.UserId == FakeTeachingApi.Noor);
        _ = page.WaitForElement($"[data-testid=eligibility-revoke-{noor.Id}]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Equal([noor.Id], _teaching.Revoked), Wait);
        await page.WaitForAssertionAsync(() => Assert.DoesNotContain("Noor Teacher", page.Find(".mud-table-body").TextContent), Wait);
    }

    [Fact]
    public async Task Bulk_assign_from_a_teachers_point_of_view_sends_the_class_courses()
    {
        var page = await RenderPageAsync();
        var sara = _teaching.Eligibility.First(e => e.UserId == FakeTeachingApi.Sara);
        _ = page.WaitForElement($"[data-testid=eligibility-bulk-{sara.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=bulk-list]");

        // Sara is eligible for Mathematics only: the Mathematics class courses of Grade 1 A and B are offered, Science is not.
        var math = _classCourses.All.Where(c => c.CourseCode == "MATH101").ToList();
        Assert.Equal(2, Dialogs.FindAll("input[data-testid^=bulk-pick-]").Count);
        foreach (var cc in math) await Dialogs.Find($"input[data-testid=bulk-pick-{cc.Id}]").ChangeAsync(new() { Value = true });

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Single(_teaching.Bulks), Wait);
        Assert.Equal(FakeTeachingApi.Sara, _teaching.Bulks[0].UserId);
        Assert.Equal(math.Select(c => c.Id).OrderBy(x => x), _teaching.Bulks[0].ClassCourseIds.OrderBy(x => x));
        Assert.Contains("2 assignments created", (await Dialogs.WaitForElementAsync("[data-testid=bulk-result]", Wait)).TextContent);
    }

    [Fact]
    public async Task Manage_actions_hidden_without_manage_and_page_forbidden_without_view()
    {
        var viewer = await RenderPageAsync(Permissions.TeacherAssignments.View);
        Assert.Empty(viewer.FindAll("[data-testid=eligibility-grant]"));
        Assert.Empty(viewer.FindAll("[data-testid^=eligibility-revoke-]"));

        await GrantAsync(Permissions.Staff.View);
        var forbidden = Render<EligibilityPage>();
        forbidden.WaitForAssertion(() => Assert.Contains("You do not have permission", forbidden.Markup));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.TeacherAssignments.View, Permissions.TeacherAssignments.Manage);
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<EligibilityPage>());

        Assert.Contains("أهلية المعلمين", rtl.WaitForElement("[data-testid=eligibility-title]").TextContent);
        Assert.Contains("سارة المعلمة", rtl.WaitForElement(".mud-table-body").TextContent);
        Assert.Contains("منح الأهلية", rtl.Find("[data-testid=eligibility-grant]").TextContent);
    }
}
