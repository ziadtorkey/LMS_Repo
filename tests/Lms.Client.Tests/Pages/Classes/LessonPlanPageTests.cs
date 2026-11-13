using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Services.Api;
using Lms.Client.Pages.Classes;
using Lms.Client.Tests.Support;
using Lms.Client.Services;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

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

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Planned_dates_are_iso_and_left_to_right_in_both_languages(string culture)
    {
        var page = await OpenAsync(culture: culture);

        var lesson = page.Find("[data-testid=plan-lesson-00000000-0000-0000-0000-0000000000b1]");
        var date = lesson.QuerySelector("span[dir=ltr]:not([data-testid])");
        Assert.Contains("2026-09-06", lesson.TextContent);
        Assert.NotNull(lesson.QuerySelectorAll("span[dir=ltr]").FirstOrDefault(x => x.TextContent.Trim() == "2026-09-06"));
        var undated = page.Find("[data-testid=plan-lesson-00000000-0000-0000-0000-0000000000b2]").TextContent;
        Assert.DoesNotContain("2026", undated);
        Assert.NotNull(date);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Synced_time_is_iso_and_left_to_right(string culture)
    {
        var page = await OpenAsync(culture: culture);

        var expected = new DateTime(2026, 9, 20, 8, 30, 0, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var span = page.Find("[data-testid=plan-header]").QuerySelectorAll("span[dir=ltr]").FirstOrDefault(x => x.TextContent.Trim() == expected);
        Assert.NotNull(span);
    }

    [Fact]
    public async Task Lesson_numbers_are_isolated_so_arabic_does_not_move_the_dot()
    {
        var page = await OpenAsync(culture: "ar");

        var lesson = page.Find("[data-testid=plan-lesson-00000000-0000-0000-0000-0000000000b1]");
        Assert.Contains("<span dir=\"ltr\">1.</span>", lesson.InnerHtml);
    }

    [Fact]
    public async Task The_page_has_one_h1_with_the_course_and_class_as_subtitle()
    {
        var page = await OpenAsync();

        var h1 = page.FindAll("h1");
        Assert.Single(h1);
        Assert.Contains("Lesson plan", h1[0].TextContent);
        var header = page.Find("[data-testid=page-header]");
        Assert.Contains("Arabic", header.TextContent);
        Assert.Contains("Grade 1 A", header.TextContent);
    }

    [Fact]
    public async Task The_version_chip_is_text_variant_not_filled()
    {
        var page = await OpenAsync();

        var chip = page.FindAll(".mud-chip").First(x => x.TextContent.Contains("Version 3"));
        Assert.Contains("mud-chip-text", chip.ClassName);
        Assert.DoesNotContain("mud-chip-filled", chip.ClassName);
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
    public async Task Teacher_without_Settings_View_still_sees_the_plan_with_the_default_calendar()
    {
        // A Teacher holds LessonPlans.View but not Settings.View: the effective-settings read answers 403.
        Services.AddSingleton<IConfigApi>(new ForbiddenSettingsApi());
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.LessonPlans.View);   // no Settings.View
        PlaceContextAtUnit();                              // so the page does read the unit's settings
        RenderProviders();
        var page = Render<LessonPlanPage>(p => p.Add(x => x.Id, FakeLessonPlansApi.ArabicClassCourseId));
        page.WaitForElement("[data-testid=plan-header]", Wait);

        Assert.NotEmpty(page.FindAll("[data-testid=plan-header]"));
        Assert.Empty(page.FindAll("[data-testid=plan-forbidden]"));
        Assert.DoesNotContain("forbidden", Services.GetRequiredService<NavigationManager>().Uri);   // no redirect to the Forbidden page
        Assert.Empty(page.FindAll("[data-testid=plan-hijri]"));   // no setting readable: the default (Gregorian only)
        Assert.NotEmpty(page.FindAll("[data-testid^=plan-lesson-]"));
    }

    private sealed class ForbiddenSettingsApi : IConfigApi
    {
        public Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsAsync(Guid unitId, CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<EffectiveSettingDto>>(new ApiForbiddenException());
        public Task<IReadOnlyList<PolicyDefinitionDto>> GetPolicyCatalogAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EffectivePolicyDto>> GetPoliciesAsync(Guid unitId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnablePolicyAsync(Guid unitId, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DisablePolicyAsync(Guid unitId, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SettingDefinition>> GetSettingCatalogAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetSettingAsync(Guid unitId, string name, SetSettingRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveSettingAsync(Guid unitId, string name, CancellationToken ct = default) => throw new NotSupportedException();
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
