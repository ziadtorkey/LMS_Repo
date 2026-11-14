using System.Globalization;
using System.Reflection;
using Bunit;
using Lms.Client.Pages.Guardians;
using Lms.Client.Pages.Me;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Grading;

/// <summary>The student and guardian grades pages (M5 Task 29, FR-GRD-04/05, D58) against the fake server: published items only, a total per course, no overall result.</summary>
public sealed class StudentGradesTests : StaffTestContext
{
    private static readonly Guid Course = FakeGradebookApi.DemoClassCourseId;
    private readonly FakeGradebookApi _api = new();

    public StudentGradesTests() => Services.AddSingleton<IGradebookApi>(_api);

    private async Task<IRenderedComponent<TPage>> RenderAsync<TPage>(string culture = "en", Action<ComponentParameterCollectionBuilder<TPage>>? parameters = null, params string[] permissions)
        where TPage : IComponent
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions.Length == 0 ? [Permissions.Grades.ViewOwn, Permissions.Students.ViewChildren] : permissions);
        RenderProviders();
        var page = parameters is null ? Render<TPage>() : Render<TPage>(parameters);
        page.WaitForElement("[data-testid=grades-title]");
        return page;
    }

    private Task PublishAsync(params Guid[] items) => _api.PublishAsync(Course, new PublishItemsRequest(items, PublishModes.Now, null, true));

    [Fact]
    public async Task Hidden_item_in_a_fake_response_is_never_rendered()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId); // Assignment 1 is published; Quiz 1 stays hidden
        var page = await RenderAsync<MyGradesPage>();
        page.WaitForElement($"[data-testid=grades-item-{FakeGradebookApi.AutoItemId}]");

        Assert.Contains("Assignment 1", page.Markup);
        Assert.DoesNotContain("Quiz 1", page.Markup); // a hidden item has no row, no title, no placeholder
        Assert.Empty(page.FindAll($"[data-testid=grades-item-{FakeGradebookApi.ManualItemId}]"));
    }

    [Fact]
    public async Task Nothing_published_shows_the_empty_state_and_no_item_titles()
    {
        var page = await RenderAsync<MyGradesPage>();

        Assert.NotNull(page.Find($"[data-testid=grades-empty-{Course}]"));
        Assert.DoesNotContain("Quiz 1", page.Markup);
        Assert.DoesNotContain("Assignment 1", page.Markup);
    }

    [Fact]
    public async Task No_overall_result_across_courses()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId, FakeGradebookApi.ManualItemId);
        _api.ShapeStudentGrades = data => data with { Courses = [data.Courses[0], data.Courses[0] with { ClassCourseId = Guid.NewGuid(), CourseNameAr = "العلوم", CourseNameEn = "Science" }] };
        var page = await RenderAsync<MyGradesPage>();
        page.WaitForElement("[data-testid=grades-courses]");

        Assert.Equal(2, page.FindAll("[data-testid^=grades-total-]").Count); // one total per course
        Assert.DoesNotContain("overall", page.Markup, StringComparison.OrdinalIgnoreCase); // and none across them (L18)
        Assert.DoesNotContain("GPA", page.Markup);
    }

    [Fact]
    public async Task Course_total_is_labelled_published_only_and_its_band_points_belong_to_the_course()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId, FakeGradebookApi.ManualItemId);
        var page = await RenderAsync<MyGradesPage>();
        page.WaitForElement($"[data-testid=grades-percent-{Course}]");

        Assert.Contains("Based on published grades", page.Find($"[data-testid=grades-basis-{Course}]").TextContent);
        Assert.Contains("%", page.Find($"[data-testid=grades-percent-{Course}]").TextContent);
        if (page.FindAll($"[data-testid=grades-band-points-{Course}]").Count > 0)
        {
            Assert.Contains("points for this course", page.Find($"[data-testid=grades-band-points-{Course}]").TextContent); // never a cumulative GPA
        }
    }

    [Fact]
    public async Task Band_legend_in_the_user_language()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId);
        var en = await RenderAsync<MyGradesPage>();
        en.WaitForElement($"[data-testid=grades-legend-{Course}]");
        var scale = (await _api.GetMyGradesAsync()).Courses[0].Legend;
        Assert.Contains(scale.ScaleNameEn, en.Find($"[data-testid=grades-legend-{Course}] summary").TextContent);
        Assert.Equal(scale.Bands.Count, en.FindAll($"[data-testid^=grades-legend-{Course}-band-]").Count);
        await DisposeComponentsAsync();

        var ar = await RenderAsync<MyGradesPage>("ar");
        ar.WaitForElement($"[data-testid=grades-legend-{Course}]");
        Assert.Contains(scale.ScaleNameAr, ar.Find($"[data-testid=grades-legend-{Course}] summary").TextContent);
    }

    [Fact]
    public async Task There_is_no_provisional_marker_for_a_student()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId);
        var page = await RenderAsync<MyGradesPage>();
        page.WaitForElement("[data-testid=grades-courses]");

        Assert.DoesNotContain("provisional", page.Markup, StringComparison.OrdinalIgnoreCase); // L23: admins only
    }

    [Fact]
    public async Task Guardian_page_shows_the_child_name_and_asks_for_that_child()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId);
        var child = FakeGradebookApi.StudentIds[1];

        var page = await RenderAsync<ChildGradesPage>("en", p => p.Add(x => x.StudentId, child));
        page.WaitForElement("[data-testid=grades-student]");

        Assert.Equal((await _api.GetStudentGradesAsync(child)).StudentNameEn, page.Find("[data-testid=grades-student]").TextContent.Trim());
    }

    [Fact]
    public async Task A_child_who_is_not_theirs_shows_the_servers_answer_and_no_grades()
    {
        var page = await RenderAsync<ChildGradesPage>("en", p => p.Add(x => x.StudentId, Guid.NewGuid())); // unknown to the fake = another family's child (404)

        page.WaitForElement("[data-testid=api-error]");
        Assert.Empty(page.FindAll("[data-testid=grades-courses]"));
    }

    [Fact]
    public async Task The_pages_need_their_permissions()
    {
        var student = await RenderAsync<MyGradesPage>("en", null, Permissions.Grades.ViewOwn);
        Assert.NotNull(student.Find("[data-testid=grades-title]"));
        await DisposeComponentsAsync();

        await GrantAsync(Permissions.Staff.View); // a staff member without Grades.ViewOwn
        RenderProviders();
        var staff = Render<MyGradesPage>();
        staff.WaitForAssertion(() => Assert.Empty(staff.FindAll("[data-testid=grades-title]")));
        await DisposeComponentsAsync();

        await GrantAsync(Permissions.Grades.ViewOwn); // a student cannot open a child's grades
        RenderProviders();
        var notGuardian = Render<ChildGradesPage>(p => p.Add(x => x.StudentId, FakeGradebookApi.StudentIds[1]));
        notGuardian.WaitForAssertion(() => Assert.Empty(notGuardian.FindAll("[data-testid=grades-title]")));
    }

    [Fact]
    public void Notification_opens_the_grades_page()
    {
        // the server's Grades.Published notification carries the deep link /me/grades (guardians: the child's page); both routes exist
        Assert.Equal("/me/grades", typeof(MyGradesPage).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("/me/children/{StudentId:guid}/grades", typeof(ChildGradesPage).GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public async Task Excused_and_ungraded_items_say_so_in_words()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId, FakeGradebookApi.ManualItemId);
        _api.ShapeStudentGrades = data => data with
        {
            Courses = [data.Courses[0] with { Items = [data.Courses[0].Items[0] with { Excused = true, Points = null }, data.Courses[0].Items[1] with { Points = null, Excused = false }] }],
        };
        var page = await RenderAsync<MyGradesPage>();
        page.WaitForElement("[data-testid=grades-courses]");

        Assert.Contains("Excused", page.Markup);
        Assert.Contains("Not graded", page.Markup);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        await PublishAsync(FakeGradebookApi.AutoItemId);
        var page = await RenderAsync<MyGradesPage>("ar");
        page.WaitForElement("[data-testid=grades-courses]");

        Assert.Equal("الدرجات", page.Find("[data-testid=grades-title]").TextContent.Trim());
        Assert.Contains("بناءً على الدرجات المنشورة", page.Find($"[data-testid=grades-basis-{Course}]").TextContent);
        Assert.Contains("ltr", page.Find($"[data-testid=grades-items-{Course}] span[dir]").GetAttribute("dir"));
    }
}
