using System.Globalization;
using Bunit;
using Lms.Client.Pages.Blueprints;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Blueprints;

/// <summary>The course blueprint page in the Stitch layout (#568): overview column, section cards with an icon and a lesson count, numbered lessons.</summary>
public sealed class BlueprintStitchLayoutTests : BlueprintsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static string[] Viewer => [Permissions.Courses.View, Permissions.Blueprints.View];

    private async Task<IRenderedComponent<BlueprintPage>> OpenAsync(string culture = "en")
    {
        Blueprints.SeedPublished(MathId);
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Viewer);
        RenderProviders();
        var page = Render<BlueprintPage>(p => p.Add(x => x.Id, MathId));
        page.WaitForElement("[data-testid=blueprint-readonly]", Wait);
        return page;
    }

    [Fact]
    public async Task The_page_has_a_main_column_and_an_overview_column()
    {
        var page = await OpenAsync();

        var layout = page.Find("[data-testid=blueprint-layout]");
        Assert.NotNull(layout.QuerySelector("[data-testid=blueprint-main]"));
        Assert.NotNull(layout.QuerySelector("[data-testid=blueprint-overview]"));
        Assert.Equal("Blueprint overview", page.Find("[data-testid=blueprint-overview] h2").TextContent.Trim());
    }

    [Fact]
    public async Task The_overview_counts_sections_and_lessons_and_shows_the_periods_per_week()
    {
        var page = await OpenAsync();
        var sections = page.FindAll("[data-testid=blueprint-readonly] > section.lms-section").Count;
        var lessons = page.FindAll("[data-testid^=lesson-]").Count;

        Assert.Equal(sections.ToString(), page.Find("[data-testid=blueprint-stat-sections-value]").TextContent.Trim());
        Assert.Equal(lessons.ToString(), page.Find("[data-testid=blueprint-stat-lessons-value]").TextContent.Trim());
        Assert.NotEmpty(page.Find("[data-testid=blueprint-stat-periods-value]").TextContent.Trim());
        Assert.NotEmpty(page.Find("[data-testid=blueprint-stat-version-value]").TextContent.Trim());
    }

    [Fact]
    public async Task Each_section_card_has_an_icon_tile_and_its_lesson_count()
    {
        var page = await OpenAsync();

        var card = page.Find("[data-testid=section-0]");

        Assert.NotNull(card.QuerySelector(".lms-section-icon"));
        Assert.Equal("true", card.QuerySelector(".lms-section-icon")!.GetAttribute("aria-hidden"));
        Assert.Contains("Lessons:", card.QuerySelector("[data-testid=section-count-0]")!.TextContent);
    }

    [Fact]
    public async Task Lessons_show_their_number_in_a_badge()
    {
        var page = await OpenAsync();

        var badge = page.Find("[data-testid=lesson-0-0] .lms-lesson-number");

        Assert.Equal("1.1", badge.TextContent.Trim());
        Assert.Equal("ltr", badge.GetAttribute("dir"));
    }

    [Fact]
    public async Task The_version_history_card_has_an_icon_and_a_subtitle()
    {
        var page = await OpenAsync();

        var card = page.Find("[data-testid=blueprint-history]");

        Assert.NotNull(card.QuerySelector(".lms-section-icon"));
        Assert.Contains("Complete audit trail", card.TextContent);
        Assert.NotEmpty(card.QuerySelectorAll("table thead th"));
    }

    [Fact]
    public async Task The_page_renders_in_arabic_with_no_missing_keys()
    {
        var page = await OpenAsync("ar");

        Assert.Contains("نظرة عامة على المخطط", page.Find("[data-testid=blueprint-overview]").TextContent);
        Assert.DoesNotContain("Blueprint_", page.Markup);
    }
}
