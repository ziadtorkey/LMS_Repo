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
    /// <summary>Un-skip when the course allocation API (#215) is on main.</summary>
    private const string NeedsAllocations = "Needs the course allocation API (#215) on main";

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_creates_course_and_a_duplicate_code_is_refused(string culture)
    {
        var names = M3Names.New();
        var code = names.CourseCode("MATH");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();

        var courses = new CoursesPage(Page);
        await courses.OpenAsync();
        await courses.CreateAsync(new CoursesPage.NewCourse(
            code, names.CourseNameAr("رياضيات"), names.CourseNameEn("Math"),
            OwnerUnitName: Pick(culture, "مدرسة التجمع", "School Altagamo3"), WeeklyPeriods: 4));
        await courses.SearchAsync(names.Suffix);
        await Expect(courses.Row(code)).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // A second course with the same code is refused (codes are unique in the whole client, D38).
        await courses.FillAndSaveAsync(new CoursesPage.NewCourse(
            code, names.CourseNameAr("آخر"), names.CourseNameEn("Other"), Pick(culture, "مدرسة التجمع", "School Altagamo3")));
        await Expect(courses.DialogError).ToContainTextAsync(Resx.In(culture, "Error_course_code_taken"));
        await AssertDirectionAsync(culture);
    }

    [Theory(Skip = NeedsAllocations)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_allocates_a_new_course_below_the_school(string culture)
    {
        var names = M3Names.New();
        var code = names.CourseCode("SCI");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();

        var courses = new CoursesPage(Page);
        await courses.OpenAsync();
        await courses.CreateAsync(new CoursesPage.NewCourse(
            code, names.CourseNameAr("علوم"), names.CourseNameEn("Science"),
            OwnerUnitName: Pick(culture, "مدرسة التجمع", "School Altagamo3"), WeeklyPeriods: 3,
            AllocateTo: [Pick(culture, "رياض الأطفال", "KG")]));
        await courses.SearchAsync(names.Suffix);
        await Expect(courses.Row(code)).ToBeVisibleAsync(new() { Timeout = 20_000 });
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
