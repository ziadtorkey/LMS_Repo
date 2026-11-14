using System.Globalization;
using Bunit;
using Lms.Client.Pages.Classes;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Teaching;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Classes;

public sealed class ClassPageTests : ClassesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeClassCoursesApi _classCourses = new();
    private readonly FakeCoursesApi _courses = new();
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public ClassPageTests()
    {
        Services.AddSingleton<IClassCoursesApi>(_classCourses);
        Services.AddSingleton<ICoursesApi>(_courses);
    }

    private static ClassSummaryDto Class => FakeClasses.Grade1A;

    private async Task<IRenderedComponent<ClassPage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Classes.View] : permissions);
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<ClassPage>(p => p.Add(x => x.Id, Class.Id));
    }

    private IRenderedComponent<ClassCoursesTab> RenderTab(bool manage = true, ClassSummaryDto? cls = null)
    {
        Culture.Current = new CultureInfo("en");
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<ClassCoursesTab>(p => p.Add(t => t.Class, cls ?? Class).Add(t => t.CanManage, manage));
    }

    [Fact]
    public async Task The_header_is_the_one_h1_with_the_code_chip_and_stat_tiles_from_the_class_numbers()
    {
        var page = await RenderPageAsync();
        page.WaitForElement("[data-testid=class-stats]");

        Assert.Single(page.FindAll("h1"));
        Assert.Equal(Class.Code, page.Find("[data-testid=class-code]").TextContent.Trim());
        Assert.Contains(Class.StudentCount.ToString(), page.Find("[data-testid=class-count]").TextContent);
        Assert.Equal(Class.CourseCount.ToString(), page.Find("[data-testid=class-course-count]").TextContent.Trim());
        Assert.NotEmpty(page.Find("[data-testid=class-homeroom]").TextContent.Trim()); // a name, or a dash
    }

    [Fact]
    public async Task Page_shows_the_class_header_and_tabs_without_a_timetable()
    {
        var page = await RenderPageAsync(Permissions.Classes.View, Permissions.TeacherAssignments.View);

        Assert.Contains("Grade 1 A", page.WaitForElement("[data-testid=class-title]").TextContent);
        Assert.Contains("12 / 30", page.Find("[data-testid=class-count]").TextContent);
        var tabs = page.Find("[data-testid=class-tabs]").TextContent;
        Assert.Contains("Students", tabs);
        Assert.Contains("Courses", tabs);
        Assert.Contains("Teachers", tabs);
        Assert.DoesNotContain("Timetable", tabs); // arrives with the scheduling milestone
        Assert.EndsWith($"classes/{Class.Id}/students", page.Find("[data-testid=open-students]").GetAttribute("href"));
    }

    [Fact]
    public async Task Teachers_tab_needs_TeacherAssignments_View()
    {
        var without = await RenderPageAsync(Permissions.Classes.View);
        Assert.Contains("Courses", without.WaitForElement("[data-testid=class-tabs]").TextContent);
        Assert.DoesNotContain("Teachers", without.Find("[data-testid=class-tabs]").TextContent);

        await GrantAsync(Permissions.Classes.View, Permissions.TeacherAssignments.View); // providers are already rendered
        var with = Render<ClassPage>(p => p.Add(x => x.Id, Class.Id));
        Assert.Contains("Teachers", with.WaitForElement("[data-testid=class-tabs]").TextContent);
    }

    [Fact]
    public async Task Page_is_forbidden_without_Classes_View_and_reports_a_missing_class()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Staff.View);
        RenderProviders();
        var forbidden = Render<ClassPage>(p => p.Add(x => x.Id, Class.Id));
        forbidden.WaitForAssertion(() => Assert.Contains("You do not have permission", forbidden.Markup));

        await GrantAsync(Permissions.Classes.View);
        var missing = Render<ClassPage>(p => p.Add(x => x.Id, Guid.NewGuid()));
        Assert.Contains("This class was not found.", missing.WaitForElement("[data-testid=class-error]").TextContent);
    }

    [Fact]
    public void Tab_lists_the_class_courses_with_optional_and_enrolled()
    {
        var cut = RenderTab();

        cut.WaitForElement("[data-testid=class-courses-table]");
        Assert.Contains("Mathematics", cut.Markup);
        Assert.Contains("Science", cut.Markup);
        Assert.DoesNotContain("Arabic", cut.Markup);
        Assert.True(cut.Find("input[data-testid=class-course-optional-SCI101]").HasAttribute("checked"));
        Assert.False(cut.Find("input[data-testid=class-course-optional-MATH101]").HasAttribute("checked"));
    }

    [Theory]
    [InlineData("en", "Teacher One, Teacher Two")]
    [InlineData("ar", "معلم واحد، معلم اثنان")]
    public void Teacher_names_are_joined_with_the_cultures_separator(string culture, string expected)
    {
        var math = _classCourses.All.First(c => c.ClassId == Class.Id && c.CourseCode == "MATH101");
        var teachers = new List<ClassCourseTeacherDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "معلم واحد", "Teacher One", "Primary", new DateOnly(2026, 9, 1), null, true),
            new(Guid.NewGuid(), Guid.NewGuid(), "معلم اثنان", "Teacher Two", "Assistant", new DateOnly(2026, 9, 1), null, true),
        };
        _classCourses.ReplaceTeachers(math.Id, teachers);
        RenderProviders();
        Culture.Current = new CultureInfo(culture);
        var cut = Render<ClassCoursesTab>(p => p.Add(t => t.Class, Class).Add(t => t.CanManage, false));

        cut.WaitForElement("[data-testid=class-courses-table]");

        Assert.Contains(expected, cut.Markup);
    }

    [Fact]
    public async Task Add_dialog_lists_available_courses_only()
    {
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=class-courses-add]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=add-courses-list]");

        // Available at the unit = active courses of the catalog (ARB101 is inherited but available); MATH101 is already in the class
        // and SCI101 is inactive.
        Assert.NotEmpty(Dialogs.FindAll("input[data-testid=add-course-ARB101]"));
        Assert.Empty(Dialogs.FindAll("input[data-testid=add-course-MATH101]"));
        Assert.Empty(Dialogs.FindAll("input[data-testid=add-course-SCI101]"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Add_courses_sends_the_ids_and_the_optional_flag()
    {
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=class-courses-add]").ClickAsync(new());
        await (await Dialogs.WaitForElementAsync("input[data-testid=add-course-ARB101]")).ChangeAsync(new() { Value = true });
        await Dialogs.Find("input[data-testid=add-optional]").ChangeAsync(new() { Value = true });

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_classCourses.Adds), Wait);
        Assert.Equal(Class.Id, _classCourses.Adds[0].ClassId);
        Assert.Equal([FakeCourseIds.For("ARB101")], _classCourses.Adds[0].Request.CourseIds);
        Assert.True(_classCourses.Adds[0].Request.IsOptional);
        await cut.WaitForAssertionAsync(() => Assert.Contains("Arabic", cut.Markup), Wait);
    }

    [Fact]
    public async Task Optional_toggle_updates_the_class_course()
    {
        var cut = RenderTab();
        var math = _classCourses.All.First(c => c.ClassId == Class.Id && c.CourseCode == "MATH101");

        await cut.WaitForElement("input[data-testid=class-course-optional-MATH101]").ChangeAsync(new() { Value = true });

        await cut.WaitForAssertionAsync(() => Assert.Single(_classCourses.Updates), Wait);
        Assert.Equal((math.Id, true), (_classCourses.Updates[0].Id, _classCourses.Updates[0].Request.IsOptional));
    }

    [Fact]
    public async Task Removal_blocked_message()
    {
        _classCourses.Failure = new ApiProblemException(409, "class_course_in_use", null, null);
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=class-course-remove-MATH101]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await _snackbars.WaitForAssertionAsync(() => Assert.Contains("cannot be removed", _snackbars.Markup), Wait);
        Assert.Contains("Mathematics", cut.Markup); // still there
    }

    [Fact]
    public async Task Remove_confirmed_removes_the_course()
    {
        var cut = RenderTab();
        var math = _classCourses.All.First(c => c.ClassId == Class.Id && c.CourseCode == "MATH101");
        _ = cut.WaitForElement("[data-testid=class-course-remove-MATH101]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Equal([math.Id], _classCourses.Removed), Wait);
        await cut.WaitForAssertionAsync(() => Assert.DoesNotContain("Mathematics", cut.Markup), Wait);
    }

    [Fact]
    public void Actions_hidden_without_manage_and_in_a_closed_class()
    {
        var viewer = RenderTab(manage: false);
        viewer.WaitForElement("[data-testid=class-courses-table]");
        Assert.Empty(viewer.FindAll("[data-testid=class-courses-add]"));
        Assert.Empty(viewer.FindAll("[data-testid=class-course-remove-MATH101]"));

        var closed = Render<ClassCoursesTab>(p => p.Add(t => t.Class, Class with { IsReadOnly = true }).Add(t => t.CanManage, true));
        closed.WaitForElement("[data-testid=class-courses-table]");
        Assert.Empty(closed.FindAll("[data-testid=class-courses-add]"));
        Assert.Empty(closed.FindAll("[data-testid=class-course-remove-MATH101]"));
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Classes.View, Permissions.Classes.ManageCourses);
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<ClassPage>(c => c.Add(x => x.Id, Class.Id)));

        Assert.Contains("الصف الأول أ", rtl.WaitForElement("[data-testid=class-title]").TextContent);
        Assert.Contains("المقررات", rtl.Find("[data-testid=class-tabs]").TextContent);
        Assert.Equal("ltr", rtl.Find("[data-testid=class-code]").GetAttribute("dir"));
    }
}
