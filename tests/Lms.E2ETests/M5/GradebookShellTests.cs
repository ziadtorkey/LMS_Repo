using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M5;

/// <summary>
/// The parts of M5 demo 1 that need only what #630 (gradebook tab shell) and #641 (grading scales, contract #625) bring: the tab and its access
/// rule in the UI, and what provisioning put in the scales API. Skipped until those PRs are on main; the flows are written against their code.
/// </summary>
public sealed class GradebookShellTests(E2EFixture fixture) : M5TestBase(fixture)
{
    [Theory(Skip = NeedsShellAndScales)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_reaches_the_gradebook_from_the_plan_tab_and_a_student_is_refused(string culture)
    {
        var (world, _, names) = await StartWorldAsync(culture);
        await using var teacher = await OpenTeacherAsync(culture);
        await using var student = await OpenStudentAsync(world, names, culture);

        // Staff with Grades.View see the Gradebook tab next to the plan and land on the gradebook page.
        await teacher.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/plan");
        var staffPage = new GradebookPage(teacher.Page);
        await Expect(staffPage.Tab).ToBeVisibleAsync();
        await staffPage.OpenFromTabsAsync();
        Assert.EndsWith($"/class-courses/{world.ClassCourseId}/gradebook", new Uri(teacher.Page.Url).AbsolutePath);
        await AssertDirectionAsync(teacher.Page, culture);

        // A student has no Grades.View: no tab, and the page itself answers with the forbidden notice, never a grid or a title.
        var studentPage = new GradebookPage(student.Page);
        await studentPage.OpenAsync(world.ClassCourseId);
        await Expect(studentPage.Forbidden).ToBeVisibleAsync();
        await Expect(studentPage.Title).ToHaveCountAsync(0);
        await Expect(studentPage.Grid).ToHaveCountAsync(0);
        await AssertDirectionAsync(student.Page, culture);
    }

    [Fact(Skip = NeedsShellAndScales)]
    public async Task Provisioned_client_has_a_plain_default_scale_and_provisional_saudi_templates()
    {
        var (_, admin, _) = await StartWorldAsync("en");
        var root = await M5Kit.RootUnitAsync(admin);

        var scales = await M5Kit.ScalesAsync(admin, root);

        // L7: one plain default "Percentage" scale; the Saudi presets are templates, never the default, provisional until an admin confirms them.
        var plain = Assert.Single(scales, s => s.IsDefault);
        Assert.False(plain.IsTemplate);
        Assert.Equal("Percentage", plain.NameEn);
        Assert.Equal(1, plain.BandCount);

        var templates = scales.Where(s => s.IsTemplate).ToList();
        Assert.Equal(3, templates.Count); // school 5-band, university 5-point, university 4-point
        Assert.All(templates, t =>
        {
            Assert.True(t.Provisional);
            Assert.False(t.IsDefault);
            Assert.NotNull(t.SeedKey);
            Assert.True(t.BandCount > 0);
        });
    }
}
