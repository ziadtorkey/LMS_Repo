using System.Globalization;
using Bunit;
using Lms.Client.Pages.AcademicYears;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.AcademicYears;

public sealed class AcademicYearsPageTests : ClientTestContext
{
    private readonly FakeYearsApi _years = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    public AcademicYearsPageTests()
    {
        Services.AddSingleton<IYearsApi>(_years);
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
    }

    private new async Task GrantAsync(params string[] permissions)
    {
        await base.GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private IRenderedComponent<AcademicYearsPage> RenderPage(Guid? unit = null)
    {
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        _snackbars = Render<MudSnackbarProvider>();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        if (unit is { } id) navigation.NavigateTo(navigation.GetUriWithQueryParameter("unit", id.ToString()));
        return Render<AcademicYearsPage>();
    }

    /// <summary>A planned or active year around today at a unit.</summary>
    private AcademicYearDto AroundToday(Guid unit, string status) =>
        _years.AddYear(unit, "عام الاختبار", "Test year", Today.AddDays(-30), Today.AddDays(30), status);

    private static string Id(string prefix, Guid id) => $"[data-testid={prefix}-{id}]";

    [Fact]
    public async Task The_dashboard_row_describes_the_active_year_from_its_own_data()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View);
        AroundToday(FakeUnits.SchoolId, AcademicYearStatuses.Active); // 61 days, 30 of them still ahead
        var cut = RenderPage(FakeUnits.SchoolId);

        cut.WaitForElement("[data-testid=years-stats]");

        Assert.Contains("Test year", cut.Find("[data-testid=stat-year]").TextContent);
        Assert.Equal("61", cut.Find("[data-testid=stat-days] .lms-stat-value").TextContent.Trim());
        Assert.Equal("0", cut.Find("[data-testid=stat-terms] .lms-stat-value").TextContent.Trim());
        Assert.Equal("30", cut.Find("[data-testid=stat-left] .lms-stat-value").TextContent.Trim());
    }

    [Fact]
    public async Task A_planned_year_has_no_days_left_tile()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View);
        AroundToday(FakeUnits.SchoolId, AcademicYearStatuses.Planned);
        var cut = RenderPage(FakeUnits.SchoolId);

        cut.WaitForElement("[data-testid=years-stats]");

        Assert.Empty(cut.FindAll("[data-testid=stat-left]"));
    }

    [Fact]
    public async Task Inherited_years_are_read_only_and_name_source()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Create, Permissions.AcademicYears.Update);
        var cut = RenderPage(FakeUnits.SchoolId);

        var banner = cut.WaitForElement("[data-testid=years-inherited]");
        Assert.Contains("Defined at Al Noor Schools", banner.TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=years-define-own]"));
        Assert.Empty(cut.FindAll("[data-testid^=year-edit-]"));
        Assert.Empty(cut.FindAll("[data-testid^=term-new-]"));
        Assert.Empty(cut.FindAll("[data-testid=year-new]"));
    }

    private sealed class UnitsWithout(params Guid[] hidden) : IUnitLookupApi
    {
        public Task<IReadOnlyList<Lms.Shared.Organizations.OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Lms.Shared.Organizations.OrgUnitNodeDto>>(FakeUnits.All.Where(u => !hidden.Contains(u.Id)).ToList());
    }

    private sealed class ForbiddenUnits : IUnitLookupApi
    {
        public Task<IReadOnlyList<Lms.Shared.Organizations.OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<Lms.Shared.Organizations.OrgUnitNodeDto>>(new ApiForbiddenException());
    }

    [Fact]
    public async Task Without_OrgUnits_View_the_picker_is_hidden_and_the_context_unit_is_used()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IUnitLookupApi, ForbiddenUnits>(); // a teacher cannot list units: this must not be called
        await GrantAsync(Permissions.AcademicYears.View);
        var cut = RenderPage();

        cut.WaitForElement("[data-testid^=year-status-]");
        Assert.Empty(cut.FindAll("[data-testid=unit-picker]"));
        Assert.Empty(cut.FindAll("[data-testid=year-new]"));
        Assert.Null(_years.LastEffectiveUnit);
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.DoesNotContain("forbidden", navigation.Uri);
    }

    [Fact]
    public async Task Defined_at_uses_the_names_from_the_years_when_the_unit_is_not_listed()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IUnitLookupApi>(new UnitsWithout(FakeUnits.GroupId)); // a school admin cannot see the root
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View);
        var cut = RenderPage(FakeUnits.SchoolId);

        Assert.Contains("Defined at Al Noor Schools", cut.WaitForElement("[data-testid=years-inherited]").TextContent);
    }

    [Fact]
    public async Task A_unit_can_add_calendar_entries_to_an_inherited_year()
    {
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Update);
        var year = AroundToday(FakeUnits.GroupId, AcademicYearStatuses.Active);
        var cut = RenderPage(FakeUnits.SchoolId);

        cut.WaitForElement(Id("year", year.Id));
        Assert.NotEmpty(cut.FindAll(Id("event-new", year.Id)));
        Assert.Empty(cut.FindAll(Id("term-new", year.Id))); // terms belong to the defining unit
    }

    [Fact]
    public async Task Closed_year_disables_editing()
    {
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Update, Permissions.AcademicYears.Create);
        var closed = AroundToday(FakeUnits.SchoolId, AcademicYearStatuses.Closed);
        var planned = _years.AddYear(FakeUnits.SchoolId, "مخطط", "Planned", Today.AddDays(60), Today.AddDays(120), AcademicYearStatuses.Planned);
        var cut = RenderPage(FakeUnits.SchoolId);

        cut.WaitForElement(Id("year-lock", closed.Id));
        Assert.Empty(cut.FindAll(Id("year-edit", closed.Id)));
        Assert.Empty(cut.FindAll(Id("term-new", closed.Id)));
        Assert.Empty(cut.FindAll(Id("event-new", closed.Id)));
        Assert.NotEmpty(cut.FindAll(Id("year-edit", planned.Id)));
        Assert.NotEmpty(cut.FindAll(Id("year-activate", planned.Id)));
    }

    [Fact]
    public async Task Current_term_badge_shown()
    {
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View);
        var year = AroundToday(FakeUnits.GradeId, AcademicYearStatuses.Active);
        var current = _years.AddTerm(year.Id, "الحالي", "Now", Today.AddDays(-5), Today.AddDays(5));
        var later = _years.AddTerm(year.Id, "لاحق", "Later", Today.AddDays(10), Today.AddDays(20));
        var cut = RenderPage(FakeUnits.GradeId);

        cut.WaitForElement(Id("term-current", current.Id));
        Assert.Empty(cut.FindAll(Id("term-current", later.Id)));
    }

    [Fact]
    public async Task Term_dates_outside_year_show_error_from_problem_code()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Update);
        var year = AroundToday(FakeUnits.GradeId, AcademicYearStatuses.Planned);
        var cut = RenderPage(FakeUnits.GradeId);

        await cut.WaitForElement(Id("term-new", year.Id)).ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=name-ar]").Change("فصل");
        _dialogs.Find("[data-testid=name-en]").Change("Term");
        var pickers = _dialogs.FindComponents<MudDatePicker>();
        await _dialogs.InvokeAsync(() => pickers[0].Instance.DateChanged.InvokeAsync(Today.AddDays(-90).ToDateTime(TimeOnly.MinValue)));
        await _dialogs.InvokeAsync(() => pickers[1].Instance.DateChanged.InvokeAsync(Today.AddDays(-80).ToDateTime(TimeOnly.MinValue)));
        await _dialogs.Find("[data-testid=term-save]").ClickAsync(new());

        Assert.Contains("يجب أن يقع الفصل ضمن السنة الدراسية", _dialogs.WaitForElement("[data-testid=term-error]").TextContent);
    }

    [Fact]
    public async Task Calendar_marks_holidays_and_inherited_events()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View);
        var year = AroundToday(FakeUnits.GroupId, AcademicYearStatuses.Active);
        var holiday = _years.AddEvent(year.Id, FakeUnits.GroupId, "عطلة", "Break", CalendarEventKinds.Holiday, Today, Today);
        var cut = RenderPage(FakeUnits.SchoolId); // the school inherits the group's years and calendar

        cut.WaitForElement(Id("event-inherited", holiday.Id));
        Assert.Contains("From Al Noor Schools", cut.Find(Id("event-inherited", holiday.Id)).TextContent);
        var cell = cut.Find($"[data-testid=calendar-{year.Id}] [data-date='{Today:yyyy-MM-dd}']");
        Assert.Equal("true", cell.GetAttribute("data-holiday"));
        Assert.Empty(cut.FindAll(Id("event-edit", holiday.Id))); // inherited entries are read-only
    }

    [Fact]
    public async Task Year_save_offline_shows_the_generic_error_and_stale_context_redirects()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Create);
        var cut = RenderPage(FakeUnits.SchoolId);
        await cut.WaitForElement("[data-testid=years-define-own]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=name-ar]").Change("عام");
        _dialogs.Find("[data-testid=name-en]").Change("Year");
        var pickers = _dialogs.FindComponents<MudDatePicker>();
        await _dialogs.InvokeAsync(() => pickers[0].Instance.DateChanged.InvokeAsync(Today.ToDateTime(TimeOnly.MinValue)));
        await _dialogs.InvokeAsync(() => pickers[1].Instance.DateChanged.InvokeAsync(Today.AddDays(30).ToDateTime(TimeOnly.MinValue)));

        _years.NextFailure = new HttpRequestException("offline");
        await _dialogs.Find("[data-testid=year-save]").ClickAsync(new());
        Assert.Contains("Something went wrong", _dialogs.WaitForElement("[data-testid=year-error]").TextContent);

        _years.NextFailure = new ApiContextException("invalid_context");
        await _dialogs.Find("[data-testid=year-save]").ClickAsync(new());
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith("/choose-context", navigation.Uri);
    }

    [Fact]
    public async Task Delete_year_offline_shows_a_snackbar_instead_of_crashing()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Update, Permissions.AcademicYears.Delete);
        var planned = AroundToday(FakeUnits.SchoolId, AcademicYearStatuses.Planned);
        var cut = RenderPage(FakeUnits.SchoolId);
        _years.NextFailure = new HttpRequestException("offline");

        var click = cut.WaitForElement(Id("year-delete", planned.Id)).ClickAsync(new());
        await _dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());
        await click;

        cut.WaitForAssertion(() => Assert.Contains("Something went wrong", _snackbars.Markup));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View);
        var year = AroundToday(FakeUnits.GroupId, AcademicYearStatuses.Active);
        Render<MudPopoverProvider>();
        var cut = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<AcademicYearsPage>());

        cut.WaitForElement(Id("year", year.Id));
        Assert.Contains("الأعوام الدراسية", cut.Markup);
        Assert.Contains("نشط", cut.Markup);
        Assert.Contains("dir=\"ltr\"", cut.Markup); // dates stay left-to-right
    }
}
