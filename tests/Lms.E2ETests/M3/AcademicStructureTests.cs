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
        await Expect(courses.CodeError).ToContainTextAsync(Resx.In(culture, "Error_course_code_taken"));
        // The refused save created nothing: reopen the list (the dialog is still open over it), search again, and exactly one row carries the code.
        await courses.OpenAsync();
        await courses.SearchAsync(names.Suffix);
        await Expect(courses.Row(code)).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(courses.Row(code)).ToHaveCountAsync(1);
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_allocates_a_new_course_below_the_school(string culture)
    {
        var names = M3Names.New();
        var code = names.CourseCode("SCI");
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();

        var plainCode = names.CourseCode("PLAIN");
        var courses = new CoursesPage(Page);
        await courses.OpenAsync();
        await courses.CreateAsync(new CoursesPage.NewCourse(
            code, names.CourseNameAr("علوم"), names.CourseNameEn("Science"),
            OwnerUnitName: Pick(culture, "مدرسة التجمع", "School Altagamo3"), WeeklyPeriods: 3,
            AllocateTo: [Pick(culture, "رياض الأطفال", "KG")]));
        await courses.CreateAsync(new CoursesPage.NewCourse(
            plainCode, names.CourseNameAr("فنون"), names.CourseNameEn("Art"),
            OwnerUnitName: Pick(culture, "مدرسة التجمع", "School Altagamo3"), WeeklyPeriods: 1));
        await courses.SearchAsync(names.Suffix);
        await Expect(courses.Row(code)).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(courses.Row(plainCode)).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // The Allocations column counts the units the course is allocated to: one for the allocated course, none for the other.
        var header = Resx.In(culture, "Course_Allocations");
        Assert.Equal(1, await courses.AllocationCountAsync(code, header));
        Assert.Equal(0, await courses.AllocationCountAsync(plainCode, header));
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
