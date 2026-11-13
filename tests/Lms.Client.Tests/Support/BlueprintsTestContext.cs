using Lms.Client.Services.Api;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Support;

/// <summary>Shared setup of the blueprint tests: courses plus a fake blueprints API and controllable unit settings.</summary>
public abstract class BlueprintsTestContext : CoursesTestContext
{
    protected FakeBlueprintsApi Blueprints { get; } = new();
    protected BlueprintSettingsStub Settings { get; } = new();

    protected BlueprintsTestContext()
    {
        Services.AddSingleton<IBlueprintsApi>(Blueprints);
        Services.AddSingleton<IConfigApi>(Settings);
    }

    protected Guid MathId => Courses.All.First(c => c.Summary.Code == "MATH101").Summary.Id;
}

/// <summary>Effective unit settings for blueprint tests: catalog defaults overridden by <see cref="Values"/>; the rest is unused.</summary>
public sealed class BlueprintSettingsStub : IConfigApi
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
