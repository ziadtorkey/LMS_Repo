using Bunit;
using Lms.Client.Components.Ui;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Lms.Client.Tests.Components;

public sealed class UiComponentsTests : ClientTestContext
{
    [Fact]
    public void PageHeader_renders_the_title_as_the_page_h1_with_subtitle_and_actions()
    {
        var cut = Render<PageHeader>(p => p
            .Add(x => x.Title, "Staff")
            .Add(x => x.Subtitle, "People and roles")
            .Add(x => x.TitleTestId, "staff-title")
            .Add(x => x.Actions, (RenderFragment)(b => b.AddMarkupContent(0, "<button id=\"go\">Go</button>"))));

        var title = cut.Find("[data-testid=staff-title]");
        Assert.Equal("H1", title.TagName);
        Assert.Equal("Staff", title.TextContent.Trim());
        Assert.Contains("People and roles", cut.Markup);
        Assert.NotNull(cut.Find(".lms-page-header-actions #go"));
    }

    [Fact]
    public void PageHeader_without_subtitle_or_actions_renders_neither()
    {
        var cut = Render<PageHeader>(p => p.Add(x => x.Title, "Staff"));

        Assert.Empty(cut.FindAll(".lms-page-subtitle"));
        Assert.Empty(cut.FindAll(".lms-page-header-actions"));
    }

    [Fact]
    public void SectionCard_shows_an_icon_tile_before_the_heading_that_assistive_technology_skips()
    {
        var cut = Render<SectionCard>(p => p.Add(x => x.Title, "Roles").Add(x => x.Icon, MudBlazor.Icons.Material.Rounded.Badge));

        var tile = cut.Find(".lms-section-icon");
        Assert.Equal("true", tile.GetAttribute("aria-hidden"));
        Assert.Equal("Roles", cut.Find("h2").TextContent.Trim()); // the heading stays the label of the section
        Assert.Empty(Render<SectionCard>(p => p.Add(x => x.Title, "Roles")).FindAll(".lms-section-icon"));
    }

    [Fact]
    public void ListCountStat_shows_the_server_total_or_a_dash_before_it_is_known()
    {
        var known = Render<ListCountStat>(p => p.Add(x => x.Label, "Staff members").Add(x => x.Value, 42).Add(x => x.Icon, MudBlazor.Icons.Material.Rounded.Badge).Add(x => x.TestId, "stat"));
        Assert.Equal("42", known.Find("[data-testid=stat] .lms-stat-value").TextContent.Trim());
        Assert.Contains("Staff members", known.Find("[data-testid=stat] .lms-stat-label").TextContent);

        var unknown = Render<ListCountStat>(p => p.Add(x => x.Label, "Staff members").Add(x => x.TestId, "stat"));
        Assert.Equal("–", unknown.Find("[data-testid=stat] .lms-stat-value").TextContent.Trim());
    }

    [Fact]
    public void SectionCard_labels_the_section_with_its_heading()
    {
        var cut = Render<SectionCard>(p => p.Add(x => x.Title, "Roles"));

        var heading = cut.Find("h2");
        Assert.Equal("Roles", heading.TextContent.Trim());
        Assert.Equal(heading.Id, cut.Find("section").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void EmptyState_is_a_status_message_with_title_and_hint()
    {
        var cut = Render<EmptyState>(p => p.Add(x => x.Title, "No staff").Add(x => x.Text, "Change the filters.").Add(x => x.TestId, "empty"));

        var root = cut.Find("[data-testid=empty]");
        Assert.Equal("status", root.GetAttribute("role"));
        Assert.Contains("No staff", root.TextContent);
        Assert.Contains("Change the filters.", root.TextContent);
    }

    [Fact]
    public void LoadingState_announces_loading_to_screen_readers()
    {
        var cut = Render<LoadingState>(p => p.Add(x => x.TestId, "loading"));

        Assert.Equal("polite", cut.Find("[data-testid=loading]").GetAttribute("aria-live"));
        Assert.Contains("جارٍ التحميل", cut.Find(".lms-visually-hidden").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0500000000", "0500000000")]
    [InlineData("", "—")]
    [InlineData(null, "—")]
    public void DetailItem_shows_the_value_or_a_dash(string? value, string expected)
    {
        var cut = Render<DetailItem>(p => p.Add(x => x.Label, "Mobile").Add(x => x.Value, value).Add(x => x.Ltr, true));

        Assert.Equal("Mobile", cut.Find("dt").TextContent.Trim());
        Assert.Equal(expected, cut.Find("dd").TextContent.Trim());
    }

    [Fact]
    public void DetailItem_keeps_ltr_values_left_to_right()
    {
        var cut = Render<DetailItem>(p => p.Add(x => x.Label, "Email").Add(x => x.Value, "sara@x.test").Add(x => x.Ltr, true));

        Assert.Equal("ltr", cut.Find("dd span").GetAttribute("dir"));
    }
}
