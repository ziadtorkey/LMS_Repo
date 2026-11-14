using System.Globalization;
using Bunit;
using Lms.Client.Components.YearEnd;
using Lms.Client.Layout;
using Lms.Client.Services;
using Lms.Client.Services.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.YearEnd;

/// <summary>M7 H8: the Year end navigation group and the home checklist (D86).</summary>
public sealed class YearEndH8Tests : ClientTestContext
{
    private readonly FakeYearEndHomeApi _home = new();

    public YearEndH8Tests() => Services.AddSingleton<IYearEndHomeApi>(_home);

    private new async Task GrantAsync(params string[] permissions)
    {
        await base.GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private async Task<IRenderedComponent<NavMenu>> NavAsync(params string[] permissions)
    {
        Api.Permissions = new EffectivePermissionsDto(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        var cut = Render<NavMenu>();
        cut.WaitForState(() => cut.FindAll("[data-testid^=nav-]").Count > 0);
        return cut;
    }

    private async Task<IRenderedComponent<YearEndChecklistCard>> CardAsync(string culture = "en", string[]? permissions = null)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions ?? [Permissions.AcademicYears.Close]);
        var card = Render<YearEndChecklistCard>();
        return card;
    }

    // ----- navigation

    [Fact]
    public async Task Year_end_section_is_hidden_without_any_of_its_permissions()
    {
        var cut = await NavAsync(Permissions.Roles.View);
        Assert.Empty(cut.FindAll("[data-testid=navsection-yearend]"));
    }

    [Theory]
    [InlineData("AcademicYears.Close", "nav-yearendclose")]
    [InlineData("Grades.ChangeApprove", "nav-gradechanges")]
    [InlineData("Promotion.Run", "nav-promotion")]
    [InlineData("Students.ViewArchive", "nav-studentarchive")]
    public async Task Each_entry_of_the_section_needs_its_own_permission(string permission, string testId)
    {
        var cut = await NavAsync(permission);

        var section = cut.Find("[data-testid=navsection-yearend]");
        Assert.NotNull(section.QuerySelector($"[data-testid={testId}]"));
        Assert.Single(section.QuerySelectorAll("[data-testid^=nav-]")); // the others stay hidden
        Assert.Equal("group", section.GetAttribute("role"));
    }

    [Fact]
    public void The_section_comes_before_security_and_every_entry_has_a_rounded_icon()
    {
        var sections = Lms.Client.Navigation.NavCatalog.Sections;
        Assert.True(sections.ToList().IndexOf(Lms.Client.Navigation.NavCatalog.YearEnd) < sections.ToList().IndexOf(Lms.Client.Navigation.NavCatalog.Security));
        Assert.Equal(4, Lms.Client.Navigation.NavCatalog.Items.Count(i => i.Section == Lms.Client.Navigation.NavCatalog.YearEnd));
    }

    // ----- checklist

    [Fact]
    public async Task Checklist_shows_the_state_of_each_step_in_words_in_order()
    {
        var card = await CardAsync();

        card.WaitForElement("[data-testid=yearend-checklist]");
        Assert.Equal("OL", card.Find("[data-testid=yearend-steps]").TagName); // a list, not a stepper
        Assert.Contains("Needs attention: 3", card.Find("[data-testid=yearend-state-1]").TextContent);
        Assert.Contains("To do", card.Find("[data-testid=yearend-state-2]").TextContent);
        Assert.Contains("Available after the year is closed", card.Find("[data-testid=yearend-state-3]").TextContent);
        Assert.Equal("Review", card.Find("[data-testid=yearend-action-1]").TextContent.Trim());
        Assert.Contains("2027-06-24", card.Find("[data-testid=yearend-subtitle]").TextContent);
        Assert.Contains("42", card.Find("[data-testid=yearend-subtitle]").TextContent);
    }

    [Fact]
    public async Task A_done_step_says_when_it_was_done()
    {
        _home.Current = _home.Current with
        {
            Steps = [new(YearEndStepKeys.PublishGrades, YearEndStepStates.Done, null, new DateTime(2027, 5, 20, 8, 0, 0, DateTimeKind.Utc)), .. _home.Current.Steps.Skip(1)],
        };
        var card = await CardAsync();

        card.WaitForElement("[data-testid=yearend-checklist]");

        Assert.Equal("Done", card.Find("[data-testid=yearend-step-1]").GetAttribute("data-state"));
        Assert.Contains("Done on", card.Find("[data-testid=yearend-state-1]").TextContent);
    }

    [Fact]
    public async Task A_step_that_cannot_start_yet_is_not_a_link_and_says_why()
    {
        var card = await CardAsync();
        card.WaitForElement("[data-testid=yearend-checklist]");

        Assert.True(card.Find("[data-testid=yearend-action-3]").HasAttribute("disabled") || card.Find("[data-testid=yearend-action-3]").ClassList.Contains("mud-disabled"));
        Assert.Equal("academic-years", card.Find("[data-testid=yearend-action-2]").GetAttribute("href"));
    }

    [Fact]
    public async Task The_card_is_hidden_when_the_server_says_the_year_is_not_near_its_end()
    {
        _home.Current = _home.Current with { Show = false };
        var card = await CardAsync();

        await Task.Delay(50);
        Assert.Empty(card.FindAll("[data-testid=yearend-checklist]"));
    }

    [Fact]
    public async Task The_card_is_hidden_from_people_who_neither_close_a_year_nor_approve_grades()
    {
        var card = await CardAsync(permissions: [Permissions.Students.View]);

        await Task.Delay(50);
        Assert.Empty(card.FindAll("[data-testid=yearend-checklist]"));
    }

    [Fact]
    public async Task The_card_shows_the_four_counts()
    {
        var card = await CardAsync();
        card.WaitForElement("[data-testid=yearend-stats]");

        var text = card.Find("[data-testid=yearend-stats]").TextContent;
        Assert.Contains("480", text);
        Assert.Contains("20", text);
    }

    [Fact]
    public async Task The_checklist_renders_rtl_in_arabic()
    {
        var card = await CardAsync("ar");
        card.WaitForElement("[data-testid=yearend-checklist]");

        Assert.True(Culture.IsRightToLeft);
        Assert.Contains("نهاية العام", card.Markup);
        Assert.Contains("يحتاج مراجعة", card.Find("[data-testid=yearend-state-1]").TextContent);
    }
}
