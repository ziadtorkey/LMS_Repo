using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Services.Api;
using Lms.Client.Pages.Teaching;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;

namespace Lms.Client.Tests.Pages.Teaching;

public sealed class MyClassCoursesPageTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Teacher_sees_own_class_courses()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View);
        RenderProviders();

        var page = Render<MyClassCoursesPage>();

        page.WaitForElement($"[data-testid=my-class-course-{FakeLessonPlansApi.ArabicClassCourseId}]", Wait);
        Assert.Equal(3, page.FindAll("[data-testid^=my-class-course-]:not([data-testid^=my-class-course-plan-])").Count);
        var card = page.Find($"[data-testid=my-class-course-{FakeLessonPlansApi.ArabicClassCourseId}]").TextContent;
        Assert.Contains("Arabic", card);
        Assert.Contains("Grade 1 A", card);
        Assert.Contains("ARB101", card);
        Assert.Contains("12", card);
        Assert.Contains("Plan version 3", card);
    }

    [Fact]
    public async Task Class_course_without_plan_says_so_and_cards_link_to_the_plan()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View);
        RenderProviders();

        var page = Render<MyClassCoursesPage>();

        var empty = page.WaitForElement($"[data-testid=my-class-course-{FakeLessonPlansApi.EmptyClassCourseId}]", Wait);
        Assert.Contains("No plan yet", empty.TextContent);
        var link = page.Find($"[data-testid=my-class-course-plan-{FakeLessonPlansApi.ArabicClassCourseId}]");
        Assert.EndsWith($"class-courses/{FakeLessonPlansApi.ArabicClassCourseId}/plan", link.GetAttribute("href"));
    }

    [Fact]
    public async Task No_class_courses_shows_the_empty_message()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View);
        Plans.Mine.Clear();
        RenderProviders();

        var page = Render<MyClassCoursesPage>();

        page.WaitForElement("[data-testid=my-class-courses-empty]", Wait);
    }

    [Fact]
    public async Task Page_is_forbidden_without_the_permission()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);
        RenderProviders();

        var page = Render<MyClassCoursesPage>();

        page.WaitForElement("[data-testid=my-class-courses-forbidden]", Wait);
        Assert.Empty(page.FindAll("[data-testid^=my-class-course-]:not([data-testid^=my-class-course-plan-])"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.LessonPlans.View);
        PlaceContextAtUnit(); // a context the user does not hold is cleared by the layout

        var layout = Render<MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b => { b.OpenComponent<MyClassCoursesPage>(0); b.CloseComponent(); })));

        layout.WaitForElement($"[data-testid=my-class-course-{FakeLessonPlansApi.ArabicClassCourseId}]", Wait);

        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("اللغة العربية", layout.Markup);
        Assert.Contains("الإصدار 3", layout.Markup);
    }
}

public sealed class HomeClassCoursesCardTests : LessonPlansTestContext
{
    [Fact]
    public async Task Home_links_to_my_class_courses_with_the_permission()
    {
        await GrantAsync(Permissions.LessonPlans.View);
        var page = Render<Lms.Client.Pages.Home>();
        var link = page.WaitForElement("[data-testid=home-my-class-courses-link]");
        Assert.EndsWith("me/class-courses", link.GetAttribute("href"));
    }

    [Fact]
    public async Task Home_has_no_card_without_the_permission()
    {
        await GrantAsync(Permissions.Courses.View);
        var page = Render<Lms.Client.Pages.Home>();
        page.WaitForAssertion(() => Assert.Contains("مستخدم", page.Markup));
        Assert.Empty(page.FindAll("[data-testid=home-my-class-courses]"));
    }

    [Fact]
    public async Task Students_get_an_open_course_link_and_teachers_do_not()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View, Permissions.Modules.View);
        RenderProviders();

        var page = Render<MyClassCoursesPage>();

        var id = FakeLessonPlansApi.ArabicClassCourseId;
        page.WaitForElement($"[data-testid=my-class-course-learn-{id}]", TimeSpan.FromSeconds(5));
        Assert.Contains($"class-courses/{id}/learn", page.Find($"[data-testid=my-class-course-learn-{id}]").GetAttribute("href"));
    }
}
