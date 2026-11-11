using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Layout;

public sealed class YearSelectorTests : ClientTestContext
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private async Task<IRenderedComponent<YearSelector>> RenderAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        var cut = Render<YearSelector>();
        await Services.GetRequiredService<IYearStore>().LoadAsync();
        return cut;
    }

    [Fact]
    public async Task Selector_hidden_without_AcademicYears_View()
    {
        var cut = await RenderAsync();

        Assert.Empty(cut.FindAll("[data-testid=year-selector]"));
        Assert.Equal(0, YearsApi.EffectiveCalls); // asking would answer 403 and redirect to /forbidden
    }

    [Fact]
    public async Task Selector_shows_the_selected_year_name()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var cut = await RenderAsync(Permissions.AcademicYears.View);

        cut.WaitForAssertion(() => Assert.Contains("2026/2027", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=year-lock]"));
    }

    [Fact]
    public async Task Closed_year_sets_read_only_and_shows_lock()
    {
        var closed = YearsApi.AddYear(FakeUnits.GroupId, "مغلق", "Closed", Today.AddDays(-700), Today.AddDays(-400), AcademicYearStatuses.Closed);
        var cut = await RenderAsync(Permissions.AcademicYears.View);

        await Services.GetRequiredService<IYearStore>().SelectAsync(closed.Id);

        cut.WaitForElement("[data-testid=year-lock]");
        Assert.True(Services.GetRequiredService<IYearStore>().IsReadOnly);
    }

    [Fact]
    public async Task No_years_shows_the_none_label()
    {
        YearsApi.EffectiveFailure = new HttpRequestException("offline");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var cut = await RenderAsync(Permissions.AcademicYears.View);

        Assert.Contains("No academic year", cut.WaitForElement("[data-testid=year-none]").TextContent);
    }

    [Fact]
    public async Task Selector_renders_rtl_in_arabic()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        await GrantAsync(Permissions.AcademicYears.View);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        var cut = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<YearSelector>());
        await Services.GetRequiredService<IYearStore>().LoadAsync();

        cut.WaitForAssertion(() => Assert.Contains("2026/2027", cut.Markup));
        Assert.NotEmpty(cut.FindAll("[data-testid=year-selector]"));
    }
}
