using System.Globalization;
using Bunit;
using Lms.Client.Pages.Courses;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Courses;

/// <summary>My courses in the Stitch layout (#570): KPI tiles, filter toolbar with a grid / list toggle, cards, the assign card.</summary>
public sealed class MyCoursesLayoutTests : CoursesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    public MyCoursesLayoutTests() => Culture.Current = new CultureInfo("en");

    private async Task<IRenderedComponent<MyCoursesPage>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.Courses.View] : permissions);
        RenderProviders();
        var page = Render<MyCoursesPage>();
        page.WaitForElement("[data-testid=my-courses-stats]", Wait);
        return page;
    }

    private CourseSummaryDto Math => Courses.All.First(c => c.Summary.Code == "MATH101").Summary;

    [Fact]
    public async Task The_stat_tiles_count_what_the_api_gives()
    {
        var page = await OpenAsync();

        Assert.Equal("1", page.Find("[data-testid=my-courses-stat-supervised-value]").TextContent.Trim());
        Assert.Equal(Math.AllocationCount.ToString(), page.Find("[data-testid=my-courses-stat-allocations-value]").TextContent.Trim());
        Assert.Equal(Math.HasPublishedBlueprint ? "1" : "0", page.Find("[data-testid=my-courses-stat-published-value]").TextContent.Trim());
        Assert.Equal(Math.HasPublishedBlueprint ? "0" : "1", page.Find("[data-testid=my-courses-stat-pending-value]").TextContent.Trim());
        Assert.Equal(4, page.FindAll(".lms-stat").Count);
        Assert.All(page.FindAll(".lms-stat-icon"), i => Assert.Equal("true", i.GetAttribute("aria-hidden")));
    }

    [Fact]
    public async Task A_course_card_shows_its_code_unit_blueprint_state_and_an_open_button()
    {
        var page = await OpenAsync();

        var card = page.Find($"[data-testid=my-course-{Math.Id}]");

        Assert.Contains("MATH101", card.TextContent);
        Assert.NotNull(card.QuerySelector(".lms-kind-tile"));
        Assert.NotNull(card.QuerySelector("h2.lms-course-name"));
        Assert.Contains("Periods per week", card.TextContent);
        Assert.Contains($"courses/{Math.Id}", card.QuerySelector($"[data-testid=my-course-open-{Math.Id}]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task The_search_filters_by_name_or_code_and_says_when_nothing_matches()
    {
        var page = await OpenAsync();

        page.Find("input[data-testid=my-courses-search]").Input("zzz-nothing");

        page.WaitForElement("[data-testid=my-courses-no-match]", Wait);
        Assert.Empty(page.FindAll($"[data-testid=my-course-{Math.Id}]"));

        page.Find("input[data-testid=my-courses-search]").Input("math");

        page.WaitForElement($"[data-testid=my-course-{Math.Id}]", Wait);
        Assert.Empty(page.FindAll("[data-testid=my-courses-no-match]"));
    }

    [Fact]
    public async Task The_blueprint_filter_hides_courses_without_the_chosen_state()
    {
        var page = await OpenAsync();
        var select = page.FindComponents<MudSelect<string>>().Single(s => s.Markup.Contains("my-courses-blueprint-filter"));
        var hide = Math.HasPublishedBlueprint ? "none" : "published";

        await page.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(hide));

        page.WaitForElement("[data-testid=my-courses-no-match]", Wait);
    }

    [Fact]
    public async Task The_list_view_shows_a_labelled_table_with_the_same_open_links()
    {
        var page = await OpenAsync();
        Assert.NotEmpty(page.FindAll("[data-testid=my-courses-grid]"));   // the grid is the default

        page.Find("[data-testid=my-courses-view-list]").Click();

        var table = page.WaitForElement("[data-testid=my-courses-table]", Wait);
        Assert.Empty(page.FindAll("[data-testid=my-courses-grid]"));
        Assert.All(table.QuerySelectorAll("tbody td:not(.lms-cell-actions)"), td => Assert.False(string.IsNullOrEmpty(td.GetAttribute("data-label"))));
        Assert.Contains($"courses/{Math.Id}", table.QuerySelector($"[data-testid=my-course-open-{Math.Id}]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task The_view_toggle_and_its_buttons_have_names()
    {
        var page = await OpenAsync();

        Assert.Equal("View", page.Find("[data-testid=my-courses-view]").GetAttribute("aria-label"));
        Assert.Equal("Grid view", page.Find("[data-testid=my-courses-view-grid]").GetAttribute("aria-label"));
        Assert.Equal("List view", page.Find("[data-testid=my-courses-view-list]").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task The_assign_card_offers_templates_only_to_someone_who_can_read_them()
    {
        var without = await OpenAsync();
        Assert.NotEmpty(without.FindAll("[data-testid=my-courses-assign]"));
        Assert.Empty(without.FindAll("[data-testid=my-courses-assign-link]"));
    }

    [Fact]
    public async Task The_assign_card_links_to_templates_with_Templates_View()
    {
        var page = await OpenAsync(Permissions.Courses.View, Permissions.Templates.View);

        Assert.Equal("templates", page.Find("[data-testid=my-courses-assign-link]").GetAttribute("href"));
    }

    [Fact]
    public async Task The_page_renders_in_arabic_with_localized_labels()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await OpenAsync();

        Assert.Contains("مقررات تشرف عليها", page.Find("[data-testid=my-courses-stat-supervised]").TextContent);
        Assert.Contains("إسناد مقرر أو ربطه", page.Find("[data-testid=my-courses-assign]").TextContent);
        Assert.DoesNotContain("MyCourses_", page.Markup);   // a missing key would show its name
    }

    [Fact]
    public async Task The_filtered_result_count_is_announced_in_a_polite_live_region()
    {
        var page = await OpenAsync();
        var region = page.Find("[data-testid=my-courses-result-count]");

        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Equal("1 course", region.TextContent.Trim());

        page.Find("[data-testid=my-courses-search]").Input("no-such-course");
        Assert.Equal("0 courses", page.Find("[data-testid=my-courses-result-count]").TextContent.Trim());
    }

    [Fact]
    public async Task The_result_count_uses_the_arabic_plural_forms()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await OpenAsync();

        Assert.Equal("دورة واحدة", page.Find("[data-testid=my-courses-result-count]").TextContent.Trim());
        page.Find("[data-testid=my-courses-search]").Input("no-such-course");
        Assert.Equal("0 دورة", page.Find("[data-testid=my-courses-result-count]").TextContent.Trim());
    }
}
