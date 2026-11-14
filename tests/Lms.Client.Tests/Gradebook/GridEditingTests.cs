using System.Globalization;
using Bunit;
using Lms.Client.Components.Gradebook;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Gradebook;

/// <summary>Editing scores in the grid and in the phone list (M5 Task 25): optimistic value replaced by the server row, reasons, failures, conflicts.</summary>
public sealed class GridEditingTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private static readonly Guid S0 = FakeGradebookApi.StudentIds[0];
    private static readonly Guid S1 = FakeGradebookApi.StudentIds[1];
    private static readonly Guid S2 = FakeGradebookApi.StudentIds[2];
    private static readonly Guid Manual = FakeGradebookApi.ManualItemId;
    private static readonly Guid Auto = FakeGradebookApi.AutoItemId;
    private const int AutoCol = 0, ManualCol = 2;
    private readonly FakeGradebookApi _api = new();
    private readonly FakeViewport _viewport = new();
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public GridEditingTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IGradebookUnit>(new FakeGradebookUnit());
        Services.AddSingleton<IViewport>(_viewport);
    }

    private async Task<IRenderedComponent<GradebookPage>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.Grades.View, Permissions.Grades.Manage] : permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        _popovers = Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=gradebook-loading]")), Wait);
        return page;
    }

    private static AngleSharp.Dom.IElement Cell(IRenderedComponent<GradebookPage> page, Guid student, Guid item) => page.Find($"[data-testid=cell-{student}-{item}]");

    private static void Press(AngleSharp.Dom.IElement cell, string key, bool shift = false) => cell.KeyDown(new KeyboardEventArgs { Key = key, ShiftKey = shift });

    /// <summary>Starts editing a cell, types a value and commits it with Enter.</summary>
    private static void Type(IRenderedComponent<GradebookPage> page, Guid student, Guid item, string value, string commit = "Enter")
    {
        Press(Cell(page, student, item), "Enter");
        page.WaitForElement($"[data-testid=edit-{student}-{item}]", Wait).Input(value);
        Press(Cell(page, student, item), commit);
    }

    // ---- saving a typed score ----

    [Fact]
    public async Task A_typed_score_is_saved_as_one_entry_with_the_row_version_and_the_server_row_replaces_it()
    {
        var page = await OpenAsync();
        var version = (await _api.GetAsync(Cc, new GradebookQuery())).Rows.Items.First(r => r.StudentId == S0).Version;

        Type(page, S0, Manual, "15");

        page.WaitForAssertion(() => Assert.Equal("15 of 20", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);
        var sent = _api.LastEntries!;
        Assert.Equal(S0, sent.StudentId);
        Assert.Equal(version, sent.Version);
        var change = Assert.Single(sent.Changes);
        Assert.Equal((Manual, GradeEntryActions.Set, 15m, (string?)null), (change.ItemId, change.Action, change.Points, change.Reason));
        Assert.Contains("Saved", page.Find("[data-testid=grid-live-polite]").TextContent);
        // The final is the server's answer for the new row (the demo engine), not something the page computed.
        Assert.NotEqual("90.00", page.Find($"[data-testid=final-{S0}]").TextContent.Trim());
    }

    [Fact]
    public async Task An_unchanged_value_sends_nothing()
    {
        var page = await OpenAsync();

        Type(page, S0, Manual, "18");

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("input.lms-gb-input")), Wait);
        Assert.Equal(0, _api.UpsertCalls);
    }

    [Fact]
    public async Task An_empty_value_clears_the_score()
    {
        var page = await OpenAsync();

        Type(page, S0, Manual, "");

        page.WaitForAssertion(() => Assert.Equal("No score", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);
        Assert.Equal(GradeEntryActions.Clear, _api.LastEntries!.Changes.Single().Action);
    }

    [Theory]
    [InlineData("١٢", 12)]
    [InlineData("12,5", 12.5)]
    [InlineData("١٢٫٥", 12.5)]
    public async Task Arabic_digits_and_decimal_separators_are_understood(string typed, double expected)
    {
        var page = await OpenAsync();

        Type(page, S1, Manual, typed);

        page.WaitForAssertion(() => Assert.Equal((decimal)expected, _api.LastEntries!.Changes.Single().Points), Wait);
    }

    [Fact]
    public async Task Text_that_is_not_a_number_is_refused_before_any_call()
    {
        var page = await OpenAsync();

        Type(page, S0, Manual, "abc");

        page.WaitForAssertion(() => Assert.Contains("Enter a number", page.Find("[data-testid=grid-live-assertive]").TextContent), Wait);
        Assert.Equal(0, _api.UpsertCalls);
    }

    // ---- which cells can be edited ----

    [Fact]
    public async Task Event_scores_are_changed_through_an_override_not_typed_over()
    {
        var page = await OpenAsync();

        Press(Cell(page, S0, Auto), "Enter");

        Assert.Empty(page.FindAll("input.lms-gb-input"));
        Press(Cell(page, S0, Auto), "F10", shift: true);
        _popovers.WaitForElement("[data-testid=menu-override]", Wait);
    }

    [Fact]
    public async Task A_locked_gradebook_cannot_be_edited_or_given_a_menu()
    {
        _api.Locked = true;
        var page = await OpenAsync();

        Press(Cell(page, S0, Manual), "Enter");
        Press(Cell(page, S0, Manual), "F10", shift: true);

        Assert.Empty(page.FindAll("input.lms-gb-input"));
        Assert.Empty(_popovers.FindAll("[data-testid=cell-menu]"));
    }

    [Fact]
    public async Task A_viewer_without_Manage_cannot_edit()
    {
        _api.CanManage = false;
        var page = await OpenAsync(Permissions.Grades.View);

        Press(Cell(page, S0, Manual), "Enter");

        Assert.Empty(page.FindAll("input.lms-gb-input"));
    }

    // ---- override, excuse, clear, newer score ----

    [Fact]
    public async Task Override_requires_a_reason()
    {
        var page = await OpenAsync();
        Press(Cell(page, S0, Auto), "F10", shift: true);
        _popovers.WaitForElement("[data-testid=menu-override]", Wait).Click();
        _dialogs.WaitForElement("[data-testid=override-points]", Wait);
        _dialogs.Find("[data-testid=override-points]").Input("7");

        _dialogs.Find("[data-testid=dialog-save]").Click();
        Assert.NotNull(_dialogs.WaitForElement("[data-testid=override-error]", Wait));
        Assert.Contains("reason", _dialogs.Find("[data-testid=override-error]").TextContent);
        Assert.Equal(0, _api.UpsertCalls);

        _dialogs.Find("[data-testid=override-reason]").Input("Re-marked after appeal");
        _dialogs.Find("[data-testid=dialog-save]").Click();

        page.WaitForAssertion(() => Assert.Equal("7 of 10, overridden: Re-marked after appeal", Cell(page, S0, Auto).GetAttribute("aria-label")), Wait);
        var change = _api.LastEntries!.Changes.Single();
        Assert.Equal((GradeEntryActions.Override, 7m, "Re-marked after appeal"), (change.Action, change.Points, change.Reason));
    }

    [Fact]
    public async Task Override_refuses_a_score_above_the_maximum()
    {
        var page = await OpenAsync();
        Press(Cell(page, S0, Auto), "F10", shift: true);
        _popovers.WaitForElement("[data-testid=menu-override]", Wait).Click();
        _dialogs.WaitForElement("[data-testid=override-points]", Wait);
        _dialogs.Find("[data-testid=override-points]").Input("11");
        _dialogs.Find("[data-testid=override-reason]").Input("typo");

        _dialogs.Find("[data-testid=dialog-save]").Click();

        Assert.Contains("0 to 10", _dialogs.WaitForElement("[data-testid=override-error]", Wait).TextContent);
        Assert.Equal(0, _api.UpsertCalls);
    }

    [Fact]
    public async Task Typing_over_an_overridden_score_keeps_its_reason()
    {
        _api.SetCell(S0, new GradeCellDto(Manual, 17m, GradeCellStates.Override, "Re-marked"));
        var page = await OpenAsync();

        Type(page, S0, Manual, "16");

        page.WaitForAssertion(() => Assert.Equal("16 of 20, overridden: Re-marked", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);
        Assert.Equal(GradeEntryActions.Override, _api.LastEntries!.Changes.Single().Action);
    }

    [Fact]
    public async Task Excuse_from_this_item_and_clear_score_are_in_the_menu()
    {
        var page = await OpenAsync();
        Press(Cell(page, S0, Manual), "F10", shift: true);
        _popovers.WaitForElement("[data-testid=menu-excuse]", Wait).Click();
        page.WaitForAssertion(() => Assert.Equal("Excused", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);

        Press(Cell(page, S0, Auto), "F10", shift: true);
        _popovers.WaitForElement("[data-testid=menu-clear]", Wait).Click();

        page.WaitForAssertion(() => Assert.Equal("No score", Cell(page, S0, Auto).GetAttribute("aria-label")), Wait);
    }

    [Fact]
    public async Task Newer_score_keep_or_drop()
    {
        _api.SetCell(S1, new GradeCellDto(Manual, 5m, GradeCellStates.NewerScoreExists, null));
        var page = await OpenAsync();
        Press(Cell(page, S1, Manual), "F10", shift: true);
        Assert.NotNull(_popovers.WaitForElement("[data-testid=menu-drop]", Wait));

        _popovers.Find("[data-testid=menu-keep]").Click();

        page.WaitForAssertion(() => Assert.Equal(GradeEntryActions.KeepOverride, _api.LastEntries!.Changes.Single().Action), Wait);
    }

    [Fact]
    public async Task The_menu_offers_the_newer_score_choices_only_for_that_state()
    {
        var page = await OpenAsync();

        Press(Cell(page, S0, Manual), "F10", shift: true);

        _popovers.WaitForElement("[data-testid=menu-override]", Wait);
        Assert.Empty(_popovers.FindAll("[data-testid=menu-keep]"));
        Assert.Empty(_popovers.FindAll("[data-testid=menu-drop]"));
    }

    // ---- failures ----

    [Fact]
    public async Task Failed_save_reverts_and_shows_text()
    {
        _api.NextUpsertFailure = new ApiProblemException(500, null, null, null);
        var page = await OpenAsync();

        Type(page, S0, Manual, "12");

        page.WaitForAssertion(() => Assert.Contains("Not saved", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);
        Assert.Contains("Press Enter to try again", Cell(page, S0, Manual).GetAttribute("aria-label"));
        Assert.Contains("Not saved", Cell(page, S0, Manual).TextContent);
        Assert.Contains("Not saved", page.Find("[data-testid=grid-live-assertive]").TextContent);
        // The server still has the old score, and the cell is the only thing that changed.
        Assert.Equal(18m, (await _api.GetAsync(Cc, new GradebookQuery())).Rows.Items.First(r => r.StudentId == S0).Cells.First(c => c.ItemId == Manual).Points);
    }

    [Fact]
    public async Task Enter_on_a_not_saved_cell_tries_again_and_saves()
    {
        _api.NextUpsertFailure = new ApiProblemException(500, null, null, null);
        var page = await OpenAsync();
        Type(page, S0, Manual, "12");
        page.WaitForAssertion(() => Assert.Contains("Not saved", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);

        Press(Cell(page, S0, Manual), "Enter");

        page.WaitForAssertion(() => Assert.Equal("12 of 20", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);
        Assert.Equal(2, _api.UpsertCalls);
    }

    [Fact]
    public async Task A_server_refusal_shows_its_reason_in_words()
    {
        _api.NextUpsertFailure = new ApiProblemException(409, ProblemCodes.gradebook_locked, null, null);
        var page = await OpenAsync();

        Type(page, S0, Manual, "12");

        page.WaitForAssertion(() => Assert.Contains("locked", Cell(page, S0, Manual).GetAttribute("aria-label"), StringComparison.OrdinalIgnoreCase), Wait);
    }

    [Fact]
    public async Task Concurrency_conflict_reloads_the_row()
    {
        _api.ConflictOnce = true;
        var page = await OpenAsync();
        var loads = 0;
        _api.Transform = (book, _) => { loads++; return book; };

        Type(page, S0, Manual, "12");

        page.WaitForAssertion(() => Assert.Contains("changed by someone else", page.Find("[data-testid=grid-live-polite]").TextContent), Wait);
        Assert.True(loads >= 1, "the row (the page) was loaded again");
        Assert.Equal("18 of 20", Cell(page, S0, Manual).GetAttribute("aria-label")); // the server's value, not the typed one
        Type(page, S0, Manual, "12"); // with the fresh version the same edit now saves
        page.WaitForAssertion(() => Assert.Equal("12 of 20", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);
        Assert.Equal(2, _api.LastEntries!.Version);
    }

    // ---- the phone list and the breakpoint ----

    [Fact]
    public async Task The_phone_list_edits_a_manual_score_in_place()
    {
        _viewport.IsPhone = true;
        var page = await OpenAsync();

        var input = page.WaitForElement($"[data-testid=phone-edit-{Manual}]", Wait);
        input.Focus();
        input.Input("14");
        input.Change("14");

        page.WaitForAssertion(() => Assert.Equal(14m, _api.LastEntries!.Changes.Single().Points), Wait);
    }

    [Fact]
    public async Task The_phone_list_offers_an_override_for_an_event_score()
    {
        _viewport.IsPhone = true;
        var page = await OpenAsync();

        page.WaitForElement($"[data-testid=phone-override-{Auto}]", Wait).Click();

        _dialogs.WaitForElement("[data-testid=override-reason]", Wait);
    }

    [Fact]
    public async Task Edits_survive_crossing_600px()
    {
        var page = await OpenAsync();
        Press(Cell(page, S0, Manual), "Enter");
        page.Find($"[data-testid=edit-{S0}-{Manual}]").Input("13");

        _viewport.IsPhone = true; // the screen is rotated: the phone list replaces the grid
        await page.InvokeAsync(() => _viewport.Raise());

        var phone = page.WaitForElement($"[data-testid=phone-edit-{Manual}]", Wait);
        Assert.Equal("13", phone.GetAttribute("value")); // what was typed in the grid is still there
        _viewport.IsPhone = false;
        await page.InvokeAsync(() => _viewport.Raise());
        page.WaitForAssertion(() => Assert.Equal("13", page.Find($"[data-testid=edit-{S0}-{Manual}]").GetAttribute("value")), Wait);
    }

    [Fact]
    public async Task A_phone_edit_that_is_rejected_stays_in_the_field_with_its_reason()
    {
        _viewport.IsPhone = true;
        var page = await OpenAsync();
        var input = page.WaitForElement($"[data-testid=phone-edit-{Manual}]", Wait);
        input.Focus();
        input.Input("99");

        input.Change("99");

        page.WaitForAssertion(() => Assert.Contains("Out of range", page.Find("[data-testid=grid-live-assertive]").TextContent), Wait);
        Assert.Equal(0, _api.UpsertCalls);
    }

    // ---- parsing ----

    [Theory]
    [InlineData("12", true, 12)]
    [InlineData(" 7.25 ", true, 7.25)]
    [InlineData("٣", true, 3)]
    [InlineData("۴۵", true, 45)]
    [InlineData("1,5", true, 1.5)]
    [InlineData("", false, 0)]
    [InlineData("1e3", false, 0)]
    [InlineData("12abc", false, 0)]
    public void Parsing_accepts_plain_numbers_in_either_script(string text, bool ok, double expected)
    {
        Assert.Equal(ok, GradebookEditSession.TryParse(text, out var value));
        if (ok) Assert.Equal((decimal)expected, value);
    }

    // ---- RTL ----

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await OpenAsync();

        Type(page, S0, Manual, "15");

        page.WaitForAssertion(() => Assert.Equal("15 من 20", Cell(page, S0, Manual).GetAttribute("aria-label")), Wait);
        Assert.Contains("تم الحفظ", page.Find("[data-testid=grid-live-polite]").TextContent);
        Press(Cell(page, S0, Manual), "F10", shift: true);
        Assert.Contains("إعفاء", _popovers.WaitForElement("[data-testid=menu-excuse]", Wait).TextContent);
        Assert.DoesNotContain("Grd_", page.Markup);
    }
}
