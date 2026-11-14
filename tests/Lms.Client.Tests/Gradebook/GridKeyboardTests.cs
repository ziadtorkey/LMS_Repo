using System.Globalization;
using Bunit;
using Lms.Client.Components.Gradebook;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Services;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Gradebook;

/// <summary>The grid key map (L24, M5 Task 25): the pure map, and the grid driven by keys.</summary>
public sealed class GridKeyboardTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private static readonly Guid S0 = FakeGradebookApi.StudentIds[0];
    private static readonly Guid S1 = FakeGradebookApi.StudentIds[1];
    private static readonly Guid Manual = FakeGradebookApi.ManualItemId;
    private static readonly Guid Auto = FakeGradebookApi.AutoItemId;
    private readonly FakeGradebookApi _api = new();
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;

    // Columns of the demo gradebook: 0 Assignment 1 (an event score), 1 Homework result, 2 Quiz 1 (manual), 3 Exams result, 4 Final, 5 Band.
    private const int AutoCol = 0, ManualCol = 2, FinalCol = 4, LastCol = 5;

    public GridKeyboardTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IGradebookUnit>(new FakeGradebookUnit());
        Services.AddSingleton<IViewport>(new FakeViewport());
    }

    private async Task<IRenderedComponent<GradebookPage>> OpenAsync()
    {
        await GrantAsync(Permissions.Grades.View, Permissions.Grades.Manage);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        _popovers = Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForElement("[data-testid=gradebook-grid]", Wait);
        return page;
    }

    private static AngleSharp.Dom.IElement CellAt(IRenderedComponent<GradebookPage> page, int row, int col) => page.Find($"td[data-r='{row}'][data-c='{col}']");

    private static void Press(AngleSharp.Dom.IElement cell, string key, bool shift = false, bool ctrl = false) =>
        cell.KeyDown(new KeyboardEventArgs { Key = key, ShiftKey = shift, CtrlKey = ctrl });

    private static (int Row, int Col) Stop(IRenderedComponent<GradebookPage> page)
    {
        var stop = page.FindAll("td[role=gridcell][tabindex='0']").Single();
        return (int.Parse(stop.GetAttribute("data-r")!), int.Parse(stop.GetAttribute("data-c")!));
    }

    // ---- the pure key map ----

    [Theory]
    [InlineData("ArrowDown", 0, 0, 1, 0)]
    [InlineData("ArrowUp", 0, 0, 0, 0)]       // stops at the first row
    [InlineData("ArrowRight", 0, 0, 0, 1)]
    [InlineData("ArrowLeft", 0, 0, 0, 0)]     // stops at the first column
    [InlineData("ArrowDown", 2, 3, 2, 3)]     // stops at the last row
    [InlineData("Home", 1, 4, 1, 0)]
    [InlineData("End", 1, 1, 1, 5)]
    public void Arrows_home_and_end_move_focus_and_stop_at_the_edges(string key, int row, int col, int expectedRow, int expectedCol)
    {
        var r = GridKeyboard.Handle(key, false, false, false, row, col, 3, 6, false, false, false);

        Assert.Equal((expectedRow, expectedCol), (r.Row, r.Col));
        Assert.Equal(GridAction.Move, r.Action);
    }

    [Fact]
    public void Ctrl_Home_goes_to_the_first_cell()
    {
        var r = GridKeyboard.Handle("Home", false, true, false, 2, 4, 3, 6, false, false, false);

        Assert.Equal((0, 0), (r.Row, r.Col));
    }

    [Fact]
    public void In_a_right_to_left_page_the_horizontal_arrows_follow_the_reading_direction()
    {
        var right = GridKeyboard.Handle("ArrowRight", false, false, false, 0, 3, 3, 6, rtl: true, false, false);
        var left = GridKeyboard.Handle("ArrowLeft", false, false, false, 0, 3, 3, 6, rtl: true, false, false);

        Assert.Equal(2, right.Col); // visually right is the previous column when the page reads right to left
        Assert.Equal(4, left.Col);
    }

    [Fact]
    public void Enter_or_f2_toggles_edit_only_on_a_cell_that_can_be_edited()
    {
        Assert.Equal(GridAction.StartEdit, GridKeyboard.Handle("Enter", false, false, false, 0, 2, 3, 6, false, false, editable: true).Action);
        Assert.Equal(GridAction.StartEdit, GridKeyboard.Handle("F2", false, false, false, 0, 2, 3, 6, false, false, editable: true).Action);
        Assert.Equal(GridAction.None, GridKeyboard.Handle("Enter", false, false, false, 0, 0, 3, 6, false, false, editable: false).Action);
        Assert.Equal(GridAction.CommitStay, GridKeyboard.Handle("Enter", false, false, false, 0, 2, 3, 6, false, editing: true, true).Action);
        Assert.Equal(GridAction.CommitStay, GridKeyboard.Handle("F2", false, false, false, 0, 2, 3, 6, false, editing: true, true).Action); // F2 toggles
    }

    [Fact]
    public void Tab_moves_right_only_while_editing_and_otherwise_leaves_the_grid()
    {
        var editing = GridKeyboard.Handle("Tab", false, false, false, 0, 2, 3, 6, false, editing: true, true);
        var back = GridKeyboard.Handle("Tab", true, false, false, 0, 2, 3, 6, false, editing: true, true);
        var navigating = GridKeyboard.Handle("Tab", false, false, false, 0, 2, 3, 6, false, editing: false, false);

        Assert.Equal((GridAction.CommitMoveNext, 3), (editing.Action, editing.Col));
        Assert.Equal((GridAction.CommitMovePrevious, 1), (back.Action, back.Col));
        Assert.Equal(GridAction.None, navigating.Action);
        Assert.False(GridKeyboard.HandlesKey("Tab", false, editing: false)); // the page keeps the default: Tab leaves the grid
        Assert.True(GridKeyboard.HandlesKey("Tab", false, editing: true));
    }

    [Fact]
    public void Esc_cancels_and_the_menu_keys_open_the_menu()
    {
        Assert.Equal(GridAction.CancelEdit, GridKeyboard.Handle("Escape", false, false, false, 0, 2, 3, 6, false, true, true).Action);
        Assert.Equal(GridAction.OpenMenu, GridKeyboard.Handle("F10", true, false, false, 0, 2, 3, 6, false, false, false).Action);
        Assert.Equal(GridAction.OpenMenu, GridKeyboard.Handle("ContextMenu", false, false, false, 0, 2, 3, 6, false, false, false).Action);
        Assert.Equal(GridAction.None, GridKeyboard.Handle("F10", false, false, false, 0, 2, 3, 6, false, false, false).Action); // F10 alone is not the menu
    }

    [Fact]
    public void Alt_combinations_are_left_to_the_browser()
    {
        Assert.Equal(GridAction.None, GridKeyboard.Handle("ArrowLeft", false, false, alt: true, 0, 3, 3, 6, false, false, false).Action);
    }

    // ---- the grid driven by keys ----

    [Fact]
    public async Task Arrows_home_end_and_ctrl_home_move_the_one_tab_stop()
    {
        var page = await OpenAsync();
        Assert.Equal((0, 0), Stop(page));

        Press(CellAt(page, 0, 0), "ArrowRight");
        Assert.Equal((0, 1), Stop(page));
        Press(CellAt(page, 0, 1), "ArrowDown");
        Assert.Equal((1, 1), Stop(page));
        Press(CellAt(page, 1, 1), "End");
        Assert.Equal((1, LastCol), Stop(page));
        Press(CellAt(page, 1, LastCol), "Home", ctrl: true);
        Assert.Equal((0, 0), Stop(page));
        Press(CellAt(page, 0, 0), "Home");
        Assert.Equal((0, 0), Stop(page));
    }

    [Fact]
    public async Task Final_and_band_cells_are_reachable_by_keys_too()
    {
        var page = await OpenAsync();

        Press(CellAt(page, 0, 0), "End");
        Press(CellAt(page, 0, LastCol), "ArrowLeft");

        Assert.Equal((0, FinalCol), Stop(page));
    }

    [Fact]
    public async Task Enter_or_f2_toggles_edit()
    {
        var page = await OpenAsync();

        Press(CellAt(page, 0, ManualCol), "Enter");
        page.WaitForElement($"[data-testid=edit-{S0}-{Manual}]", Wait);
        Assert.Equal("edit", page.Find("[data-testid=gradebook-grid]").GetAttribute("data-mode"));
        Press(CellAt(page, 0, ManualCol), "Enter"); // saves (nothing changed) and leaves edit mode
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("input.lms-gb-input")), Wait);
        Press(CellAt(page, 0, ManualCol), "F2");
        page.WaitForElement($"[data-testid=edit-{S0}-{Manual}]", Wait);
    }

    [Fact]
    public async Task Enter_does_nothing_on_a_score_that_comes_from_an_event()
    {
        var page = await OpenAsync();

        Press(CellAt(page, 0, AutoCol), "Enter");

        Assert.Empty(page.FindAll("input.lms-gb-input"));
    }

    [Fact]
    public async Task Shift_f10_opens_the_cell_menu_and_the_context_menu_key_does_too()
    {
        var page = await OpenAsync();

        Press(CellAt(page, 0, ManualCol), "F10", shift: true);
        _popovers.WaitForElement("[data-testid=menu-override]", Wait);
        Press(CellAt(page, 0, ManualCol), "Escape"); // not editing: the menu has its own Esc (below)
        _popovers.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _popovers.WaitForAssertion(() => Assert.Empty(_popovers.FindAll("[data-testid=menu-override]")), Wait);
        Press(CellAt(page, 0, ManualCol), "ContextMenu");
        _popovers.WaitForElement("[data-testid=menu-excuse]", Wait);
    }

    [Fact]
    public async Task Tab_in_navigation_mode_changes_nothing_so_it_leaves_the_grid()
    {
        var page = await OpenAsync();

        Press(CellAt(page, 0, 0), "Tab");

        Assert.Equal((0, 0), Stop(page));
        Assert.Empty(page.FindAll("input.lms-gb-input"));
    }

    [Fact]
    public async Task Tab_while_editing_saves_and_moves_to_the_next_cell()
    {
        var page = await OpenAsync();
        Press(CellAt(page, 0, ManualCol), "Enter");
        page.Find($"[data-testid=edit-{S0}-{Manual}]").Input("15");

        Press(CellAt(page, 0, ManualCol), "Tab");

        page.WaitForAssertion(() => Assert.Equal((0, ManualCol + 1), Stop(page)), Wait);
        Assert.Empty(page.FindAll("input.lms-gb-input"));
        Assert.Equal(15m, _api.LastEntries!.Changes.Single().Points);
    }

    [Fact]
    public async Task Esc_cancels_the_edit_without_saving()
    {
        var page = await OpenAsync();
        Press(CellAt(page, 0, ManualCol), "Enter");
        page.Find($"[data-testid=edit-{S0}-{Manual}]").Input("3");

        Press(CellAt(page, 0, ManualCol), "Escape");

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("input.lms-gb-input")), Wait);
        Assert.Equal(0, _api.UpsertCalls);
        Assert.Equal("18 of 20", CellAt(page, 0, ManualCol).GetAttribute("aria-label"));
        Assert.Contains("cancelled", page.Find("[data-testid=grid-live-polite]").TextContent);
    }

    [Fact]
    public async Task Edit_mode_is_announced_and_hinted()
    {
        var page = await OpenAsync();
        Assert.Contains("Enter or F2 edits, Enter again saves", page.Find("[data-testid=gradebook-hint]").TextContent);
        Assert.Equal("gb-hint", page.Find("[data-testid=gradebook-grid]").GetAttribute("aria-describedby"));

        Press(CellAt(page, 0, ManualCol), "Enter");

        page.WaitForAssertion(() => Assert.Contains("Editing Quiz 1 for Sara Ahmed", page.Find("[data-testid=grid-live-polite]").TextContent), Wait);
        Assert.Contains("Esc cancels", page.Find("[data-testid=gradebook-hint]").TextContent);
        Assert.Equal("Sara Ahmed, Quiz 1", page.Find($"[data-testid=edit-{S0}-{Manual}]").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Rejected_value_is_announced_assertively_once()
    {
        var page = await OpenAsync();
        Press(CellAt(page, 0, ManualCol), "Enter");
        page.Find($"[data-testid=edit-{S0}-{Manual}]").Input("99");

        Press(CellAt(page, 0, ManualCol), "Enter");

        page.WaitForAssertion(() => Assert.Contains("Out of range", page.Find("[data-testid=grid-live-assertive]").TextContent), Wait);
        Assert.Equal("alert", page.Find("[data-testid=grid-live-assertive]").GetAttribute("role"));
        Assert.Equal(0, _api.UpsertCalls); // refused before any call
        Assert.NotEmpty(page.FindAll("input.lms-gb-input")); // still in the cell, so it can be fixed
        var message = page.Find("[data-testid=grid-live-assertive]").TextContent;
        Press(CellAt(page, 0, ManualCol), "ArrowDown"); // an unrelated render (not editing keys): the text is not announced again
        Assert.Equal(message, page.Find("[data-testid=grid-live-assertive]").TextContent);
    }

    [Fact]
    public async Task Focused_cell_is_not_obscured_by_sticky_headers()
    {
        // Layout is not computed in bUnit: this guards the two things that keep a focused cell clear of the sticky header rows and the student column,
        // in both reading directions (WCAG 2.4.11): logical scroll-padding on the wrapper, and a scroll into view that respects it.
        var root = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(root);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lms.Client"))) dir = dir.Parent;
        var css = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "src", "Lms.Client", "wwwroot", "css", "app.css"));
        var js = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "src", "Lms.Client", "wwwroot", "js", "gradebook-grid.js"));

        Assert.Contains("scroll-padding-block-start", css);
        Assert.Contains("scroll-padding-inline-start", css); // logical: the student column is at the start in LTR and RTL alike
        Assert.DoesNotContain("scroll-padding-left", css);
        Assert.Contains("scrollIntoView({ block: 'nearest', inline: 'nearest' })", js);
        Assert.Contains("preventScroll: true", js); // the scroll is done once, with the padding, not by the browser's default focus scroll
        var page = await OpenAsync();
        Assert.NotNull(page.Find(".lms-gb-wrap"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await OpenAsync();

        Press(CellAt(page, 0, 3), "ArrowRight"); // visually right = the previous column in an Arabic page

        Assert.Equal((0, 2), Stop(page));
        Assert.Contains("F2", page.Find("[data-testid=gradebook-hint]").TextContent);
        Assert.Contains("للتعديل", page.Find("[data-testid=gradebook-hint]").TextContent);
        Press(CellAt(page, 0, 2), "Enter");
        page.WaitForAssertion(() => Assert.Contains("تعديل", page.Find("[data-testid=grid-live-polite]").TextContent), Wait);
        Assert.DoesNotContain("Grd_", page.Markup);
    }
}
