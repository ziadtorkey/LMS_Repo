using Bunit;
using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Support;

/// <summary>Shared setup of the M3 student tests: the staff test setup plus fake students, guardians, classes and unit settings.</summary>
public abstract class StudentsTestContext : StaffTestContext
{
    protected FakeStudentsApi Students { get; } = new();
    protected FakeGuardiansApi GuardiansApi { get; } = new();
    protected FakeClassesApi ClassesApi { get; } = new();
    protected StubSettingsApi Settings { get; } = new();

    protected StudentsTestContext()
    {
        Services.AddSingleton<IStudentsApi>(Students);
        Services.AddSingleton<IGuardiansApi>(GuardiansApi);
        Services.AddSingleton<IClassesApi>(ClassesApi);
        Services.AddSingleton<IConfigApi>(Settings);
    }

    protected static IRenderedComponent<MudSelect<string>> StringSelect(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> root, string testId) =>
        root.FindComponents<MudSelect<string>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == testId);
}

/// <summary>Effective unit settings for tests: the catalog defaults, overridden by <see cref="Values"/>. Everything else is unused.</summary>
public sealed class StubSettingsApi : IConfigApi
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsAsync(Guid unitId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EffectiveSettingDto>>(SettingCatalog.All
            .Select(d => new EffectiveSettingDto(d.Name, Values.GetValueOrDefault(d.Name, d.DefaultJson), false, false, false, null, null, null)).ToList());

    public Task<IReadOnlyList<PolicyDefinitionDto>> GetPolicyCatalogAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<EffectivePolicyDto>> GetPoliciesAsync(Guid unitId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task EnablePolicyAsync(Guid unitId, string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DisablePolicyAsync(Guid unitId, string key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<SettingDefinition>> GetSettingCatalogAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetSettingAsync(Guid unitId, string name, SetSettingRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveSettingAsync(Guid unitId, string name, CancellationToken ct = default) => throw new NotSupportedException();
}
