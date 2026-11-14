using System.Globalization;
using Bunit;
using Lms.Client.Components.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Lms.Shared.YearEnd;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Components.YearEnd;

/// <summary>The M7 presentational components (spec #814 §1–§8, §11): structure, words, states and accessibility; no data access.</summary>
public sealed class YearEndComponentsTests : ClientTestContext
{
    private readonly ManualClock _clock = new();

    public YearEndComponentsTests() => Services.AddSingleton<TimeProvider>(_clock);

    private static void English() => FakeCultureService.Apply(new CultureInfo("en"));

    private static YearClosePreviewDto Preview(IReadOnlyList<YearEndIssueDto>? blocking = null, IReadOnlyList<YearEndIssueDto>? warnings = null) =>
        new(Guid.NewGuid(), "2026/2027", "2026/2027", 184, 6240, blocking ?? [], warnings ?? [new(YearEndIssueCodes.UnpublishedGradeItems, 12, null)]);

    // --- year status chip

    [Theory]
    [InlineData(AcademicYearStatuses.Planned, "Planned", "mud-chip-color-info", false)]
    [InlineData(AcademicYearStatuses.Active, "Active", "mud-chip-color-success", false)]
    [InlineData(AcademicYearStatuses.Closed, "Closed", "mud-chip-color-default", true)]
    public void Year_status_chip_says_the_status_in_words_and_a_closed_year_has_a_lock(string status, string text, string color, bool locked)
    {
        English();
        var cut = Render<YearStatusChip>(p => p.Add(c => c.Status, status));

        var chip = cut.Find(".lms-status-chip");
        Assert.Contains(text, chip.TextContent);
        Assert.Contains(color, chip.ClassList);
        Assert.Equal(status, chip.GetAttribute("data-status"));
        Assert.Equal(locked, cut.FindAll(".lms-year-lock").Count == 1);
    }

    // --- close-year confirmation

    [Fact]
    public void Close_confirmation_lists_what_closing_locks_and_warnings_that_do_not_block()
    {
        English();
        var cut = Render<CloseYearConfirmation>(p => p.Add(c => c.Preview, Preview()).Add(c => c.IssueHref, i => $"fix/{i.Code}"));

        var effects = cut.Find("[data-testid=year-close-effects]").TextContent;
        Assert.Contains("184 gradebooks are locked", effects);
        Assert.Contains("6240 attendance sessions are locked", effects.Replace(",", ""));
        var warning = cut.Find("[data-testid=year-close-warning-UnpublishedGradeItems]");
        Assert.Contains("Some grade items are not published.", warning.TextContent);
        Assert.Contains("12", warning.TextContent);
        Assert.Equal("fix/UnpublishedGradeItems", warning.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid=year-close-blocking]"));
        Assert.True(cut.Find("[data-testid=year-close-confirm]").HasAttribute("disabled")); // nothing typed yet
    }

    [Theory]
    [InlineData("2026/2027", true)]
    [InlineData("  2026/2027 ", true)]
    [InlineData("2026/27", false)]
    public void Typing_the_year_name_enables_close_and_passes_the_typed_name(string typed, bool enabled)
    {
        English();
        string? confirmed = null;
        var cut = Render<CloseYearConfirmation>(p => p.Add(c => c.Preview, Preview()).Add(c => c.OnConfirm, s => confirmed = s));

        cut.Find("[data-testid=year-close-confirm-input] input").Input(typed);

        var button = cut.Find("[data-testid=year-close-confirm]");
        Assert.Equal(enabled, !button.HasAttribute("disabled"));
        if (enabled)
        {
            button.Click();
            Assert.Equal("2026/2027", confirmed);
        }
    }

    [Fact]
    public void A_blocking_issue_keeps_close_disabled_and_says_why()
    {
        English();
        var cut = Render<CloseYearConfirmation>(p => p.Add(c => c.Preview, Preview(blocking: [new(YearEndIssueCodes.OpenTerms, 1, null)])));

        cut.Find("[data-testid=year-close-confirm-input] input").Input("2026/2027");

        Assert.Contains("Some terms are still open.", cut.Find("[data-testid=year-close-blocking]").TextContent);
        var button = cut.Find("[data-testid=year-close-confirm]");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("year-close-why", button.GetAttribute("aria-describedby"));
    }

    // --- promotion preview row

    private static PromotionRowDto Row(string outcome = PromotionOutcomes.Promote, Guid? target = null, string? problem = null, params string[] warnings) =>
        new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "ST-2024-0091", "عمر صالح", "Omar Saleh", Guid.NewGuid(), "10-أ", "10-A",
            outcome, PromotionOutcomes.Promote, target, target is null ? null : "11-أ", target is null ? null : "11-A", problem, warnings, null);

    private static readonly Guid ClassA = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly IReadOnlyList<PromotionTargetClass> Targets = [new(ClassA, "11-أ", "11-A"), new(Guid.Parse("00000000-0000-0000-0000-0000000000c2"), "11-ب", "11-B")];

    private IRenderedComponent<PromotionPreviewRow> RenderRow(PromotionRowDto row, Action<PromotionDecisionDto>? onDecision = null) =>
        Render<PromotionPreviewRow>(p => p.Add(c => c.Row, row).Add(c => c.TargetClasses, Targets).Add(c => c.OnDecision, d => onDecision?.Invoke(d)));

    [Fact]
    public void Promotion_row_names_its_controls_for_the_student()
    {
        English();
        var cut = RenderRow(Row(target: ClassA));

        Assert.Contains("Omar Saleh", cut.Find(".lms-promo-student").TextContent);
        Assert.Equal("ltr", cut.Find(".lms-promo-code").GetAttribute("dir"));
        Assert.Equal("Outcome for Omar Saleh", cut.Find("[data-testid^=promo-outcome-]").GetAttribute("aria-label"));
        Assert.Equal("Target class for Omar Saleh", cut.Find("[data-testid^=promo-target-]").GetAttribute("aria-label"));
        Assert.Equal("Select Omar Saleh", cut.Find("input[type=checkbox]").GetAttribute("aria-label"));
        Assert.Equal(4, cut.FindAll("[data-testid^=promo-outcome-] option").Count);
        Assert.Empty(cut.FindAll(".lms-promo-changed"));
    }

    [Fact]
    public void Changing_the_outcome_raises_a_decision_and_marks_the_row_changed_with_a_reset()
    {
        English();
        var decisions = new List<PromotionDecisionDto>();
        var row = Row(target: ClassA);
        var cut = RenderRow(row, decisions.Add);

        cut.Find("[data-testid^=promo-outcome-]").Change(PromotionOutcomes.Repeat);
        Assert.Equal(new PromotionDecisionDto(row.StudentId, PromotionOutcomes.Repeat, ClassA), decisions.Single());

        cut.Render(p => p.Add(c => c.Row, row with { Outcome = PromotionOutcomes.Repeat }));
        Assert.Contains("Changed", cut.Find(".lms-promo-changed").TextContent);
        cut.Find("[data-testid$=-reset]").Click();
        Assert.Equal(PromotionOutcomes.Promote, decisions.Last().Outcome);
    }

    [Fact]
    public void Graduate_needs_no_target_class_and_a_missing_class_is_an_error_in_words()
    {
        English();
        var graduate = RenderRow(Row(PromotionOutcomes.Graduate));
        Assert.Empty(graduate.FindAll("[data-testid^=promo-target-]"));
        Assert.Contains("Not needed", graduate.Find(".lms-promo-target").TextContent);

        var missing = RenderRow(Row(problem: YearEndIssueCodes.TargetClassMissing, warnings: YearEndIssueCodes.PendingExcuseRequests));
        Assert.Equal("true", missing.Find("[data-testid^=promo-target-]").GetAttribute("aria-invalid"));
        Assert.Contains("No target class", missing.Find(".lms-promo-problem").TextContent);
        Assert.Contains("Open excuse request", missing.Find(".lms-promo-warnings").TextContent);
    }

    // --- undo-window banner

    private IRenderedComponent<UndoWindowBanner> RenderUndo(DateTime? until, string? overReason = null, Action? onUndo = null) =>
        Render<UndoWindowBanner>(p => p
            .Add(c => c.UndoUntilUtc, until).Add(c => c.ServerNowUtc, _clock.GetUtcNow().UtcDateTime).Add(c => c.OverReasonCode, overReason)
            .Add(c => c.TimeZone, TimeZoneInfo.Utc).Add(c => c.OnUndo, () => onUndo?.Invoke()));

    [Fact]
    public void Undo_banner_shows_the_clock_time_and_the_minutes_left_from_server_time()
    {
        English();
        var undone = false;
        var cut = RenderUndo(_clock.GetUtcNow().UtcDateTime.AddMinutes(9), onUndo: () => undone = true);

        var banner = cut.Find("[data-testid=promo-undo-banner]");
        Assert.Contains("until 08:09", banner.TextContent);
        Assert.Contains("9 minutes left", banner.TextContent);
        Assert.Equal("2026-11-20T08:09:00Z", banner.GetAttribute("data-until"));
        cut.Find("[data-testid=promo-undo]").Click();
        Assert.True(undone);
    }

    [Fact]
    public void Undo_banner_counts_down_and_closes_the_window_when_the_time_is_up()
    {
        English();
        var cut = RenderUndo(_clock.GetUtcNow().UtcDateTime.AddMinutes(2));

        _clock.Advance(TimeSpan.FromSeconds(60));
        cut.WaitForAssertion(() => Assert.Contains("1 minute left", cut.Find("[data-testid=promo-undo-banner]").TextContent));

        _clock.Advance(TimeSpan.FromSeconds(60));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("over", cut.Find("[data-testid=promo-undo-banner]").ClassList);
            Assert.True(cut.Find("[data-testid=promo-undo]").HasAttribute("disabled"));
            Assert.Contains("The time to undo this promotion has passed.", cut.Find("[data-testid=promo-undo-banner] [role=status]").TextContent);
        });
    }

    [Fact]
    public void Undo_banner_says_the_server_reason_when_activity_ended_the_window()
    {
        English();
        var cut = RenderUndo(null, overReason: "promotion_undo_activity_exists");

        Assert.Contains("over", cut.Find("[data-testid=promo-undo-banner]").ClassList);
        Assert.Contains("the new year already", cut.Find("[data-testid=promo-undo-banner]").TextContent);
        Assert.True(cut.Find("[data-testid=promo-undo]").HasAttribute("disabled"));
    }

    // --- withdrawal reason picker

    private static readonly IReadOnlyList<WithdrawalReasonDto> Reasons =
        [new("Move", "الانتقال إلى مدرسة أخرى", "Moving to another school", true), new("Old", "قديم", "Retired reason", false), new("Health", "أسباب صحية", "Health", true)];

    [Fact]
    public void Reason_picker_offers_the_active_reasons_and_other_last_and_says_the_reason_is_staff_only()
    {
        English();
        var cut = Render<WithdrawalReasonPicker>(p => p.Add(c => c.Reasons, Reasons));

        var options = cut.FindAll("[data-testid=withdraw-reason] option:not([disabled])").Select(o => o.TextContent.Trim()).Where(t => t.Length > 0).ToList();
        Assert.Equal(["Moving to another school", "Health", "Other"], options);
        Assert.Contains("Staff only", cut.Markup);
    }

    [Fact]
    public async Task Other_needs_a_note_and_validate_reports_both_missing_fields()
    {
        English();
        string? key = null;
        var cut = Render<WithdrawalReasonPicker>(p => p.Add(c => c.Reasons, Reasons).Add(c => c.ReasonKeyChanged, k => key = k));

        Assert.False(await cut.InvokeAsync(() => cut.Instance.Validate()));
        Assert.Contains("Choose a reason.", cut.Markup);

        cut.Find("[data-testid=withdraw-reason]").Change(WithdrawalReasons.Other);
        Assert.Equal(WithdrawalReasons.Other, key);
        cut.Render(p => p.Add(c => c.ReasonKey, WithdrawalReasons.Other));
        Assert.False(await cut.InvokeAsync(() => cut.Instance.Validate()));
        Assert.Contains("Write a note for “Other”.", cut.Markup);
        Assert.Equal("true", cut.Find("[data-testid=withdraw-note] textarea").GetAttribute("aria-required"));

        cut.Render(p => p.Add(c => c.Note, "The family is moving abroad."));
        Assert.True(await cut.InvokeAsync(() => cut.Instance.Validate()));
    }

    // --- year-end checklist

    [Fact]
    public void Checklist_is_an_ordered_list_whose_step_states_are_in_words()
    {
        English();
        var cut = Render<YearEndChecklist>(p => p
            .Add(c => c.YearName, "2026/2027").Add(c => c.EndDate, new DateOnly(2027, 6, 24)).Add(c => c.DaysLeft, 42)
            .Add(c => c.Steps, new YearEndStep[]
            {
                new("Publish the term grades", "12 grade items aren't published yet.", YearEndStepState.Attention, "Review", "grades"),
                new("Close the year", "Locks grades and attendance.", YearEndStepState.ToDo, "Close 2026/2027", "years"),
                new("Run the promotion", "Available after the year is closed.", YearEndStepState.NotYet),
                new("Open 2027/2028", "The year is Planned.", YearEndStepState.Done, "View", "years"),
            }));

        Assert.Contains("Year end: 2026/2027", cut.Markup);
        Assert.Contains("in 42 days", cut.Find(".lms-section").TextContent);
        Assert.Equal("ltr", cut.Find(".lms-checklist-date").GetAttribute("dir"));
        var steps = cut.FindAll("ol.lms-checklist > li");
        Assert.Equal(4, steps.Count);
        Assert.Contains("Needs attention", steps[0].TextContent);
        Assert.Contains("Not available yet", steps[2].TextContent);
        Assert.Contains("Done", steps[3].TextContent);
        Assert.Equal("grades", steps[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(steps[2].QuerySelector("a"));
    }

    [Fact]
    public void Components_render_in_arabic_without_raw_keys()
    {
        var close = Render<CloseYearConfirmation>(p => p.Add(c => c.Preview, Preview()));
        var undo = RenderUndo(_clock.GetUtcNow().UtcDateTime.AddMinutes(9));
        var row = RenderRow(Row(target: ClassA));
        var picker = Render<WithdrawalReasonPicker>(p => p.Add(c => c.Reasons, Reasons));

        foreach (var markup in new[] { close.Markup, undo.Markup, row.Markup, picker.Markup })
            Assert.DoesNotMatch(@"\b(YearEnd|Promo|Lifecycle|YearIssue)_\w+", markup);
        Assert.Contains("عمر صالح", row.Markup);
        Assert.Contains("9 دقائق", undo.Markup);
    }
}
