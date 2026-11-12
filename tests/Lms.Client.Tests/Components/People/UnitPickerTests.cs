using System.Net;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.People;

/// <summary>#344: a role without OrgUnits.View (counselor, teacher...) must still reach pages that offer an optional unit filter.</summary>
public sealed class UnitPickerTests : ClientTestContext
{
    private sealed class ForbiddenUnits : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<OrgUnitNodeDto>>(new ApiForbiddenException());
    }

    private sealed class StaleContextUnits : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<OrgUnitNodeDto>>(new ApiContextException("context_required"));
    }

    private IRenderedComponent<UnitPicker> RenderPicker()
    {
        Render<MudPopoverProvider>();
        return Render<UnitPicker>(p => p.Add(x => x.Label, "Unit").Add(x => x.TestId, "picker"));
    }

    [Fact]
    public void Shows_the_units_the_caller_can_see()
    {
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();

        var cut = RenderPicker();

        Assert.Single(cut.FindAll("[data-testid=picker]"));
    }

    [Fact]
    public void Is_hidden_and_stays_on_the_page_when_the_caller_cannot_list_units()
    {
        Services.AddSingleton<IUnitLookupApi, ForbiddenUnits>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;

        var cut = RenderPicker();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=picker]")));
        Assert.Equal(before, navigation.Uri);
    }

    [Fact]
    public void A_stale_context_still_leaves_for_the_chooser()
    {
        Services.AddSingleton<IUnitLookupApi, StaleContextUnits>();
        var navigation = Services.GetRequiredService<NavigationManager>();

        RenderPicker();

        Assert.EndsWith("/choose-context", navigation.Uri);
    }

}
