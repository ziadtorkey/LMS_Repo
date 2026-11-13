using System.Globalization;
using Bunit;
using Lms.Client.Pages.Forums;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;

namespace Lms.Client.Tests.Pages;

/// <summary>Which first tab a class course shows: Plan for staff, My course for enrolled students, both (Plan first) for both (#533).</summary>
public sealed class ClassCourseTabsTests : LessonPlansTestContext
{
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;

    private async Task<IRenderedComponent<ClassCourseTabs>> RenderTabsAsync(bool teaches, bool enrolled, params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        Plans.TeachesIds = teaches ? [Cc] : [];
        Plans.EnrolledIds = enrolled ? [Cc] : [];
        await GrantAsync(permissions);
        RenderProviders();
        var tabs = Render<ClassCourseTabs>(p => p.Add(x => x.ClassCourseId, Cc).Add(x => x.Active, "forum"));
        tabs.WaitForElement("[data-testid=class-course-tabs]");
        return tabs;
    }

    private static string[] Order(IRenderedComponent<ClassCourseTabs> tabs) =>
        tabs.FindAll("[data-testid^=tab-]").Select(t => t.GetAttribute("data-testid")!).ToArray();

    [Fact]
    public async Task A_teacher_sees_Plan_and_not_My_course()
    {
        var tabs = await RenderTabsAsync(teaches: true, enrolled: false, Permissions.Forums.View, Permissions.LessonPlans.View);

        Assert.Equal(["tab-plan", "tab-forum", "tab-announcements"], Order(tabs));
    }

    [Fact]
    public async Task An_enrolled_student_sees_My_course_and_not_Plan()
    {
        var tabs = await RenderTabsAsync(teaches: false, enrolled: true, Permissions.Forums.View, Permissions.LessonPlans.View, Permissions.Modules.View);

        Assert.Equal(["tab-learn", "tab-forum", "tab-announcements"], Order(tabs));
        Assert.Contains($"class-courses/{Cc}/learn", tabs.Find("[data-testid=tab-learn]").GetAttribute("href"));
    }

    [Fact]
    public async Task Someone_who_teaches_and_is_enrolled_sees_both_with_Plan_first()
    {
        var tabs = await RenderTabsAsync(teaches: true, enrolled: true, Permissions.Forums.View);

        Assert.Equal(["tab-plan", "tab-learn", "tab-forum", "tab-announcements"], Order(tabs));
    }

    [Fact]
    public async Task Modules_Manage_shows_Plan_without_a_teacher_relationship()
    {
        var tabs = await RenderTabsAsync(teaches: false, enrolled: false, Permissions.Modules.Manage, Permissions.Forums.View);

        Assert.Equal(["tab-plan", "tab-forum", "tab-announcements"], Order(tabs));
    }

    [Fact]
    public async Task A_reader_of_plans_without_a_relationship_still_gets_Plan()
    {
        var tabs = await RenderTabsAsync(teaches: false, enrolled: false, Permissions.LessonPlans.View);

        Assert.Equal(["tab-plan"], Order(tabs));
    }

    [Fact]
    public async Task The_current_tab_is_marked_for_assistive_technology()
    {
        Culture.Current = new CultureInfo("en");
        Plans.TeachesIds = []; Plans.EnrolledIds = [Cc];
        await GrantAsync(Permissions.Forums.View);
        RenderProviders();
        var tabs = Render<ClassCourseTabs>(p => p.Add(x => x.ClassCourseId, Cc).Add(x => x.Active, "learn"));
        tabs.WaitForElement("[data-testid=tab-learn]");

        Assert.Equal("page", tabs.Find("[data-testid=tab-learn]").GetAttribute("aria-current"));
        Assert.Null(tabs.Find("[data-testid=tab-forum]").GetAttribute("aria-current"));
    }
}
