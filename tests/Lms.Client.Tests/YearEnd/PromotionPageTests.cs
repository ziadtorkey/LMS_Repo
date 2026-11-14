using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using Lms.Client.Components.People;
using Lms.Client.Pages.Promotion;
using Lms.Client.Services.Api;
using Lms.Client.Services.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Lms.Shared.YearEnd;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.YearEnd;

/// <summary>The promotion pages: choose and preview (M7 H3; FR-STU-07, PRD D78 to D84).</summary>
public sealed class PromotionPageTests : ClassesTestContext
{
    private readonly FakePromotionApi _promotion = new(30);

    public PromotionPageTests()
    {
        Services.AddSingleton<IPromotionApi>(_promotion);
        Culture.Current = new CultureInfo("en");
        FakeCultureService.Apply(new CultureInfo("en"));
    }

    private static Func<IRenderedComponent<MudSelect<T>>, bool> Has<T>(string testId) =>
        s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == testId;

    private IRenderedComponent<MudSelect<T>> Select<T>(IRenderedComponent<PromotionPreview> cut, string testId) => cut.FindComponents<MudSelect<T>>().First(Has<T>(testId));

    private static IRenderedComponent<MudSelect<Guid?>> YearSelect(IRenderedComponent<PromotionPage> cut, string testId) =>
        cut.FindComponents<MudSelect<Guid?>>().First(x => Equals(x.Instance.UserAttributes.GetValueOrDefault("data-testid"), testId));

    private async Task<PromotionRunDto> NewRunAsync() =>
        await _promotion.CreateRunAsync(new CreatePromotionRunRequest(FakePromotionApi.SourceYearId, FakePromotionApi.UnitId, FakePromotionApi.TargetYearId, true));

    private IRenderedComponent<PromotionPage> OpenPage(Guid? runId = null)
    {
        var cut = Render<PromotionPage>(p => p.Add(x => x.RunId, runId));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=promo-loading]")));
        return cut;
    }

    private async Task<IRenderedComponent<PromotionPreview>> OpenPreviewAsync(PromotionRunDto run)
    {
        await GrantAsync(Permissions.Promotion.Run);
        var provider = Render<MudDialogProvider>();
        _ = provider;
        var cut = Render<PromotionPreview>(p => p.Add(x => x.RunId, run.Id));
        cut.WaitForElement("[data-testid=promo-preview]");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid^=promo-row-]")));
        return cut;
    }

    // --- the page

    [Fact]
    public void Without_the_permission_the_page_says_so_and_shows_nothing_else()
    {
        var cut = OpenPage();

        Assert.NotEmpty(cut.FindAll("[data-testid=promo-forbidden]"));
        Assert.Empty(cut.FindAll("[data-testid=promo-setup]"));
    }

    [Fact]
    public async Task The_steps_mark_where_the_person_is()
    {
        await GrantAsync(Permissions.Promotion.Run);
        var cut = OpenPage();
        Assert.Equal("step", cut.Find("[data-testid=promo-step-choose]").GetAttribute("aria-current"));

        var run = await NewRunAsync();
        var preview = OpenPage(run.Id);
        Assert.Equal("step", preview.Find("[data-testid=promo-step-preview]").GetAttribute("aria-current"));
        Assert.Null(preview.Find("[data-testid=promo-step-choose]").GetAttribute("aria-current"));
    }

    // --- step 1

    [Fact]
    public async Task Only_a_closed_year_is_offered_as_the_source_and_none_closed_says_why_D79()
    {
        await GrantAsync(Permissions.Promotion.Run);
        YearsApi.AddYear(FakeUnits.SchoolId, "٢٠٢٥", "2025/2026", new DateOnly(2025, 9, 1), new DateOnly(2026, 6, 30), AcademicYearStatuses.Closed);
        YearsApi.AddYear(FakeUnits.SchoolId, "٢٠٢٦", "2026/2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), AcademicYearStatuses.Active);
        var cut = OpenPage();

        await cut.InvokeAsync(() => cut.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=promo-no-closed-year]")));
        var popovers = Render<MudPopoverProvider>();
        await YearSelect(cut, "promo-source-year").Find(".mud-input-control").MouseDownAsync(new());
        popovers.WaitForAssertion(() => Assert.NotEmpty(popovers.FindAll(".mud-list-item")));
        var sourceOptions = popovers.FindAll(".mud-list-item").Select(e => e.TextContent).ToList();

        Assert.Contains(sourceOptions, o => o.Contains("2025/2026"));
        Assert.DoesNotContain(sourceOptions, o => o.Contains("2026/2027")); // the active year is a target, never a source
    }

    [Fact]
    public async Task A_unit_without_a_closed_year_explains_and_links_to_the_years()
    {
        await GrantAsync(Permissions.Promotion.Run);
        var cut = OpenPage();

        await cut.InvokeAsync(() => cut.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));

        cut.WaitForElement("[data-testid=promo-no-closed-year]");
        Assert.Contains("academic-years", cut.Find("[data-testid=promo-no-closed-year] a").GetAttribute("href"));
        Assert.True(cut.Find("[data-testid=promo-create]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Create_opens_the_preview_of_the_new_run()
    {
        await GrantAsync(Permissions.Promotion.Run);
        var closed = YearsApi.AddYear(FakeUnits.SchoolId, "٢٠٢٥", "2025/2026", new DateOnly(2025, 9, 1), new DateOnly(2026, 6, 30), AcademicYearStatuses.Closed);
        YearsApi.AddYear(FakeUnits.SchoolId, "٢٠٢٦", "2026/2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), AcademicYearStatuses.Active);
        var cut = OpenPage();
        await cut.InvokeAsync(() => cut.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        var target = (await YearsApi.GetEffectiveAsync(FakeUnits.SchoolId)).Years.First(y => y.Status == AcademicYearStatuses.Active);

        await cut.InvokeAsync(() => YearSelect(cut, "promo-source-year").Instance.ValueChanged.InvokeAsync(closed.Id));
        await cut.InvokeAsync(() => YearSelect(cut, "promo-target-year").Instance.ValueChanged.InvokeAsync(target.Id));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=promo-create]").HasAttribute("disabled")));
        await cut.Find("[data-testid=promo-create]").ClickAsync(new());

        Assert.Contains("promotion/", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Single((await _promotion.ListRunsAsync()).Items);
    }

    [Fact]
    public async Task A_server_refusal_is_shown_and_nothing_navigates()
    {
        await GrantAsync(Permissions.Promotion.Run);
        _promotion.SourceClosed = false; // the server says year_not_closed
        var closed = YearsApi.AddYear(FakeUnits.SchoolId, "٢٠٢٥", "2025/2026", new DateOnly(2025, 9, 1), new DateOnly(2026, 6, 30), AcademicYearStatuses.Closed);
        YearsApi.AddYear(FakeUnits.SchoolId, "٢٠٢٦", "2026/2027", new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), AcademicYearStatuses.Active);
        var cut = OpenPage();
        await cut.InvokeAsync(() => cut.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        var active = (await YearsApi.GetEffectiveAsync(FakeUnits.SchoolId)).Years.First(y => y.Status == AcademicYearStatuses.Active);
        await cut.InvokeAsync(() => YearSelect(cut, "promo-source-year").Instance.ValueChanged.InvokeAsync(closed.Id));
        await cut.InvokeAsync(() => YearSelect(cut, "promo-target-year").Instance.ValueChanged.InvokeAsync(active.Id));
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=promo-create]").HasAttribute("disabled")));

        await cut.Find("[data-testid=promo-create]").ClickAsync(new());

        cut.WaitForElement("[data-testid=promo-error]");
        Assert.DoesNotContain("promotion/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    // --- step 2

    [Fact]
    public async Task The_preview_lists_the_students_with_counts_and_is_server_paged()
    {
        var run = await NewRunAsync();
        var cut = await OpenPreviewAsync(run);

        Assert.Equal(30, cut.FindAll("[data-testid^=promo-row-]").Count);
        Assert.Equal("20", cut.Find("[data-testid=promo-summary-Promote] .lms-stat-value").TextContent);
        Assert.Equal("10", cut.Find("[data-testid=promo-summary-Graduate] .lms-stat-value").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=promo-blocking]"));
        Assert.False(cut.Find("[data-testid=promo-continue]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Changing_an_outcome_is_saved_as_a_draft_announced_and_survives_a_reload()
    {
        var run = await NewRunAsync();
        var cut = await OpenPreviewAsync(run);
        var first = (await _promotion.GetRowsAsync(run.Id, new PromotionRowsQuery(PromotionOutcomes.Promote))).Items[0];

        await cut.InvokeAsync(() => Select<string>(cut, $"promo-outcome-{first.StudentId}").Instance.ValueChanged.InvokeAsync(PromotionOutcomes.Repeat));

        cut.WaitForAssertion(() => Assert.Contains("Repeat", cut.Find("[data-testid=promo-announcer]").TextContent));
        Assert.Equal("polite", cut.Find("[data-testid=promo-announcer]").GetAttribute("aria-live"));
        Assert.Equal(PromotionOutcomes.Repeat, (await _promotion.GetRowsAsync(run.Id, new PromotionRowsQuery(PromotionOutcomes.Repeat))).Items.Single().Outcome);
        cut.WaitForAssertion(() => Assert.Equal("1", cut.Find("[data-testid=promo-summary-Repeat] .lms-stat-value").TextContent));

        var reloaded = Render<PromotionPreview>(p => p.Add(x => x.RunId, run.Id));
        reloaded.WaitForAssertion(() => Assert.Equal("1", reloaded.Find("[data-testid=promo-summary-Repeat] .lms-stat-value").TextContent));
    }

    [Fact]
    public async Task Every_outcome_and_target_select_is_named_by_the_student()
    {
        var run = await NewRunAsync();
        var cut = await OpenPreviewAsync(run);

        var outcome = cut.FindComponents<MudSelect<string>>().First(Has<string>(cut.FindAll("[data-testid^=promo-outcome-]").First().GetAttribute("data-testid")!));

        Assert.StartsWith("Outcome for Student", Convert.ToString(outcome.Instance.UserAttributes["aria-label"]));
        Assert.NotEmpty(cut.FindAll("[data-testid^=promo-target-]"));
    }

    [Fact]
    public async Task Search_filters_on_the_server()
    {
        var run = await NewRunAsync();
        var cut = await OpenPreviewAsync(run);

        await cut.InvokeAsync(() => cut.FindComponent<MudTextField<string>>().Instance.ValueChanged.InvokeAsync("Student 7"));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid^=promo-row-]")));
    }

    [Fact]
    public async Task A_bulk_change_asks_first_then_sends_a_filter_and_reloads()
    {
        var run = await NewRunAsync();
        var provider = Render<MudDialogProvider>();
        await GrantAsync(Permissions.Promotion.Run);
        var cut = Render<PromotionPreview>(p => p.Add(x => x.RunId, run.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid^=promo-row-]")));
        await cut.InvokeAsync(() => Select<string>(cut, "promo-filter-outcome").Instance.ValueChanged.InvokeAsync(PromotionOutcomes.Graduate));
        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll("[data-testid^=promo-row-]").Count));
        await cut.InvokeAsync(() => Select<string>(cut, "promo-bulk-outcome").Instance.ValueChanged.InvokeAsync(PromotionOutcomes.Repeat));

        _ = cut.Find("[data-testid=promo-bulk-set]").ClickAsync(new()); // the click returns when the dialog is answered
        provider.WaitForElement("[data-testid=confirm-effect-yes]");
        Assert.Contains("10 students", provider.Find("[data-testid=confirm-effect-count]").TextContent);
        await provider.Find("[data-testid=confirm-effect-yes]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal("10", cut.Find("[data-testid=promo-summary-Repeat] .lms-stat-value").TextContent));
        Assert.Equal("0", cut.Find("[data-testid=promo-summary-Graduate] .lms-stat-value").TextContent);
    }

    [Fact]
    public async Task A_missing_level_mapping_blocks_continue_and_links_to_the_fix()
    {
        _promotion.LevelMappingMissing = true;
        var run = await NewRunAsync();
        var cut = await OpenPreviewAsync(run);

        Assert.NotEmpty(cut.FindAll("[data-testid=promo-blocking]"));
        Assert.Contains("next level", cut.Find("[data-testid=promo-issue-NoLevelMapping]").TextContent);
        Assert.NotNull(cut.Find("[data-testid=promo-issue-NoLevelMapping] a").GetAttribute("href"));
        Assert.True(cut.Find("[data-testid=promo-continue]").HasAttribute("disabled"));
        Assert.NotEmpty(cut.FindAll("[data-testid=promo-continue-hint]"));
    }

    [Fact]
    public async Task Continue_goes_to_the_run_step()
    {
        var run = await NewRunAsync();
        var cut = await OpenPreviewAsync(run);

        await cut.Find("[data-testid=promo-continue]").ClickAsync(new());

        Assert.EndsWith($"promotion/{run.Id}/run", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task A_run_that_is_no_longer_a_draft_is_read_only()
    {
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        var cut = await OpenPreviewAsync(run);

        Assert.NotEmpty(cut.FindAll("[data-testid=promo-readonly]"));
        Assert.Empty(cut.FindAll("[data-testid=promo-bulk]"));
        Assert.True(cut.Find("[data-testid=promo-continue]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task A_missing_run_shows_an_error_not_a_crash()
    {
        await GrantAsync(Permissions.Promotion.Run);

        var cut = Render<PromotionPreview>(p => p.Add(x => x.RunId, Guid.NewGuid()));

        cut.WaitForElement("[data-testid=promo-load-error]");
    }

    [Fact]
    public async Task The_page_renders_in_arabic_with_the_rtl_labels()
    {
        Culture.Current = new CultureInfo("ar");
        FakeCultureService.Apply(new CultureInfo("ar"));
        var run = await NewRunAsync();
        var cut = await OpenPreviewAsync(run);

        Assert.Contains("الطالب", cut.Markup);
        Assert.Contains("ترقية", cut.Markup);
        Assert.DoesNotContain("Promo_", cut.Markup); // no missing resource key shows its name
    }

    [Fact]
    public void Promotion_resources_exist_in_both_languages()
    {
        foreach (var culture in new[] { "ar", "en" })
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var l = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Lms.Client.Resources.SharedResource>>();
            string[] keys = [.. PromotionOutcomes.All.Select(o => $"Promo_Outcome_{o}"), .. PromotionRunStates.All.Select(s => $"Promo_State_{s}"), "Promo_Title", "Promo_Create", "Promo_Continue", "Nav_Promotion"];
            Assert.All(keys, k => Assert.False(l[k].ResourceNotFound, $"{culture}:{k}"));
        }
    }

    [Fact]
    public void The_nav_shows_the_entry_only_with_the_permission()
    {
        var item = Lms.Client.Navigation.NavCatalog.Items.Single(i => i.ResourceKey == "Nav_Promotion");

        Assert.Equal(Permissions.Promotion.Run, item.RequiredPermission);
        Assert.Equal("promotion", item.Href);
    }
}
