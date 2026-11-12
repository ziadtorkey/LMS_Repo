using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Services.Api;
using Lms.Client.Pages.Classes;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;

namespace Lms.Client.Tests.Pages.Classes;

public sealed class LessonPlanPageTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private async Task<IRenderedComponent<LessonPlanPage>> OpenAsync(Guid? id = null, string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.LessonPlans.View);
        RenderProviders();
        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, id ?? FakeLessonPlansApi.ArabicClassCourseId));
        page.WaitForElement("[data-testid=plan-header]", Wait);
        return page;
    }

    [Fact]
    public async Task Staff_see_sections_lessons_and_greyed_hidden_ones()
    {
        var page = await OpenAsync();

        Assert.Equal(3, page.FindAll("[data-testid^=plan-section-]").Count);
        Assert.Equal(5, page.FindAll("[data-testid^=plan-lesson-]").Count);
        var hidden = page.FindAll("[data-testid^=plan-lesson-].lms-plan-hidden");
        Assert.Equal(2, hidden.Count);
        Assert.Contains("Hidden", hidden[0].TextContent);
        Assert.Contains("Retired chapter", page.Find("[data-testid=plan-section-00000000-0000-0000-0000-0000000000a3]").TextContent);
    }

    [Fact]
    public async Task Student_tree_has_no_hidden_lessons()
    {
        Plans.HideHiddenItems = true;
        var page = await OpenAsync();

        Assert.Equal(2, page.FindAll("[data-testid^=plan-section-]").Count);
        Assert.Equal(3, page.FindAll("[data-testid^=plan-lesson-]").Count);
        Assert.Empty(page.FindAll(".lms-plan-hidden"));
        Assert.DoesNotContain("Retired", page.Markup);
    }

    [Fact]
    public async Task Header_shows_version_and_synced_time_and_section_label()
    {
        var page = await OpenAsync();

        var header = page.Find("[data-testid=plan-header]").TextContent;
        Assert.Contains("Arabic", header);
        Assert.Contains("Grade 1 A", header);
        Assert.Contains("Version 3", header);
        Assert.Contains("Synced", header);
        // SectionLabel "Chapter" is shown in front of each section title.
        Assert.Contains("Chapter 1", page.Find("[data-testid=plan-section-00000000-0000-0000-0000-0000000000a1]").TextContent);
    }

    [Fact]
    public async Task Plan_without_a_published_version_says_so()
    {
        var page = await OpenAsync(FakeLessonPlansApi.EmptyClassCourseId);

        Assert.NotEmpty(page.FindAll("[data-testid=plan-empty]"));
        Assert.Contains("not received a plan", page.Find("[data-testid=plan-empty]").TextContent);
    }

    [Fact]
    public async Task Modules_slot_is_reserved_for_a_later_release()
    {
        var page = await OpenAsync();

        Assert.Contains("later release", page.Find("[data-testid=plan-modules-slot]").TextContent);
    }

    [Fact]
    public async Task Planned_dates_render_in_culture()
    {
        var page = await OpenAsync();

        var lesson = page.Find("[data-testid=plan-lesson-00000000-0000-0000-0000-0000000000b1]").TextContent;
        Assert.Contains(new DateOnly(2026, 9, 6).ToString("d", new CultureInfo("en")), lesson);
        var undated = page.Find("[data-testid=plan-lesson-00000000-0000-0000-0000-0000000000b2]").TextContent;
        Assert.DoesNotContain("2026", undated);
    }

    [Fact]
    public async Task Hijri_date_is_added_when_the_setting_is_on()
    {
        ShowHijri();
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View);
        PlaceContextAtUnit();
        RenderProviders();
        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));

        var lesson = page.WaitForElement("[data-testid=plan-lesson-00000000-0000-0000-0000-0000000000b1]", Wait).TextContent;

        var hijri = Hijri(new DateOnly(2026, 9, 6));
        Assert.Contains(hijri, lesson);
    }

    [Fact]
    public async Task Hijri_date_is_absent_when_the_setting_is_off()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View);
        PlaceContextAtUnit();
        RenderProviders();
        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));

        var lesson = page.WaitForElement("[data-testid=plan-lesson-00000000-0000-0000-0000-0000000000b1]", Wait).TextContent;

        Assert.Empty(page.FindAll("[data-testid=plan-hijri]"));
        Assert.DoesNotContain(Hijri(new DateOnly(2026, 9, 6)), lesson);
    }

    [Fact]
    public async Task Unknown_class_course_shows_not_found()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View);
        RenderProviders();

        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, Guid.NewGuid()));

        page.WaitForElement("[data-testid=plan-error]", Wait);
    }

    [Fact]
    public async Task Page_is_forbidden_without_the_permission()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);
        RenderProviders();

        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));

        page.WaitForElement("[data-testid=plan-forbidden]", Wait);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.LessonPlans.View);
        PlaceContextAtUnit(); // a context the user does not hold is cleared by the layout

        var layout = Render<MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b =>
            {
                b.OpenComponent<LessonPlanPage>(0);
                b.AddComponentParameter(1, "Id", FakeLessonPlansApi.ArabicClassCourseId);
                b.CloseComponent();
            })));

        layout.WaitForElement("[data-testid=plan-header]", Wait);

        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("القراءة", layout.Markup);
        Assert.Contains("حروف الهجاء", layout.Markup);
    }

    private static string Hijri(DateOnly date)
    {
        var calendar = new UmAlQuraCalendar();
        var at = date.ToDateTime(TimeOnly.MinValue);
        return $"{calendar.GetDayOfMonth(at):00}/{calendar.GetMonth(at):00}/{calendar.GetYear(at)}";
    }
}
