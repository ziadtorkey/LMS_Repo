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

namespace Lms.Client.Tests.Timetable;

/// <summary>The M5 accessibility sweep for the timetable pages (Task 39, X-10): the shared axe-style scan in both languages, plus the grid's own contract.</summary>
public sealed class TimetableA11yTests : TimetableTestContext
{
    public TimetableA11yTests() => Services.AddSingleton<IClassesApi, FakeClassesApi>();

    private async Task PrepareAsync(string culture, params string[] permissions)
    {
        FakeCultureService.Apply(new CultureInfo(culture));
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(permissions);
        RenderProviders();
    }

    private static void AssertClean(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, string page, bool oneHeading = true)
    {
        var problems = A11yScan.Problems(cut, oneHeading);
        Assert.True(problems.Count == 0, $"{page}:\n  " + string.Join("\n  ", problems));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Builder_set_up_and_class_list_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Permissions.Timetable.View, Permissions.Timetable.Manage, Permissions.Timetable.Configure);

        var builder = Render<TimetableBuilderPage>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));
        builder.WaitForElement("[data-testid=tt-grid]");
        AssertClean(builder, "builder");
        var maths = Api2.Slots.First(s => s.ClassCourseId == FakeTimetableApi.Maths);
        builder.Find($"[data-testid=tt-menu-{maths.Id}]").Click();
        builder.WaitForElement("[data-testid=tt-move-menu]");
        AssertClean(builder, "builder with the lesson menu");
        builder.Find("[data-testid=tt-validate]").Click();
        builder.WaitForElement("[data-testid=tt-conflicts-none]");
        AssertClean(builder, "builder with the conflicts panel");
        builder.Find("[data-testid=tt-add-2-5]").Click();
        Dialogs.WaitForElement("[data-testid=slot-save]");
        AssertClean(Dialogs, "the lesson dialog", oneHeading: false);
        await DisposeComponentsAsync();

        RenderProviders();
        var setup = Render<TimetableSetupPage>(p => p.Add(x => x.UnitId, FakeTimetableApi.UnitId));
        setup.WaitForElement("[data-testid=periods-table]");
        AssertClean(setup, "set-up");
        setup.Find("[data-testid=period-new]").Click();
        Dialogs.WaitForElement("[data-testid=period-save]");
        AssertClean(Dialogs, "the period dialog", oneHeading: false);
        await DisposeComponentsAsync();

        RenderProviders();
        var classes = Render<TimetableClassesPage>();
        classes.WaitForElement("[data-testid^=tt-class-]");
        AssertClean(classes, "unit class list");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Personal_views_and_the_class_tab_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Permissions.Timetable.ViewOwn, Permissions.Timetable.View, Permissions.Timetable.Manage, Permissions.Students.ViewChildren);

        var mine = Render<MyTimetablePage>();
        mine.WaitForElement("[data-testid=tt-view]");
        AssertClean(mine, "my timetable");
        await DisposeComponentsAsync();

        RenderProviders();
        var child = Render<ChildTimetablePage>(p => p.Add(x => x.StudentId, Guid.NewGuid()));
        child.WaitForElement("[data-testid=tt-view]");
        AssertClean(child, "child timetable");
        await DisposeComponentsAsync();

        RenderProviders();
        var tab = Render<ClassTimetableTab>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));
        tab.WaitForElement("[data-testid=tt-grid]");
        AssertClean(tab, "class timetable tab", oneHeading: false);
    }

    [Fact]
    public async Task The_grid_has_a_grid_role_row_and_column_headers_a_live_region_and_one_tab_stop()
    {
        await PrepareAsync("en", Permissions.Timetable.View, Permissions.Timetable.Manage);
        var builder = Render<TimetableBuilderPage>(p => p.Add(x => x.ClassId, FakeTimetableApi.ClassId));
        builder.WaitForElement("[data-testid=tt-grid]");

        var grid = builder.Find("[data-testid=tt-grid]");
        Assert.Equal("grid", grid.GetAttribute("role"));
        Assert.All(grid.QuerySelectorAll("thead th[scope=col]"), th => Assert.False(string.IsNullOrWhiteSpace(th.TextContent) && th.QuerySelector(".lms-visually-hidden") is null));
        Assert.All(grid.QuerySelectorAll("tbody th"), th => Assert.Equal("row", th.GetAttribute("scope")));
        Assert.Equal("polite", builder.Find("[data-testid=tt-live]").GetAttribute("aria-live"));
        Assert.Single(grid.QuerySelectorAll("td[data-cell]"), c => c.GetAttribute("tabindex") == "0"); // the roving tabindex: one stop for the whole grid
        Assert.All(grid.QuerySelectorAll("td[data-cell]"), c => Assert.False(string.IsNullOrWhiteSpace(c.GetAttribute("aria-label")))); // every cell says what and where it is
    }

    [Fact]
    public async Task The_week_grid_and_the_day_list_share_the_same_lessons_so_neither_layout_loses_one()
    {
        await PrepareAsync("en", Permissions.Timetable.ViewOwn);
        var view = await Api2.GetMyTimetableAsync(new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 21));
        var cut = Render<TimetableView>(p => p.Add(x => x.View, view).Add(x => x.Today, new DateOnly(2026, 11, 15)));

        var inWeek = cut.FindAll(".lms-tt-weekgrid [data-testid^=tt-card-]").Select(c => c.GetAttribute("data-testid")).ToHashSet();
        var onSunday = cut.FindAll(".lms-tt-days [data-testid^=tt-card-]").Select(c => c.GetAttribute("data-testid")).ToHashSet();

        Assert.NotEmpty(onSunday);
        Assert.True(onSunday.IsSubsetOf(inWeek)); // what the phone shows for the selected day is in the week grid too
    }
}
