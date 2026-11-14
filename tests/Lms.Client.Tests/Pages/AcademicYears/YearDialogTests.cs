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

/// <summary>The terms-per-year option of the year dialog (M5 Task 30, D62, L8): two or three terms, planned years only, the count from the server.</summary>
public sealed class YearDialogTests : ClientTestContext
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private readonly FakeYearsApi _years = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public YearDialogTests()
    {
        Services.AddSingleton<IYearsApi>(_years);
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
    }

    private async Task<AcademicYearDto> OpenEditAsync(string status)
    {
        Culture.Current = new CultureInfo("en");
        await base.GrantAsync(Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Create, Permissions.AcademicYears.Update);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        var year = _years.AddYear(FakeUnits.SchoolId, "عام", "Year", Today.AddDays(10), Today.AddDays(200), status);
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("unit", FakeUnits.SchoolId.ToString()));
        var page = Render<AcademicYearsPage>();
        await page.WaitForElement($"[data-testid=year-edit-{year.Id}]").ClickAsync(new());
        return year;
    }

    private Task PickAsync(int count) => _dialogs.InvokeAsync(() => _dialogs.FindComponent<MudSelect<int>>().Instance.ValueChanged.InvokeAsync(count));

    private IEnumerable<string> Preview() => _dialogs.FindAll("[data-testid=year-terms-preview] li").Select(li => li.TextContent.Trim());

    [Fact]
    public async Task Terms_option_previews_template_names()
    {
        await OpenEditAsync(AcademicYearStatuses.Planned);
        _dialogs.WaitForElement("[data-testid=year-terms-option]");

        Assert.Equal(["Term 1", "Term 2"], Preview());
        await PickAsync(3);
        Assert.Equal(["Term 1", "Term 2", "Term 3"], Preview());
    }

    [Fact]
    public async Task Terms_switch_is_saved_with_the_year()
    {
        var year = await OpenEditAsync(AcademicYearStatuses.Planned);
        _dialogs.WaitForElement("[data-testid=year-terms-option]");

        await PickAsync(3);
        await _dialogs.Find("[data-testid=year-save]").ClickAsync(new());

        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=year-save]")));
        Assert.Equal(3, _years.Structures[year.Id].TermsPerYear);
    }

    [Fact]
    public async Task Terms_switch_shows_dependants_conflict()
    {
        await OpenEditAsync(AcademicYearStatuses.Planned);
        _dialogs.WaitForElement("[data-testid=year-terms-option]");
        _years.TermsHaveDependants = true;

        await PickAsync(3);
        await _dialogs.Find("[data-testid=year-save]").ClickAsync(new());

        Assert.Contains("already has a gradebook, timetable or attendance", _dialogs.WaitForElement("[data-testid=year-error]").TextContent);
        Assert.NotEmpty(_dialogs.FindAll("[data-testid=year-save]")); // the dialog stays open
        Assert.Null(_years.LastStructure);
    }

    [Fact]
    public async Task Client_has_no_default_term_count()
    {
        _years.DefaultStructure = _years.DefaultStructure with { TermsPerYear = 3 }; // this client runs three terms
        await OpenEditAsync(AcademicYearStatuses.Planned);
        _dialogs.WaitForElement("[data-testid=year-terms-option]");

        Assert.Equal(3, Preview().Count()); // the dialog follows the server and has no number of its own
    }

    [Fact]
    public async Task An_active_year_has_no_terms_option()
    {
        await OpenEditAsync(AcademicYearStatuses.Active);
        _dialogs.WaitForElement("[data-testid=year-save]");

        Assert.Empty(_dialogs.FindAll("[data-testid=year-terms-option]"));
    }
}
