using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M3;

/// <summary>
/// M3 exit demo 1 (plan Task 46, #254): courses, allocations, blueprints, classes, teachers and the lesson plan, in
/// Arabic (RTL) and English, on the seeded school client. Everything a scenario is not testing is set up through the
/// API (<see cref="ApiSetup"/>); only the feature named by the scenario is driven through the UI. Names are unique per
/// test (<see cref="M3Names"/>).
/// </summary>
public sealed class AcademicStructureTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>Un-skip as the pieces reach main: courses page (#264), course API (#214, #215), supervisors (#217, FE T30).</summary>
    private const string NeedsCourses = "Needs the courses page (#264) and the course and allocation API (#214, #215) on main";

    [Theory(Skip = NeedsCourses)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_creates_course_and_allocates_it_below_the_school(string culture)
    {
        var names = M3Names.New();
        var code = names.CourseCode("MATH");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();

        var courses = new CoursesPage(Page);
        await courses.OpenAsync();
        await courses.CreateAsync(new CoursesPage.NewCourse(
            code, names.CourseNameAr("رياضيات"), names.CourseNameEn("Math"),
            OwnerUnitName: Pick(culture, "مدرسة التجمع", "School Altagamo3"), WeeklyPeriods: 4,
            AllocateTo: [Pick(culture, "رياض الأطفال", "KG")]));
        await courses.SearchAsync(code);
        await Expect(courses.Row(code)).ToBeVisibleAsync();

        // A second course with the same code is refused (codes are unique in the whole client, D38).
        await courses.FillAndSaveAsync(new CoursesPage.NewCourse(
            code, names.CourseNameAr("آخر"), names.CourseNameEn("Other"), Pick(culture, "مدرسة التجمع", "School Altagamo3")));
        await Expect(courses.DialogError).ToContainTextAsync(Resx.In(culture, "Error_course_code_taken"));
        await AssertDirectionAsync(culture);
    }

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

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
}
