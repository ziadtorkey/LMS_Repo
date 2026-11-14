using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Lms.Client.Pages.Promotion;
using Lms.Client.Services.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.YearEnd;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.YearEnd;

/// <summary>Execute, progress, result, undo window and history of a promotion (M7 H4; FR-STU-07, PRD D12, D84).</summary>
public sealed class PromotionRunPageTests : ClassesTestContext
{
    private readonly ManualClock _clock = new();
    private readonly FakePromotionApi _promotion = new(30);

    public PromotionRunPageTests()
    {
        _promotion.Now = () => _clock.GetUtcNow().UtcDateTime;
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IPromotionApi>(_promotion);
        Culture.Current = new CultureInfo("en");
        FakeCultureService.Apply(new CultureInfo("en"));
    }

    private async Task<PromotionRunDto> NewRunAsync() =>
        await _promotion.CreateRunAsync(new CreatePromotionRunRequest(FakePromotionApi.SourceYearId, FakePromotionApi.UnitId, FakePromotionApi.TargetYearId, true));

    private async Task<(IRenderedComponent<PromotionRunPage> Page, IRenderedComponent<MudDialogProvider> Dialogs)> OpenAsync(Guid runId)
    {
        await GrantAsync(Permissions.Promotion.Run);
        var dialogs = Render<MudDialogProvider>();
        var page = Render<PromotionRunPage>(p => p.Add(x => x.RunId, runId).Add(x => x.PollEvery, TimeSpan.FromMilliseconds(5)).Add(x => x.AnnounceEvery, TimeSpan.Zero));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=promo-run-loading]")));
        return (page, dialogs);
    }

    private static async Task ConfirmAsync(IRenderedComponent<MudDialogProvider> dialogs, string? typed = null)
    {
        dialogs.WaitForElement("[data-testid=confirm-effect-yes]");
        if (typed is not null) dialogs.Find("input[data-testid=confirm-effect-input]").Input(typed);
        dialogs.WaitForAssertion(() => Assert.False(dialogs.Find("[data-testid=confirm-effect-yes]").HasAttribute("disabled")));
        await dialogs.Find("[data-testid=confirm-effect-yes]").ClickAsync(new());
    }

    [Fact]
    public async Task Without_the_permission_the_page_says_so()
    {
        var run = await NewRunAsync();
        var page = Render<PromotionRunPage>(p => p.Add(x => x.RunId, run.Id));

        page.WaitForElement("[data-testid=promo-forbidden]");
    }

    [Fact]
    public async Task A_draft_shows_the_effect_and_the_count_and_the_back_link()
    {
        var run = await NewRunAsync();
        var (page, _) = await OpenAsync(run.Id);

        Assert.Contains("history stays readable", page.Find("[data-testid=promo-execute-effect]").TextContent);
        Assert.Contains("30 students", page.Find("[data-testid=promo-execute-card]").TextContent);
        Assert.Equal($"promotion/{run.Id}", page.Find("[data-testid=promo-back]").GetAttribute("href"));
        Assert.Equal("step", page.Find("[data-testid=promo-step-run]").GetAttribute("aria-current"));
    }

    [Fact]
    public async Task Execute_needs_the_typed_student_count_then_shows_the_result_and_the_undo_banner()
    {
        var run = await NewRunAsync();
        var (page, dialogs) = await OpenAsync(run.Id);

        _ = page.Find("[data-testid=promo-execute]").ClickAsync(new());
        dialogs.WaitForElement("[data-testid=confirm-effect-input]");
        Assert.True(dialogs.Find("[data-testid=confirm-effect-yes]").HasAttribute("disabled")); // nothing typed yet
        await ConfirmAsync(dialogs, "30");

        page.WaitForElement("[data-testid=promo-result]");
        Assert.Equal("20", page.Find("[data-testid=promo-result-Promote] .lms-stat-value").TextContent);
        Assert.NotEmpty(page.FindAll("[data-testid=promo-undo-banner]"));
        Assert.Equal(PromotionRunStates.Done, (await _promotion.GetRunAsync(run.Id)).State);
    }

    [Fact]
    public async Task A_blocker_disables_execute_and_cancel_runs_nothing()
    {
        _promotion.LevelMappingMissing = true;
        var run = await NewRunAsync();
        var (page, _) = await OpenAsync(run.Id);
        Assert.True(page.Find("[data-testid=promo-execute]").HasAttribute("disabled"));

        _promotion.LevelMappingMissing = false;
        var again = await OpenAsync(run.Id);
        _ = again.Page.Find("[data-testid=promo-execute]").ClickAsync(new());
        again.Dialogs.WaitForElement("[data-testid=confirm-effect-cancel]");
        await again.Dialogs.Find("[data-testid=confirm-effect-cancel]").ClickAsync(new());

        Assert.Equal(PromotionRunStates.Draft, (await _promotion.GetRunAsync(run.Id)).State);
    }

    [Fact]
    public async Task A_running_job_shows_progress_announces_politely_and_ends_with_the_result()
    {
        _promotion.Slow = true;
        var run = await NewRunAsync();
        var (page, dialogs) = await OpenAsync(run.Id);
        _ = page.Find("[data-testid=promo-execute]").ClickAsync(new());
        await ConfirmAsync(dialogs, "30");

        page.WaitForElement("[data-testid=promo-progress]");
        Assert.Equal("polite", page.Find("[data-testid=job-announcer]").GetAttribute("aria-live"));
        Assert.Empty(page.FindAll("[data-testid=promo-result]"));

        _promotion.Advance(run.Id, 30);

        page.WaitForElement("[data-testid=promo-result]");
        Assert.Empty(page.FindAll("[data-testid=promo-running]"));
    }

    [Fact]
    public async Task The_result_survives_a_reload_because_the_run_id_is_in_the_address()
    {
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));

        var (page, _) = await OpenAsync(run.Id);

        Assert.NotEmpty(page.FindAll("[data-testid=promo-result]"));
        Assert.Empty(page.FindAll("[data-testid=promo-execute]"));
    }

    // --- failures

    [Fact]
    public async Task A_partial_failure_lists_the_students_that_failed_and_says_the_rest_went_through_D84()
    {
        _promotion.FailStudents.Add(3);
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        var (page, _) = await OpenAsync(run.Id);

        page.WaitForElement("[data-testid=promo-failures-note]");
        Assert.Contains("1 students could not be moved", page.Find("[data-testid=promo-failures-note]").TextContent);
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid^=promo-failure-]")));
        Assert.Contains("could not be created", page.Find("[data-testid^=promo-failure-]").ParentElement!.TextContent);
        Assert.NotEmpty(page.FindAll("[data-testid=promo-failures-download]"));
    }

    [Fact]
    public async Task Download_sends_a_csv_through_the_download_script()
    {
        _promotion.FailStudents.Add(3);
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        var (page, _) = await OpenAsync(run.Id);
        var download = JSInterop.SetupVoid("lmsDownloadFile", _ => true);
        download.SetVoidResult();

        await page.Find("[data-testid=promo-failures-download]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["lmsDownloadFile"]));
        Assert.EndsWith(".csv", (string)JSInterop.Invocations["lmsDownloadFile"].Single().Arguments[0]!);
    }

    [Theory]
    [InlineData("S00003", "\"S00003\"")]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("-5", "\"'-5\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    public void Csv_cells_are_quoted_and_a_formula_is_defused(string value, string expected)
    {
        Assert.Equal(expected, PromotionResultCard.Cell(value));
    }

    // --- undo

    [Fact]
    public async Task The_undo_banner_shows_the_clock_time_and_a_countdown_from_the_server_clock_D12()
    {
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        var (page, _) = await OpenAsync(run.Id);

        var text = page.Find("[data-testid=promo-undo-text]").TextContent;

        Assert.Matches(new Regex(@"until \d\d:\d\d \(10 min left\)"), text);
        Assert.NotNull(page.Find("[data-testid=promo-undo-banner]").GetAttribute("data-until"));
        _clock.Advance(TimeSpan.FromMinutes(4));
        page.WaitForAssertion(() => Assert.Contains("(6 min left)", page.Find("[data-testid=promo-undo-text]").TextContent));
    }

    [Fact]
    public async Task The_countdown_follows_the_server_clock_not_the_browser_clock()
    {
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        _promotion.Now = () => _clock.GetUtcNow().UtcDateTime.AddMinutes(3); // the server is three minutes ahead of this browser
        var (page, _) = await OpenAsync(run.Id);

        Assert.Contains("(7 min left)", page.Find("[data-testid=promo-undo-text]").TextContent);
    }

    [Fact]
    public async Task Undo_confirms_then_restores_and_the_banner_goes()
    {
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        var (page, dialogs) = await OpenAsync(run.Id);

        var btn = page.Find("[data-testid=promo-undo]");
        _ = page.Find("[data-testid=promo-undo]").ClickAsync(new());
        await ConfirmAsync(dialogs);

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=promo-undo-banner]")));
        Assert.Equal(PromotionRunStates.Undone, (await _promotion.GetRunAsync(run.Id)).State);
        Assert.Contains("undone", page.Find("[data-testid=promo-result-state]").TextContent);
    }

    [Fact]
    public async Task After_the_window_the_undo_is_off_and_says_why_by_the_server_clock()
    {
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        var (page, _) = await OpenAsync(run.Id);

        _clock.Advance(TimeSpan.FromMinutes(11));

        page.WaitForAssertion(() => Assert.Contains("has passed", page.Find("[data-testid=promo-undo-over]").TextContent));
        Assert.True(page.Find("[data-testid=promo-undo]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Activity_in_the_new_year_refuses_the_undo_with_its_own_words_and_keeps_it_off()
    {
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        _promotion.ActivityInNewYear = true;
        var (page, dialogs) = await OpenAsync(run.Id);

        _ = page.Find("[data-testid=promo-undo]").ClickAsync(new());
        await ConfirmAsync(dialogs);

        page.WaitForElement("[data-testid=promo-error]");
        page.WaitForAssertion(() => Assert.Contains("new year already has activity", page.Find("[data-testid=promo-undo-over]").TextContent));
        Assert.True(page.Find("[data-testid=promo-undo]").HasAttribute("disabled"));
        Assert.Equal(PromotionRunStates.Done, (await _promotion.GetRunAsync(run.Id)).State);
    }

    // --- history

    [Fact]
    public async Task History_lists_runs_newest_first_with_state_and_links_each_to_its_step()
    {
        var done = await NewRunAsync();
        await _promotion.ExecuteAsync(done.Id, new ExecutePromotionRequest(30));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var draft = await NewRunAsync();
        await GrantAsync(Permissions.Promotion.Run);

        var page = Render<PromotionHistoryPage>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid^=promo-run-]").Count));
        Assert.Equal($"promotion/{draft.Id}", page.Find($"[data-testid=promo-open-{draft.Id}]").GetAttribute("href"));
        Assert.Equal($"promotion/{done.Id}/run", page.Find($"[data-testid=promo-open-{done.Id}]").GetAttribute("href"));
        Assert.Contains("Done", page.Find("[data-state=Done]").TextContent);
        Assert.Equal("promotion", page.Find("[data-testid=promo-new]").GetAttribute("href"));
    }

    [Fact]
    public async Task History_is_empty_with_a_next_step()
    {
        await GrantAsync(Permissions.Promotion.Run);

        var page = Render<PromotionHistoryPage>();

        page.WaitForAssertion(() => Assert.Contains("No promotions yet", page.Markup));
    }

    [Fact]
    public async Task The_promotion_page_links_to_the_history()
    {
        await GrantAsync(Permissions.Promotion.Run);

        var cut = Render<PromotionPage>();

        cut.WaitForAssertion(() => Assert.Equal("promotion/runs", cut.Find("[data-testid=promo-history-open]").GetAttribute("href")));
    }

    [Fact]
    public async Task The_run_page_renders_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        FakeCultureService.Apply(new CultureInfo("ar"));
        var run = await NewRunAsync();
        await _promotion.ExecuteAsync(run.Id, new ExecutePromotionRequest(30));
        var (page, _) = await OpenAsync(run.Id);

        Assert.Contains("التراجع", page.Markup);
        Assert.DoesNotContain("Promo_", page.Markup);
    }

    [Fact]
    public void Run_resources_exist_in_both_languages()
    {
        foreach (var culture in new[] { "ar", "en" })
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var l = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Lms.Client.Resources.SharedResource>>();
            string[] keys =
            [
                .. PromotionRunStates.All.Select(s => $"Promo_RunState_{s}"), "Promo_Execute", "Promo_Undo", "Promo_Undo_Until", "Promo_Undo_WindowOver", "Promo_Undo_Activity",
                "Promo_Failed_Note", "PromoFailure_PlacementFailed", "PromoFailure_Other", "Promo_History", "Common_Open",
            ];
            Assert.All(keys, k => Assert.False(l[k].ResourceNotFound, $"{culture}:{k}"));
        }
    }
}
