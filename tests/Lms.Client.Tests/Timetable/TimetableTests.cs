using System.Globalization;
using Bunit;
using Lms.Client.Components.Timetable;
using Lms.Client.Pages.Timetable;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Timetable;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Teaching;
using Lms.Shared.Timetable;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Timetable;

/// <summary>A year store with two terms (the current one and an earlier one) for the timetable pages.</summary>
public sealed class TermsYearStore : IYearStore
{
    public static readonly Guid EarlierTerm = Guid.Parse("e0000000-0000-0000-0000-000000000009");
    private readonly AcademicYearDto _year = new(Guid.NewGuid(), FakeTimetableApi.UnitId, "مدرسة", "School", "2026/2027", "2026/2027", new DateOnly(2026, 8, 1), new DateOnly(2027, 6, 30),
        AcademicYearStatuses.Active, false,
        [new TermDto(EarlierTerm, "الأول", "First", 1, new DateOnly(2026, 8, 1), new DateOnly(2026, 11, 1), false),
         new TermDto(FakeTimetableApi.TermId, "الثاني", "Second", 2, new DateOnly(2026, 11, 2), new DateOnly(2027, 3, 1), true)]);

    public Guid? SelectedYearId => _year.Id;
    public AcademicYearDto? Selected => _year;
    public IReadOnlyList<AcademicYearDto> Years => [_year];
    public bool IsReadOnly => false;
    public event Action? Changed { add { } remove { } }
    public Task LoadAsync(Guid? orgUnitId = null) => Task.CompletedTask;
    public Task SelectAsync(Guid yearId) => Task.CompletedTask;
}

public abstract class TimetableTestContext : StaffTestContext
{
    protected FakeTimetableApi Api2 { get; } = new();
    protected Lms.Client.Services.LiveSessions.FakeLiveSessionsApi LiveApi { get; } = new();

    protected TimetableTestContext()
    {
        Services.AddSingleton<ITimetableApi>(Api2);
        Services.AddSingleton<Lms.Client.Services.LiveSessions.ILiveSessionsApi>(LiveApi);
        Services.AddSingleton<IYearStore, TermsYearStore>();
        Services.AddSingleton<IClassCoursesApi>(new FakeCourses());
    }

    private sealed class FakeCourses : IClassCoursesApi
    {
        private static readonly ClassCourseTeacherDto Saleh = new(Guid.NewGuid(), FakeTimetableApi.Saleh, "أ. صالح", "A. Saleh", "Teacher", new DateOnly(2026, 8, 1), null, true);
        private static readonly ClassCourseTeacherDto Omar = new(Guid.NewGuid(), FakeTimetableApi.Omar, "ل. عمر", "L. Omar", "Teacher", new DateOnly(2026, 8, 1), null, true);

        public Task<IReadOnlyList<ClassCourseDto>> ListForClassAsync(Guid classId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ClassCourseDto>>(
        [
            new(FakeTimetableApi.Maths, classId, "5أ", "5A", Guid.NewGuid(), "MATH", "الرياضيات", "Mathematics", false, 20, [Saleh], null),
            new(FakeTimetableApi.Science, classId, "5أ", "5A", Guid.NewGuid(), "SCI", "العلوم", "Science", false, 20, [Omar], null),
        ]);

        public Task AddAsync(Guid classId, AddClassCoursesRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Guid classCourseId, UpdateClassCourseRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid classCourseId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<BulkAssignResultDto> BulkAssignAsync(BulkAssignCourseRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DistributionMatrixDto> GetMatrixAsync(Guid orgUnitId, Guid academicYearId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

public sealed class TimetableGridTests : TimetableTestContext
{
    private static readonly Guid P1 = FakeTimetableApi.PeriodId(1);
    private static readonly Guid P3 = FakeTimetableApi.PeriodId(3);
    private static readonly Guid P4 = FakeTimetableApi.PeriodId(4);
    private readonly List<SlotMove> _moves = [];

    private async Task<IRenderedComponent<TimetableGrid>> RenderAsync(string culture = "en", bool manage = true, ISet<Guid>? clash = null)
    {
        FakeCultureService.Apply(new CultureInfo(culture));
        Culture.Current = new CultureInfo(culture);
        var timetable = await Api2.GetClassTimetableAsync(FakeTimetableApi.ClassId, FakeTimetableApi.TermId);
        return Render<TimetableGrid>(p => p
            .Add(x => x.Timetable, timetable)
            .Add(x => x.CanManage, manage)
            .Add(x => x.ClashSlotIds, clash ?? new HashSet<Guid>())
            .Add(x => x.OnMove, (SlotMove m) => { _moves.Add(m); return Task.CompletedTask; }));
    }

    private static Task Key(IRenderedComponent<TimetableGrid> cut, string key, int day, Guid period, string code = "") =>
        cut.InvokeAsync(() => cut.Instance.OnKey(key, code, day, period.ToString()));

    [Fact]
    public async Task Grid_mirrors_days_in_rtl_and_keeps_times_ltr()
    {
        var cut = await RenderAsync("ar");

        Assert.Equal("rtl", cut.Find("[data-testid=tt-grid]").GetAttribute("dir")); // the first working day is on the right
        Assert.Equal(["tt-day-0", "tt-day-1", "tt-day-2", "tt-day-3", "tt-day-4"], cut.FindAll("thead th[data-testid]").Select(h => h.GetAttribute("data-testid")).ToList());
        Assert.All(cut.FindAll(".lms-tt-time"), t => Assert.Equal("ltr", t.GetAttribute("dir")));
    }

    [Fact]
    public async Task Keyboard_move_equals_menu_move()
    {
        var keys = await RenderAsync();
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        await Key(keys, "M", (int)DayOfWeek.Sunday, P1, "KeyM");
        await Key(keys, "ArrowRight", 0, P1);
        await Key(keys, "ArrowDown", 0, P1);
        await Key(keys, "ArrowDown", 0, P1);
        await Key(keys, "ArrowDown", 0, P1);
        await Key(keys, "Enter", 0, P1);
        var viaKeys = Assert.Single(_moves);
        await DisposeComponentsAsync();
        _moves.Clear();

        var menu = await RenderAsync();
        menu.Find($"[data-testid=tt-menu-{maths.Id}]").Click();
        await menu.InvokeAsync(() => menu.Instance.DropAsync(maths, DayOfWeek.Monday, P4));
        var viaMenu = Assert.Single(_moves);

        Assert.Equal((viaKeys.Slot.Id, viaKeys.Day, viaKeys.PeriodId), (viaMenu.Slot.Id, viaMenu.Day, viaMenu.PeriodId)); // the same move, whichever way
        Assert.Equal((DayOfWeek.Monday, P4), (viaKeys.Day, viaKeys.PeriodId));
    }

    [Fact]
    public async Task The_pick_up_key_is_matched_by_the_physical_key_so_an_Arabic_layout_works()
    {
        var cut = await RenderAsync("ar");

        await Key(cut, "ئ", 0, P1, "KeyM"); // the character an Arabic layout produces for the physical M key
        await Key(cut, "ArrowDown", 0, P1);
        await Key(cut, "Enter", 0, P1);

        Assert.Equal(FakeTimetableApi.PeriodId(2), Assert.Single(_moves).PeriodId); // picked up Mathematics and dropped it one period down
    }

    [Fact]
    public async Task Arrows_follow_what_the_user_sees_in_both_directions()
    {
        var ltr = await RenderAsync();
        await Key(ltr, "M", 0, P1, "KeyM");
        await Key(ltr, "ArrowRight", 0, P1); // visually right = the next day in English
        await Key(ltr, "ArrowDown", 0, P1);
        await Key(ltr, "ArrowDown", 0, P1);
        await Key(ltr, "ArrowDown", 0, P1);
        await Key(ltr, "Enter", 0, P1);
        Assert.Equal(DayOfWeek.Monday, _moves[0].Day);
        await DisposeComponentsAsync();

        var rtl = await RenderAsync("ar");
        await Key(rtl, "M", 0, P1, "KeyM");
        await Key(rtl, "ArrowLeft", 0, P1); // visually left = the next day in Arabic
        await Key(rtl, "ArrowDown", 0, P1);
        await Key(rtl, "ArrowDown", 0, P1);
        await Key(rtl, "ArrowDown", 0, P1);
        await Key(rtl, "Enter", 0, P1);
        Assert.Equal(DayOfWeek.Monday, _moves[1].Day);
    }

    [Fact]
    public async Task Escape_cancels_a_pickup_and_nothing_is_sent()
    {
        var cut = await RenderAsync();

        await Key(cut, "M", 0, P1, "KeyM");
        await Key(cut, "Escape", 0, P1);

        Assert.Empty(_moves);
        Assert.Contains("cancelled", cut.Find("[data-testid=tt-live]").TextContent);
    }

    [Fact]
    public async Task A_drop_from_the_pointer_uses_the_same_move_path()
    {
        var cut = await RenderAsync();
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);

        await cut.InvokeAsync(() => cut.Instance.OnDrop(maths.Id.ToString(), (int)DayOfWeek.Thursday, P3.ToString()));

        Assert.Equal((DayOfWeek.Thursday, P3), (Assert.Single(_moves).Day, _moves[0].PeriodId));
    }

    [Fact]
    public async Task Without_Timetable_Manage_the_grid_is_read_only()
    {
        var cut = await RenderAsync(manage: false);
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);

        Assert.Empty(cut.FindAll("[data-menu], [data-add]")); // no menu, no add
        await Key(cut, "M", 0, P1, "KeyM");
        await cut.InvokeAsync(() => cut.Instance.OnDrop(maths.Id.ToString(), 1, P3.ToString()));
        Assert.Empty(_moves);
    }

    [Fact]
    public async Task Move_and_add_controls_are_44_px_targets_and_the_keyboard_hint_is_described_by_the_grid()
    {
        var cut = await RenderAsync();

        Assert.All(cut.FindAll("button[data-menu], button[data-add]"), b => Assert.Contains("lms-touch", b.ClassName)); // 44 px by CSS
        Assert.Equal("tt-hint", cut.Find("[data-testid=tt-grid]").GetAttribute("aria-describedby"));
        Assert.Contains("M or Enter", cut.Find("#tt-hint").TextContent);
    }

    [Fact]
    public async Task Clashing_lessons_and_draft_lessons_are_marked_with_text()
    {
        var science = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Science);
        var cut = await RenderAsync(clash: new HashSet<Guid> { science.Id });

        Assert.Contains("lms-tt-clash", cut.Find($"[data-testid=tt-cell-1-1]").ClassName);
        Assert.Contains("Draft", cut.Find($"[data-testid=tt-draft-{science.Id}]").TextContent); // a draft has a word, not only a style
    }

    [Fact]
    public async Task The_room_is_plain_text_and_absent_when_empty()
    {
        var cut = await RenderAsync();
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        var science = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Science);

        Assert.Equal("101", cut.Find($"[data-testid=tt-room-{maths.Id}]").TextContent.Trim());
        Assert.Empty(cut.FindAll($"[data-testid=tt-room-{science.Id}]"));
    }
}

public sealed class TimetableBuilderPageTests : TimetableTestContext
{
    private async Task<IRenderedComponent<TimetableBuilderPage>> RenderAsync(string culture = "en", params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions.Length == 0 ? [Permissions.Timetable.View, Permissions.Timetable.Manage] : permissions);
        RenderProviders();
        var page = Render<TimetableBuilderPage>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));
        page.WaitForElement("[data-testid=tt-grid]");
        return page;
    }

    private SlotDto Maths => Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);

    [Fact]
    public async Task Block_mode_refusal_highlights_the_clash_with_text()
    {
        var page = await RenderAsync();

        await page.InvokeAsync(() => page.FindComponent<TimetableGrid>().Instance.DropAsync(Maths, DayOfWeek.Sunday, FakeTimetableApi.PeriodId(2))); // Arabic is there

        page.WaitForAssertion(() => Assert.Contains("Arabic", page.Find("[data-testid=tt-conflict]").TextContent)); // the server's clash, in words
        Assert.Contains("lms-tt-clash", page.Find("[data-testid=tt-cell-0-2]").ClassName);
        Assert.Equal((DayOfWeek.Sunday, FakeTimetableApi.PeriodId(1)), (Maths.DayOfWeek, Maths.PeriodId)); // not saved
    }

    [Fact]
    public async Task Block_mode_refuses_a_teacher_clash_with_another_class()
    {
        var page = await RenderAsync();

        await page.InvokeAsync(() => page.FindComponent<TimetableGrid>().Instance.DropAsync(Maths, DayOfWeek.Monday, FakeTimetableApi.PeriodId(2))); // A. Saleh teaches class 5B then

        page.WaitForAssertion(() => Assert.Contains("A. Saleh", page.Find("[data-testid=tt-conflict]").TextContent));
    }

    [Fact]
    public async Task Warn_mode_saves_and_shows_chips_on_both_cards()
    {
        Api2.ConflictMode = ConflictModes.Warn;
        var page = await RenderAsync();

        await page.InvokeAsync(() => page.FindComponent<TimetableGrid>().Instance.DropAsync(Maths, DayOfWeek.Sunday, FakeTimetableApi.PeriodId(2)));

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid^=tt-warn-]").Count)); // the moved lesson and the one it clashes with, both with the word Clash
        Assert.Equal(FakeTimetableApi.PeriodId(2), Api2.Slots.First(s => s.Id == Maths.Id).PeriodId); // saved
        Assert.Contains("Clash", page.Find("[data-testid^=tt-warn-]").TextContent);
    }

    [Fact]
    public async Task A_successful_move_is_saved_and_announced()
    {
        var page = await RenderAsync();

        await page.InvokeAsync(() => page.FindComponent<TimetableGrid>().Instance.DropAsync(Maths, DayOfWeek.Wednesday, FakeTimetableApi.PeriodId(5)));

        page.WaitForAssertion(() => Assert.Equal(DayOfWeek.Wednesday, Api2.Slots.First(s => s.Id == Maths.Id).DayOfWeek));
        Assert.True(page.FindAll("[data-testid=tt-conflict]").Count == 0, string.Join("|", page.FindAll("[data-testid=tt-conflict]").Select(e => e.TextContent)));
        Assert.Contains("moved", page.Find("[data-testid=tt-live]").TextContent);
    }

    [Fact]
    public async Task Publish_says_sessions_will_be_generated_and_publishes()
    {
        var page = await RenderAsync();
        Assert.Contains("1 draft", page.Find("[data-testid=tt-state]").TextContent);

        page.Find("[data-testid=tt-publish]").Click();

        Assert.Contains("attendance sessions", Dialogs.WaitForElement("[data-testid=publish-message]").TextContent);
        Dialogs.Find("[data-testid=publish-confirm]").Click();
        page.WaitForAssertion(() => Assert.Equal(1, Api2.PublishCalls));
        page.WaitForAssertion(() => Assert.Contains("All published", page.Find("[data-testid=tt-state]").TextContent));
        Assert.True(page.Find("[data-testid=tt-publish]").HasAttribute("disabled")); // nothing left to publish
    }

    [Fact]
    public async Task A_publish_refused_in_Block_mode_shows_the_clashes_and_keeps_the_timetable_unpublished()
    {
        Api2.ConflictMode = ConflictModes.Warn; // build a clash in Warn mode, then the school switches to Block
        var page = await RenderAsync();
        await page.InvokeAsync(() => page.FindComponent<TimetableGrid>().Instance.DropAsync(Maths, DayOfWeek.Sunday, FakeTimetableApi.PeriodId(2)));
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid^=tt-warn-]").Count));
        Api2.ConflictMode = ConflictModes.Block;

        page.Find("[data-testid=tt-publish]").Click();
        Dialogs.WaitForElement("[data-testid=publish-confirm]").Click();

        page.WaitForElement("[data-testid=tt-conflicts-list]"); // the server's clashes, one sentence each
        Assert.Contains("Arabic", page.Find("[data-testid=tt-conflicts-list]").TextContent);
        Assert.Contains("Nothing was published", page.Find("[data-testid=tt-conflict]").TextContent);
        Assert.Contains(Api2.Slots, s => s.State == SlotStates.Draft); // the drafts are still drafts
        Assert.Contains("draft", page.Find("[data-testid=tt-state]").TextContent);
        Assert.False(page.Find("[data-testid=tt-publish]").HasAttribute("disabled")); // and can be published once fixed
    }

    [Fact]
    public async Task Copy_term_lists_skipped_rows()
    {
        var page = await RenderAsync();
        page.Find("[data-testid=tt-copy]").Click();
        Dialogs.WaitForElement("[data-testid=copy-source]");
        var source = Dialogs.FindComponents<MudSelect<Guid?>>().Single();
        await Dialogs.InvokeAsync(() => source.Instance.ValueChanged.InvokeAsync(TermsYearStore.EarlierTerm));

        Dialogs.Find("[data-testid=copy-run]").Click();

        Dialogs.WaitForElement("[data-testid=copy-done]");
        Assert.Equal((TermsYearStore.EarlierTerm, FakeTimetableApi.TermId), Api2.LastCopy);
        Assert.Contains("Science", Dialogs.Find("[data-testid=copy-skipped]").TextContent);
        Assert.Contains("teacher is no longer available", Dialogs.Find("[data-testid=copy-skipped]").TextContent); // the reason in words
    }

    [Fact]
    public async Task Adding_a_lesson_takes_a_course_a_teacher_and_a_free_text_room()
    {
        var page = await RenderAsync();

        page.Find("[data-testid=tt-add-2-5]").Click(); // an empty Tuesday, period 5
        Dialogs.WaitForElement("[data-testid=slot-save]");
        await Dialogs.InvokeAsync(() => Dialogs.FindComponents<MudSelect<Guid?>>().First(s => (string?)s.Instance.UserAttributes["data-testid"] == "slot-course").Instance.ValueChanged.InvokeAsync(FakeTimetableApi.Science));
        Dialogs.Find("input[data-testid=slot-room]").Change("Lab 2 (new wing)");
        Dialogs.Find("[data-testid=slot-save]").Click();

        page.WaitForAssertion(() => Assert.Contains(Api2.Slots, s => s.ClassCourseId == FakeTimetableApi.Science && s.DayOfWeek == DayOfWeek.Tuesday && s.RoomName == "Lab 2 (new wing)"));
    }

    [Fact]
    public async Task Validate_lists_the_servers_conflicts_or_says_there_are_none()
    {
        Api2.ConflictMode = ConflictModes.Warn;
        var page = await RenderAsync();

        page.Find("[data-testid=tt-validate]").Click();
        page.WaitForElement("[data-testid=tt-conflicts-none]");
        await page.InvokeAsync(() => page.FindComponent<TimetableGrid>().Instance.DropAsync(Maths, DayOfWeek.Sunday, FakeTimetableApi.PeriodId(2)));
        page.Find("[data-testid=tt-validate]").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=tt-conflicts-list]")));
    }

    [Fact]
    public async Task Without_Timetable_Manage_there_are_no_authoring_actions()
    {
        var page = await RenderAsync("en", Permissions.Timetable.View);

        Assert.Empty(page.FindAll("[data-testid=tt-publish], [data-testid=tt-copy], [data-testid=tt-validate]"));
        Assert.NotNull(page.Find("[data-testid=tt-export]")); // reading and exporting stay
    }

    [Fact]
    public async Task Without_Timetable_View_the_page_is_forbidden()
    {
        await GrantAsync(Permissions.Staff.View); // a role without any timetable permission
        RenderProviders();

        var page = Render<TimetableBuilderPage>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=tt-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync("ar");

        Assert.Equal("الجدول الدراسي", page.Find("[data-testid=tt-title]").TextContent.Trim());
        Assert.Equal("rtl", page.Find("[data-testid=tt-grid]").GetAttribute("dir"));
    }
}

public sealed class TimetableSetupPageTests : TimetableTestContext
{
    private async Task<IRenderedComponent<TimetableSetupPage>> RenderAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Timetable.Configure);
        RenderProviders();
        var page = Render<TimetableSetupPage>(p => p.Add(x => x.UnitId, FakeTimetableApi.UnitId));
        page.WaitForElement("[data-testid=periods-table]");
        return page;
    }

    [Fact]
    public async Task Periods_are_listed_in_order_with_times_ltr()
    {
        var page = await RenderAsync();

        Assert.Equal(7, page.FindAll("[data-testid^=period-row-]").Count);
        Assert.All(page.FindAll("td[data-label] span[dir=ltr]"), s => Assert.Matches(@"^\d\d:\d\d$", s.TextContent.Trim()));
        Assert.Contains("unit setting", page.Find("[data-testid=setup-settings-note]").TextContent);
    }

    [Fact]
    public async Task Periods_validate_order_and_overlap()
    {
        var overlapping = new SavePeriodRequest("ح", "P", new TimeOnly(7, 40), new TimeOnly(8, 10), 8); // overlaps period 1 (07:30-08:15)
        var backwards = new SavePeriodRequest("ح", "P", new TimeOnly(9, 0), new TimeOnly(8, 0), 8);

        Assert.Equal("period_overlap", (await Assert.ThrowsAsync<ApiProblemException>(() => Api2.CreatePeriodAsync(FakeTimetableApi.UnitId, overlapping))).Code);
        Assert.Equal("period_overlap", (await Assert.ThrowsAsync<ApiProblemException>(() => Api2.CreatePeriodAsync(FakeTimetableApi.UnitId, backwards))).Code);
        var fine = await Api2.CreatePeriodAsync(FakeTimetableApi.UnitId, new SavePeriodRequest("ح", "P8", new TimeOnly(15, 0), new TimeOnly(15, 45), 8));
        Assert.Equal(8, fine.Order);
    }

    [Fact]
    public async Task A_period_a_lesson_uses_cannot_be_deleted()
    {
        var page = await RenderAsync();

        page.Find($"[data-testid=period-delete-{FakeTimetableApi.PeriodId(1)}]").Click(); // Mathematics is in period 1
        Dialogs.WaitForElement("[data-testid=confirm-yes]").Click();

        page.WaitForAssertion(() => Assert.Equal(7, page.FindAll("[data-testid^=period-row-]").Count));
        Assert.Contains(await Api2.GetPeriodsAsync(FakeTimetableApi.UnitId), p => p.Id == FakeTimetableApi.PeriodId(1));
    }

    [Fact]
    public async Task Working_days_are_toggled_by_buttons_and_saved_in_week_order()
    {
        var page = await RenderAsync();
        Assert.Equal("true", page.Find("[data-testid=wd-Sun]").GetAttribute("aria-pressed")); // the default Sunday to Thursday
        Assert.Equal("false", page.Find("[data-testid=wd-Fri]").GetAttribute("aria-pressed"));

        page.Find("[data-testid=wd-Sat]").Click();
        page.Find("[data-testid=wd-Mon]").Click(); // switch Monday off
        page.Find("[data-testid=wd-save]").Click();

        page.WaitForElement("[data-testid=wd-saved]");
        var saved = Config.SettingCalls.Last();
        Assert.Equal("Timetable.WorkingDays", saved.Name);
        Assert.Equal("[\"Sun\",\"Tue\",\"Wed\",\"Thu\",\"Sat\"]", saved.Request.ValueJson); // in week order, not the order ticked
    }

    [Fact]
    public async Task Working_days_cannot_be_saved_empty()
    {
        var page = await RenderAsync();
        foreach (var d in new[] { "Sun", "Mon", "Tue", "Wed", "Thu" }) page.Find($"[data-testid=wd-{d}]").Click();

        Assert.NotNull(page.Find("[data-testid=wd-empty]"));
        Assert.True(page.Find("[data-testid=wd-save]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Without_Timetable_Configure_the_page_is_forbidden()
    {
        await GrantAsync(Permissions.Timetable.View);
        RenderProviders();

        var page = Render<TimetableSetupPage>(p => p.Add(x => x.UnitId, FakeTimetableApi.UnitId));

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=setup-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync("ar");

        Assert.Equal("إعداد الجدول الدراسي", page.Find("[data-testid=setup-title]").TextContent.Trim());
    }
}

public sealed class FakeTimetableApiTests
{
    [Fact]
    public async Task Fake_enforces_block_and_warn()
    {
        var api = new FakeTimetableApi();
        var maths = api.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        var onArabic = new SaveSlotRequest(maths.ClassCourseId, maths.TermId, maths.TeacherUserId, DayOfWeek.Sunday, FakeTimetableApi.PeriodId(2), null, maths.Version);

        var refused = await Assert.ThrowsAsync<ApiProblemException>(() => api.UpdateSlotAsync(FakeTimetableApi.ClassId, maths.Id, onArabic));
        Assert.Equal("timetable_conflict", refused.Code);
        Assert.Equal(ConflictKinds.ClassDoubleBooked, Assert.Single(refused.Conflicts).Kind);

        api.ConflictMode = ConflictModes.Warn;
        var saved = await api.UpdateSlotAsync(FakeTimetableApi.ClassId, maths.Id, onArabic);
        Assert.Equal(ConflictKinds.ClassDoubleBooked, Assert.Single(saved.Warnings).Kind);
        Assert.Contains(SlotBadges.Conflict, saved.Slot.Badges);
    }

    [Fact]
    public async Task A_stale_version_is_a_concurrency_conflict()
    {
        var api = new FakeTimetableApi();
        var maths = api.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        var request = new SaveSlotRequest(maths.ClassCourseId, maths.TermId, maths.TeacherUserId, DayOfWeek.Tuesday, FakeTimetableApi.PeriodId(4), null, maths.Version + 3);

        Assert.Equal("concurrency_conflict", (await Assert.ThrowsAsync<ApiProblemException>(() => api.UpdateSlotAsync(FakeTimetableApi.ClassId, maths.Id, request))).Code);
    }

    [Fact]
    public async Task The_clashes_of_a_409_reach_the_client_exception()
    {
        var body = "{\"code\":\"timetable_conflict\",\"conflicts\":[{\"kind\":\"TeacherDoubleBooked\",\"slotId\":null,\"courseNameAr\":null,\"courseNameEn\":null,\"teacherName\":\"A. Saleh\"}]}";
        using var response = new HttpResponseMessage(System.Net.HttpStatusCode.Conflict) { Content = new StringContent(body) };

        var problem = await ApiProblemException.FromResponseAsync(response);

        Assert.Equal("timetable_conflict", problem.Code);
        var clash = Assert.Single(problem.Conflicts);
        Assert.Equal((ConflictKinds.TeacherDoubleBooked, "A. Saleh"), (clash.Kind, clash.TeacherName)); // hidden details stay null: the page says "another lesson"
    }
}
