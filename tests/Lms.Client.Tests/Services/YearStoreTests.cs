using Bunit;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Services;

public sealed class YearStoreTests : ClientTestContext
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private YearStore NewStore() => new(YearsApi, Context, JSInterop.JSRuntime, Services.GetRequiredService<NavigationManager>(),
        Services.GetRequiredService<PermissionState>());

    private new async Task GrantAsync(params string[] permissions)
    {
        await base.GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task Defaults_to_current_year()
    {
        await GrantAsync(Lms.Shared.Authorization.Permissions.AcademicYears.View);
        var store = NewStore();

        await store.LoadAsync();

        Assert.Equal(YearsApi.YearIdAt(FakeUnits.GroupId, AcademicYearStatuses.Active), store.SelectedYearId);
        Assert.False(store.IsReadOnly);
    }

    [Fact]
    public async Task Selection_persists_per_context()
    {
        await GrantAsync(Lms.Shared.Authorization.Permissions.AcademicYears.View);
        var assignment = Context.ActiveAssignmentId!.Value;
        var planned = YearsApi.AddYear(FakeUnits.GroupId, "مخطط", "Planned", Today.AddDays(400), Today.AddDays(700), AcademicYearStatuses.Planned);
        var store = NewStore();
        await store.LoadAsync();

        await store.SelectAsync(planned.Id);

        JSInterop.VerifyInvoke("localStorage.setItem");
        // A new session in the same context restores the choice...
        JSInterop.Setup<string?>("localStorage.getItem", $"{YearStore.StoragePrefix}{assignment}").SetResult(planned.Id.ToString());
        var restored = NewStore();
        await restored.LoadAsync();
        Assert.Equal(planned.Id, restored.SelectedYearId);

        // ...while another context (nothing stored) falls back to the active year.
        await Context.SetAsync(Guid.NewGuid());
        var other = NewStore();
        await other.LoadAsync();
        Assert.Equal(YearsApi.YearIdAt(FakeUnits.GroupId, AcademicYearStatuses.Active), other.SelectedYearId);
    }

    [Fact]
    public async Task Context_change_reloads_years()
    {
        await GrantAsync(Lms.Shared.Authorization.Permissions.AcademicYears.View);
        var store = NewStore();
        await store.LoadAsync();
        Assert.Single(store.Years);

        YearsApi.AddYear(FakeUnits.GroupId, "جديد", "New", Today.AddDays(400), Today.AddDays(700), AcademicYearStatuses.Planned);
        await Context.SetAsync(Guid.NewGuid());

        await Eventually(() => store.Years.Count == 2);
    }

    [Fact]
    public async Task Closed_year_sets_read_only()
    {
        await GrantAsync(Lms.Shared.Authorization.Permissions.AcademicYears.View);
        var closed = YearsApi.AddYear(FakeUnits.GroupId, "مغلق", "Closed", Today.AddDays(-700), Today.AddDays(-400), AcademicYearStatuses.Closed);
        var store = NewStore();
        await store.LoadAsync();

        await store.SelectAsync(closed.Id);

        Assert.True(store.IsReadOnly);
    }

    [Fact]
    public async Task Without_AcademicYears_View_the_years_api_is_never_called()
    {
        await GrantAsync(Lms.Shared.Authorization.Permissions.Roles.View);
        var store = NewStore();

        await store.LoadAsync();
        await Context.SetAsync(Guid.NewGuid());
        await Services.GetRequiredService<PermissionState>().RefreshAsync();

        Assert.Empty(store.Years);
        Assert.Equal(0, YearsApi.EffectiveCalls);
    }

    [Fact]
    public async Task Permission_reload_after_a_context_change_reloads_the_years()
    {
        await GrantAsync(Lms.Shared.Authorization.Permissions.AcademicYears.View);
        var store = NewStore();
        await store.LoadAsync();
        var calls = YearsApi.EffectiveCalls;

        await Context.SetAsync(Guid.NewGuid()); // PermissionState reloads, then the store follows

        await Eventually(() => YearsApi.EffectiveCalls > calls);
    }

    [Fact]
    public async Task Forbidden_years_call_leaves_the_store_empty_without_redirecting()
    {
        await GrantAsync(Lms.Shared.Authorization.Permissions.AcademicYears.View);
        YearsApi.EffectiveFailure = new ApiForbiddenException();
        var store = NewStore();

        await store.LoadAsync();

        Assert.Empty(store.Years);
        Assert.DoesNotContain("forbidden", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
