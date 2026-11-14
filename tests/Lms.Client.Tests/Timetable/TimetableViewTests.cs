using System.Globalization;
using Bunit;
using Lms.Client.Components.Timetable;
using Lms.Client.Pages.Classes;
using Lms.Client.Pages.Timetable;
using Lms.Client.Services.Api;
using Lms.Client.Services.Timetable;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Timetable;

public sealed class TimetableViewTests : TimetableTestContext
{
    // Sunday 15 Nov 2026 is the first day of the week the fake timetable is viewed in.
    private static readonly DateOnly Sunday = new(2026, 11, 15);
    private static readonly Guid Student = Guid.Parse("50000000-0000-0000-0000-000000000001");

    public TimetableViewTests() => Services.AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero))); // a Tuesday: the week of the 15th

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<IRenderedComponent<TComponent>> RenderAsync<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null, string culture = "en", params string[] permissions)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        FakeCultureService.Apply(new CultureInfo(culture));
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions.Length == 0 ? [Permissions.Timetable.ViewOwn] : permissions);
        RenderProviders();
        return parameters is null ? Render<TComponent>() : Render<TComponent>(parameters);
    }

    private Task<TimetableViewDto> ViewAsync() => Api2.GetMyTimetableAsync(Sunday, Sunday.AddDays(6));

    [Fact]
    public async Task Four_entry_points_render_the_same_component()
    {
        var teacher = await RenderAsync<MyTimetablePage>();
        teacher.WaitForElement("[data-testid=tt-view]");
        Assert.NotNull(teacher.Find("[data-testid=tt-week]"));
        await DisposeComponentsAsync();

        var child = await RenderAsync<ChildTimetablePage>(p => p.Add(x => x.StudentId, Student));
        child.WaitForElement("[data-testid=tt-view]");
        Assert.NotNull(child.Find("[data-testid=tt-week]")); // a guardian's view of a child: the same week grid
        await DisposeComponentsAsync();

        var view = await ViewAsync();
        var direct = await RenderAsync<TimetableView>(p => p.Add(x => x.View, view));
        Assert.NotNull(direct.Find("[data-testid=tt-days]")); // and the phone day list beside it
    }

    [Fact]
    public async Task Day_list_below_600px_is_the_same_cards_in_a_vertical_list()
    {
        var view = await ViewAsync();

        var cut = await RenderAsync<TimetableView>(p => p.Add(x => x.View, view).Add(x => x.Today, Sunday));

        Assert.Contains("lms-tt-days", cut.Find("[data-testid=tt-days]").ClassName); // shown by CSS under 600 px, the week grid is hidden there
        Assert.Contains("lms-tt-weekgrid", cut.Find(".lms-tt-weekgrid").ClassName);
        Assert.Equal(2, cut.FindAll("[data-testid=tt-daylist] [data-testid^=tt-card-]").Count); // Sunday: Mathematics (period 1) and Arabic (period 2) are published
    }

    [Fact]
    public async Task Exception_badge_never_alters_the_base_card()
    {
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        await Api2.CreateExceptionAsync(maths.Id, new SaveTimetableExceptionRequest(Sunday.AddDays(7), ExceptionKinds.Substitute, FakeTimetableApi.Nasser, null, "Away"));
        var week1 = await RenderAsync<TimetableView>(p => p.Add(x => x.View, ViewAsync().Result));
        Assert.Empty(week1.FindAll($"[data-testid=tt-ex-{maths.Id}-{Sunday:yyyy-MM-dd}]")); // week 1: the plain lesson, no badge
        await DisposeComponentsAsync();

        var week2View = await Api2.GetMyTimetableAsync(Sunday.AddDays(7), Sunday.AddDays(13));
        var week2 = await RenderAsync<TimetableView>(p => p.Add(x => x.View, week2View));

        // week 2: the same lesson with the substitute named, in words; the base lesson itself still belongs to A. Saleh
        Assert.Contains("Substitute: M. Nasser", week2.Find($"[data-testid=tt-ex-{maths.Id}-{Sunday.AddDays(7):yyyy-MM-dd}]").TextContent);
        Assert.Equal("A. Saleh", Api2.Slots.First(s => s.Id == maths.Id).TeacherName);
    }

    [Fact]
    public async Task A_cancelled_lesson_stays_visible_with_the_word()
    {
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        await Api2.CreateExceptionAsync(maths.Id, new SaveTimetableExceptionRequest(Sunday, ExceptionKinds.Cancelled, null, null, null));

        var cut = await RenderAsync<TimetableView>(p => p.Add(x => x.View, ViewAsync().Result));

        Assert.Contains("Cancelled", cut.Find($"[data-testid=tt-ex-{maths.Id}-{Sunday:yyyy-MM-dd}]").TextContent);
        Assert.Contains("lms-tt-cancelled", cut.Find($"[data-testid=tt-card-{maths.Id}-{Sunday:yyyy-MM-dd}]").ClassName);
    }

    [Fact]
    public async Task A_moved_lesson_appears_in_its_new_period_on_that_date_only()
    {
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        await Api2.CreateExceptionAsync(maths.Id, new SaveTimetableExceptionRequest(Sunday, ExceptionKinds.Moved, null, FakeTimetableApi.PeriodId(4), null));

        var view = await ViewAsync();

        Assert.Equal(FakeTimetableApi.PeriodId(4), view.Cards.Single(c => c.SlotId == maths.Id && c.Date == Sunday).PeriodId);
        Assert.Equal(FakeTimetableApi.PeriodId(1), (await Api2.GetMyTimetableAsync(Sunday.AddDays(7), Sunday.AddDays(13))).Cards.Single(c => c.SlotId == maths.Id).PeriodId); // the next week: unchanged
    }

    [Fact]
    public async Task Drafts_never_reach_the_personal_views()
    {
        var view = await ViewAsync();

        Assert.DoesNotContain(view.Cards, c => c.CourseNameEn == "Science"); // Science is a draft in the fake
    }

    [Fact]
    public async Task Week_range_uses_iso_dates_ltr_and_moves_by_a_week()
    {
        var page = await RenderAsync<MyTimetablePage>();
        page.WaitForElement("[data-testid=week-range]");

        Assert.Equal("2026-11-15 – 2026-11-21", page.Find("[data-testid=week-range]").TextContent.Trim());
        Assert.Equal("ltr", page.Find("[data-testid=week-range]").GetAttribute("dir"));

        page.Find("[data-testid=week-next]").Click();
        page.WaitForAssertion(() => Assert.Equal("2026-11-22 – 2026-11-28", page.Find("[data-testid=week-range]").TextContent.Trim()));

        page.Find("[data-testid=week-today]").Click();
        page.WaitForAssertion(() => Assert.Equal("2026-11-15 – 2026-11-21", page.Find("[data-testid=week-range]").TextContent.Trim()));
    }

    [Fact]
    public async Task A_teacher_sees_the_class_on_each_card_and_a_child_view_does_not()
    {
        var teacher = await RenderAsync<MyTimetablePage>();
        teacher.WaitForElement("[data-testid^=tt-class-]");
        Assert.NotEmpty(teacher.FindAll("[data-testid^=tt-class-]"));
        await DisposeComponentsAsync();

        var child = await RenderAsync<ChildTimetablePage>(p => p.Add(x => x.StudentId, Student));
        child.WaitForElement("[data-testid=tt-view]");
        Assert.Empty(child.FindAll("[data-testid^=tt-class-]"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync<MyTimetablePage>(culture: "ar");
        page.WaitForElement("[data-testid=tt-week]");

        Assert.Equal("جدولي", page.Find("[data-testid=my-tt-title]").TextContent.Trim());
        Assert.Equal("rtl", page.Find("[data-testid=tt-week]").GetAttribute("dir"));
        Assert.Equal("ltr", page.Find("[data-testid=week-range]").GetAttribute("dir"));
    }
}

public sealed class TimetableExceptionDialogTests : TimetableTestContext
{
    private async Task<IRenderedComponent<TimetableBuilderPage>> OpenAsync()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Timetable.View, Permissions.Timetable.Manage);
        RenderProviders();
        var page = Render<TimetableBuilderPage>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));
        page.WaitForElement("[data-testid=tt-grid]");
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        page.Find($"[data-testid=tt-menu-{maths.Id}]").Click();
        page.WaitForElement("[data-testid=tt-exceptions]").Click();
        Dialogs.WaitForElement("[data-testid=exception-save]");
        return page;
    }

    [Fact]
    public async Task A_substitute_for_one_date_is_added_and_marks_the_lesson_without_changing_it()
    {
        var page = await OpenAsync();
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudDatePicker>().Instance.DateChanged.InvokeAsync(new DateTime(2026, 11, 22))); // a Sunday: the lesson's weekday
        await Dialogs.InvokeAsync(() => Dialogs.FindComponents<MudSelect<Guid?>>().Single(s => (string?)s.Instance.UserAttributes["data-testid"] == "exception-substitute").Instance.ValueChanged.InvokeAsync(FakeTimetableApi.Omar));

        Dialogs.Find("[data-testid=exception-save]").Click();

        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=exception-list]")));
        Assert.Contains("L. Omar", Dialogs.Find("[data-testid=exception-list]").TextContent);
        Assert.Equal("A. Saleh", Api2.Slots.First(s => s.Id == maths.Id).TeacherName); // the base lesson is untouched
        Dialogs.Find("[data-testid=exception-close]").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find($"[data-testid=tt-exception-{maths.Id}]"))); // the card now carries the word
    }

    [Fact]
    public async Task A_date_off_the_lessons_weekday_is_refused_with_a_sentence()
    {
        await OpenAsync();
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudDatePicker>().Instance.DateChanged.InvokeAsync(new DateTime(2026, 11, 24))); // a Tuesday

        Dialogs.Find("[data-testid=exception-save]").Click();

        Assert.Contains("day this lesson is held", Dialogs.WaitForElement("[data-testid=exception-error]").TextContent);
    }

    [Fact]
    public async Task Adding_without_a_date_asks_for_it_before_any_call()
    {
        await OpenAsync();

        Dialogs.Find("[data-testid=exception-save]").Click();

        Assert.Contains("Choose the date", Dialogs.Find("[data-testid=exception-error]").TextContent);
    }
}

public sealed class TimetableNavigationTests : TimetableTestContext
{
    [Fact]
    public async Task The_unit_page_lists_classes_with_links_to_their_builders_for_managers_only()
    {
        Services.AddSingleton<IClassesApi, FakeClassesApi>();
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Timetable.Manage);
        RenderProviders();

        var page = Render<TimetableClassesPage>();
        page.WaitForElement("[data-testid^=tt-class-]");

        Assert.All(page.FindAll("[data-testid^=tt-class-]"), a => Assert.Contains("/timetable", a.GetAttribute("href")));
        await DisposeComponentsAsync();

        await GrantAsync(Permissions.Timetable.View); // a reader is not offered the builders
        RenderProviders();
        var reader = Render<TimetableClassesPage>();
        reader.WaitForAssertion(() => Assert.Empty(reader.FindAll("[data-testid=tt-classes-title]")));
    }

    [Fact]
    public async Task The_class_tab_is_read_only_and_links_to_the_builder_for_managers()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Timetable.View);
        RenderProviders();

        var reader = Render<ClassTimetableTab>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));
        reader.WaitForElement("[data-testid=tt-grid]");
        Assert.Empty(reader.FindAll("[data-menu], [data-add]")); // nothing to edit here
        Assert.Empty(reader.FindAll("[data-testid=class-tt-edit]")); // and no link without Timetable.Manage
        await DisposeComponentsAsync();

        await GrantAsync(Permissions.Timetable.View, Permissions.Timetable.Manage);
        RenderProviders();
        var manager = Render<ClassTimetableTab>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));
        manager.WaitForElement("[data-testid=class-tt-edit]");
        Assert.Contains($"classes/{FakeTimetableApi.ClassId}/timetable", manager.Find("[data-testid=class-tt-edit]").GetAttribute("href"));
        Assert.Empty(manager.FindAll("[data-menu], [data-add]")); // still read-only
    }

    [Fact]
    public void Nav_has_the_timetable_entries_with_their_permissions()
    {
        var items = Lms.Client.Navigation.NavCatalog.Items;

        Assert.Equal(Permissions.Timetable.Manage, items.Single(i => i.ResourceKey == "Nav_Timetable").RequiredPermission);
        Assert.Equal(Permissions.Timetable.ViewOwn, items.Single(i => i.ResourceKey == "Nav_MyTimetable").RequiredPermission);
    }
}
