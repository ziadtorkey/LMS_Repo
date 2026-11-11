using Lms.Client.Services.Api;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;

namespace Lms.Client.Tests.Support;

public sealed class StubConfigApi : IConfigApi
{
    public List<EffectivePolicyDto> Policies { get; } = [];
    public List<EffectiveSettingDto> Settings { get; } = [];
    public List<(Guid Unit, string Key)> Enabled { get; } = [];
    public List<(Guid Unit, string Key)> Disabled { get; } = [];
    public List<(Guid Unit, string Name, SetSettingRequest Request)> SettingCalls { get; } = [];
    public List<(Guid Unit, string Name)> Removed { get; } = [];
    public Exception? PolicyFailure { get; set; }
    public Exception? SettingFailure { get; set; }

    /// <summary>Starts with every policy Off and every setting at its catalog default.</summary>
    public StubConfigApi()
    {
        foreach (var key in PolicyCatalog.BlockedPermissions.Keys)
            Policies.Add(new EffectivePolicyDto(key, PolicyStates.Off, null, null, null));
        foreach (var def in SettingCatalog.All)
            Settings.Add(new EffectiveSettingDto(def.Name, def.DefaultJson, false, false, false, null, null, null));
    }

    public Task<IReadOnlyList<PolicyDefinitionDto>> GetPolicyCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PolicyDefinitionDto>>(
            PolicyCatalog.BlockedPermissions.Select(p => new PolicyDefinitionDto(p.Key, p.Value)).ToList());

    public Task<IReadOnlyList<EffectivePolicyDto>> GetPoliciesAsync(Guid unitId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EffectivePolicyDto>>(Policies.ToList());

    public Task EnablePolicyAsync(Guid unitId, string key, CancellationToken ct = default)
    {
        if (PolicyFailure is not null) return Task.FromException(PolicyFailure);
        Enabled.Add((unitId, key));
        Replace(key, PolicyStates.SetHere);
        return Task.CompletedTask;
    }

    public Task DisablePolicyAsync(Guid unitId, string key, CancellationToken ct = default)
    {
        if (PolicyFailure is not null) return Task.FromException(PolicyFailure);
        Disabled.Add((unitId, key));
        Replace(key, PolicyStates.Off);
        return Task.CompletedTask;
    }

    private void Replace(string key, string state)
    {
        var i = Policies.FindIndex(p => p.Key == key);
        Policies[i] = Policies[i] with { State = state };
    }

    public Task<IReadOnlyList<SettingDefinition>> GetSettingCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult(SettingCatalog.All);

    public Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsAsync(Guid unitId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EffectiveSettingDto>>(Settings.ToList());

    public Task SetSettingAsync(Guid unitId, string name, SetSettingRequest request, CancellationToken ct = default)
    {
        if (SettingFailure is not null) return Task.FromException(SettingFailure);
        SettingCalls.Add((unitId, name, request));
        var i = Settings.FindIndex(s => s.Name == name);
        Settings[i] = Settings[i] with { ValueJson = request.ValueJson, IsLocked = request.IsLocked, DefinedHere = true };
        return Task.CompletedTask;
    }

    public Task RemoveSettingAsync(Guid unitId, string name, CancellationToken ct = default)
    {
        Removed.Add((unitId, name));
        var def = SettingCatalog.Find(name)!;
        var i = Settings.FindIndex(s => s.Name == name);
        Settings[i] = new EffectiveSettingDto(name, def.DefaultJson, false, false, false, null, null, null);
        return Task.CompletedTask;
    }
}
