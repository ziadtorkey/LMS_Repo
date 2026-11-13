using System.Globalization;
using Bunit;
using Lms.Client.Components.Timetable;
using Lms.Client.Services.Timetable;
using Lms.Client.Tests.Support;

namespace Lms.Client.Tests.Timetable;

/// <summary>Spike S3 (M5 Task 31): the move interaction of the timetable grid, with mock data. The JS module only forwards keys and drops.</summary>
public sealed class TimetableGridSpikeTests : ClientTestContext
{
    private static readonly Guid Maths = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const int Sunday = (int)DayOfWeek.Sunday;
    private const int Monday = (int)DayOfWeek.Monday;

    private IRenderedComponent<TimetableGrid> RenderGrid(SpikeTimetable timetable, string culture = "en")
    {
        FakeCultureService.Apply(new CultureInfo(culture));
        Culture.Current = new CultureInfo(culture);
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<TimetableGrid>(p => p.Add(x => x.Timetable, timetable));
    }

    private static Task Key(IRenderedComponent<TimetableGrid> cut, string key, int day, int period) =>
        cut.InvokeAsync(() => cut.Instance.OnKey(key, day, period));

    [Fact]
    public void Days_run_in_reading_order_and_times_stay_left_to_right_in_arabic()
    {
        var cut = RenderGrid(new SpikeTimetable(), "ar");

        Assert.Equal("rtl", cut.Find("[data-testid=tt-grid]").GetAttribute("dir")); // the first day column is on the right
        var heads = cut.FindAll("thead th[data-testid]").Select(h => h.GetAttribute("data-testid")).ToList();
        Assert.Equal(["tt-day-0", "tt-day-1", "tt-day-2", "tt-day-3", "tt-day-4"], heads); // logical order, the dir mirrors it
        Assert.All(cut.FindAll(".lms-tt-time"), t => Assert.Equal("ltr", t.GetAttribute("dir")));
    }

    [Fact]
    public async Task Keyboard_move_equals_menu_move()
    {
        var byKeyboard = new SpikeTimetable();
        var keys = RenderGrid(byKeyboard);
        await Key(keys, "M", Sunday, 1); // pick up Mathematics
        await Key(keys, "ArrowRight", Sunday, 1);
        await Key(keys, "ArrowDown", Sunday, 1);
        await Key(keys, "ArrowDown", Sunday, 1);
        await Key(keys, "Enter", Sunday, 1);
        await DisposeComponentsAsync();

        var byMenu = new SpikeTimetable();
        var menu = RenderGrid(byMenu);
        await menu.InvokeAsync(() => menu.Instance.MoveToAsync(Maths, Monday, 3));

        Assert.Equal(byMenu.Find(Maths), byKeyboard.Find(Maths));
        Assert.Equal((Monday, 3), (byKeyboard.Find(Maths)!.Day, byKeyboard.Find(Maths)!.Period));
    }

    [Fact]
    public async Task Arrows_follow_what_the_user_sees_in_both_directions()
    {
        var ltr = RenderGrid(new SpikeTimetable());
        await Key(ltr, "M", Sunday, 1);
        await Key(ltr, "ArrowRight", Sunday, 1); // visually right = the next day in English
        await Key(ltr, "ArrowDown", Sunday, 1); // Monday periods 1 and 2 are taken (a lesson, a teacher clash): go down to a free period
        await Key(ltr, "ArrowDown", Sunday, 1);
        await Key(ltr, "Enter", Sunday, 1);
        Assert.Equal(Monday, ltr.Instance.Timetable.Find(Maths)!.Day);
        await DisposeComponentsAsync();

        var rtl = RenderGrid(new SpikeTimetable(), "ar");
        await Key(rtl, "M", Sunday, 1);
        await Key(rtl, "ArrowLeft", Sunday, 1); // visually left = the next day in Arabic (the week runs right to left)
        await Key(rtl, "ArrowDown", Sunday, 1);
        await Key(rtl, "ArrowDown", Sunday, 1);
        await Key(rtl, "Enter", Sunday, 1);
        Assert.Equal(Monday, rtl.Instance.Timetable.Find(Maths)!.Day);
    }

    [Fact]
    public async Task Escape_cancels_a_pickup_and_nothing_moves()
    {
        var timetable = new SpikeTimetable();
        var cut = RenderGrid(timetable);
        await Key(cut, "M", Sunday, 1);
        await Key(cut, "ArrowDown", Sunday, 1);
        await Key(cut, "Escape", Sunday, 1);

        Assert.Equal((Sunday, 1), (timetable.Find(Maths)!.Day, timetable.Find(Maths)!.Period));
        Assert.Contains("cancelled", cut.Find("[data-testid=tt-live]").TextContent);
    }

    [Fact]
    public async Task Block_mode_refuses_a_clash_and_says_which_lesson_is_in_the_way()
    {
        var timetable = new SpikeTimetable(blockOnConflict: true);
        var cut = RenderGrid(timetable);

        await cut.InvokeAsync(() => cut.Instance.MoveToAsync(Maths, Sunday, 2)); // Arabic is there

        Assert.Equal((Sunday, 1), (timetable.Find(Maths)!.Day, timetable.Find(Maths)!.Period)); // unchanged
        Assert.Contains("Arabic", cut.Find("[data-testid=tt-conflict]").TextContent);
        Assert.Contains("lms-tt-clash", cut.Find("[data-testid=tt-cell-0-2]").ClassName);
        Assert.Empty(cut.FindAll("[data-testid^=tt-warn-]"));
    }

    [Fact]
    public async Task Block_mode_refuses_a_teacher_clash_with_another_class()
    {
        var cut = RenderGrid(new SpikeTimetable(blockOnConflict: true));

        await cut.InvokeAsync(() => cut.Instance.MoveToAsync(Maths, Monday, 2)); // A. Saleh teaches class 5B then

        Assert.Contains("A. Saleh", cut.Find("[data-testid=tt-conflict]").TextContent);
        Assert.Equal(Sunday, cut.Instance.Timetable.Find(Maths)!.Day);
    }

    [Fact]
    public async Task Warn_mode_saves_the_move_and_shows_a_clash_chip_with_text()
    {
        var timetable = new SpikeTimetable(blockOnConflict: false);
        var cut = RenderGrid(timetable);

        await cut.InvokeAsync(() => cut.Instance.MoveToAsync(Maths, Monday, 2));

        Assert.Equal((Monday, 2), (timetable.Find(Maths)!.Day, timetable.Find(Maths)!.Period)); // saved
        Assert.Contains("Clash", cut.Find($"[data-testid=tt-warn-{Maths}]").TextContent);
    }

    [Fact]
    public async Task A_drop_from_the_pointer_uses_the_same_move_path()
    {
        var timetable = new SpikeTimetable();
        var cut = RenderGrid(timetable);

        await cut.InvokeAsync(() => cut.Instance.OnDrop(Maths.ToString(), (int)DayOfWeek.Thursday, 5));

        Assert.Equal(((int)DayOfWeek.Thursday, 5), (timetable.Find(Maths)!.Day, timetable.Find(Maths)!.Period));
        Assert.Contains("moved", cut.Find("[data-testid=tt-live]").TextContent);
    }

    [Fact]
    public void Every_lesson_has_a_visible_move_button_and_the_room_is_plain_text()
    {
        var cut = RenderGrid(new SpikeTimetable());

        Assert.Contains("Mathematics", cut.Find($"[data-testid=tt-menu-{Maths}]").GetAttribute("aria-label"));
        Assert.Equal("101", cut.Find($"[data-testid=tt-room-{Maths}]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=tt-room-00000000-0000-0000-0000-000000000004]")); // no room, no placeholder
    }

    [Fact]
    public void The_menu_opens_from_the_button()
    {
        var cut = RenderGrid(new SpikeTimetable());

        cut.Find($"[data-testid=tt-menu-{Maths}]").Click();

        Assert.NotNull(cut.Find("[data-testid=tt-move-menu]"));
        cut.Find("[data-testid=tt-move-cancel]").Click();
        Assert.Empty(cut.FindAll("[data-testid=tt-move-menu]"));
    }

    [Fact]
    public async Task Only_the_focused_cell_is_in_the_tab_order()
    {
        var cut = RenderGrid(new SpikeTimetable());
        await Key(cut, "ArrowDown", Sunday, 1);

        var tabbable = cut.FindAll("td[data-cell]").Where(c => c.GetAttribute("tabindex") == "0").ToList();
        Assert.Single(tabbable);
        Assert.Equal("tt-cell-0-2", tabbable[0].GetAttribute("data-testid"));
    }
}
