using Bunit;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Organizations;
using Lms.Shared.Me;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Lms.Client.Tests.Support;

/// <summary>Shared bUnit setup: MudBlazor, localization, fake services, authenticated user.</summary>
public abstract class ClientTestContext : BunitContext, IAsyncLifetime
{
    // MudBlazor services are IAsyncDisposable only: dispose asynchronously (xunit v2) and skip bUnit's sync dispose.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;
    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();
    protected override void Dispose(bool disposing) { }

    protected FakeCultureService Culture { get; } = new();
    protected InMemoryContextStore Context { get; } = new();
    protected StubLmsApi Api { get; } = new();
    protected StubClientsApi Clients { get; } = new();
    protected StubOrgApi Org { get; } = new();
    protected FakeYearsApi YearsApi { get; } = new();

    /// <summary>Grants the active context these permissions (and selects a context).</summary>
    protected async Task GrantAsync(params string[] permissions)
    {
        Api.Permissions = new Lms.Shared.Me.EffectivePermissionsDto(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
    }

    protected ClientTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddTransient<MudBlazor.MudLocalizer, LmsMudLocalizer>();
        Services.AddLocalization();
        Services.AddSingleton<ICultureService>(Culture);
        Services.AddSingleton<IContextStore>(Context);
        Services.AddSingleton<ILmsApi>(Api);
        Services.AddScoped<PermissionState>();
        Services.AddSingleton<IClientsApi>(Clients);
        Services.AddSingleton<IOrgApi>(Org);
        Services.AddSingleton<IYearsApi>(YearsApi);
        Services.AddScoped<IYearStore, YearStore>();
        AddAuthorization().SetAuthorized("tester");
    }
}

public sealed class FakeCultureService : ICultureService
{
    public System.Globalization.CultureInfo Current { get; set; } = new("ar");
    public bool IsRightToLeft => Current.TextInfo.IsRightToLeft;
    public string? LastSet { get; private set; }
    public Task SetAsync(string culture) { LastSet = culture; return Task.CompletedTask; }
}

public sealed class InMemoryContextStore : IContextStore
{
    public Guid? ActiveAssignmentId { get; private set; }
    public event Action? Changed;
    public Task SetAsync(Guid assignmentId) { ActiveAssignmentId = assignmentId; Changed?.Invoke(); return Task.CompletedTask; }
    public Task ClearAsync() { ActiveAssignmentId = null; Changed?.Invoke(); return Task.CompletedTask; }
}

public sealed class StubLmsApi : ILmsApi
{
    public MeDto Me { get; set; } = new(Guid.NewGuid(), "مستخدم", "Tester", false, []);
    public EffectivePermissionsDto Permissions { get; set; } = new(null, []);
    public Exception? Failure { get; set; }
    public Task<MeDto> GetMeAsync(CancellationToken ct = default) =>
        Failure is null ? Task.FromResult(Me) : Task.FromException<MeDto>(Failure);
    public Task<EffectivePermissionsDto> GetMyPermissionsAsync(CancellationToken ct = default) => Task.FromResult(Permissions);
}
