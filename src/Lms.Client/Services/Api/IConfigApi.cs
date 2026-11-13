using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;

namespace Lms.Client.Services.Api;

/// <summary>Policies and inheritable settings of an org unit (FR-ORG-10, FR-ORG-11).</summary>
public interface IConfigApi
{
    Task<IReadOnlyList<PolicyDefinitionDto>> GetPolicyCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EffectivePolicyDto>> GetPoliciesAsync(Guid unitId, CancellationToken ct = default);
    Task EnablePolicyAsync(Guid unitId, string key, CancellationToken ct = default);
    Task DisablePolicyAsync(Guid unitId, string key, CancellationToken ct = default);

    Task<IReadOnlyList<SettingDefinition>> GetSettingCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsAsync(Guid unitId, CancellationToken ct = default);

    /// <summary>
    /// The same read for a page that only wants a display preference (Hijri dates): a caller without Settings.View gets an empty
    /// list, not the Forbidden page, and the page falls back to the defaults.
    /// </summary>
    async Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsIfAllowedAsync(Guid unitId, CancellationToken ct = default)
    {
        try { return await GetSettingsAsync(unitId, ct); }
        catch (ApiForbiddenException) { return []; }
    }

    Task SetSettingAsync(Guid unitId, string name, SetSettingRequest request, CancellationToken ct = default);
    Task RemoveSettingAsync(Guid unitId, string name, CancellationToken ct = default);
}

public sealed class ConfigApi(ApiHttp http) : IConfigApi
{
    public async Task<IReadOnlyList<PolicyDefinitionDto>> GetPolicyCatalogAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<PolicyDefinitionDto>>(Routes.PoliciesCatalog, ct);

    public async Task<IReadOnlyList<EffectivePolicyDto>> GetPoliciesAsync(Guid unitId, CancellationToken ct = default) =>
        await http.GetAsync<List<EffectivePolicyDto>>(Routes.OrgUnitPolicies(unitId), ct);

    public Task EnablePolicyAsync(Guid unitId, string key, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.OrgUnitPolicy(unitId, key), null, ct);

    public Task DisablePolicyAsync(Guid unitId, string key, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.OrgUnitPolicy(unitId, key), null, ct);

    public async Task<IReadOnlyList<SettingDefinition>> GetSettingCatalogAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<SettingDefinition>>(Routes.SettingsCatalog, ct);

    public async Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsAsync(Guid unitId, CancellationToken ct = default) =>
        await http.GetAsync<List<EffectiveSettingDto>>(Routes.OrgUnitSettings(unitId), ct);

    // Optional read: a plain 403 (no Settings.View) must not send the user to the Forbidden page.
    public async Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsIfAllowedAsync(Guid unitId, CancellationToken ct = default)
    {
        try { return await http.GetOptionalAsync<List<EffectiveSettingDto>>(Routes.OrgUnitSettings(unitId), ct); }
        catch (ApiForbiddenException) { return []; }
    }

    public Task SetSettingAsync(Guid unitId, string name, SetSettingRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.OrgUnitSetting(unitId, name), request, ct);

    public Task RemoveSettingAsync(Guid unitId, string name, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.OrgUnitSetting(unitId, name), null, ct);
}

/// <summary>
/// In-memory fake for the standalone dev server (Lms:UseFakeApi=true). Inheritance follows the fake org tree:
/// a policy set at an ancestor shows as Inherited; a setting locked at an ancestor is read-only.
/// </summary>
public sealed class FakeConfigApi(IOrgApi org) : IConfigApi
{
    private readonly HashSet<(Guid Unit, string Key)> _policies = [];
    private readonly Dictionary<(Guid Unit, string Name), (string ValueJson, bool Locked)> _settings = [];
    private bool _seeded;

    public Task<IReadOnlyList<PolicyDefinitionDto>> GetPolicyCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PolicyDefinitionDto>>(
            PolicyCatalog.BlockedPermissions.Select(p => new PolicyDefinitionDto(p.Key, p.Value, PolicyCatalog.BlockedRoles.GetValueOrDefault(p.Key))).ToList());

    public async Task<IReadOnlyList<EffectivePolicyDto>> GetPoliciesAsync(Guid unitId, CancellationToken ct = default)
    {
        var units = await SeedAsync();
        var chain = Chain(units, unitId); // unit first, then ancestors up to the root
        var result = new List<EffectivePolicyDto>();
        foreach (var key in PolicyCatalog.BlockedPermissions.Keys)
        {
            var holder = chain.LastOrDefault(u => _policies.Contains((u.Id, key))); // highest ancestor holding it
            result.Add(holder is null
                ? new EffectivePolicyDto(key, PolicyStates.Off, null, null, null)
                : holder.Id == unitId
                    ? new EffectivePolicyDto(key, PolicyStates.SetHere, holder.Id, holder.NameAr, holder.NameEn)
                    : new EffectivePolicyDto(key, PolicyStates.Inherited, holder.Id, holder.NameAr, holder.NameEn));
        }
        return result;
    }

    public Task EnablePolicyAsync(Guid unitId, string key, CancellationToken ct = default)
    {
        _policies.Add((unitId, key));
        return Task.CompletedTask;
    }

    public Task DisablePolicyAsync(Guid unitId, string key, CancellationToken ct = default)
    {
        _policies.Remove((unitId, key));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SettingDefinition>> GetSettingCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult(SettingCatalog.All);

    public async Task<IReadOnlyList<EffectiveSettingDto>> GetSettingsAsync(Guid unitId, CancellationToken ct = default)
    {
        var units = await SeedAsync();
        var chain = Chain(units, unitId);
        var result = new List<EffectiveSettingDto>();
        foreach (var def in SettingCatalog.All)
        {
            var lockedAbove = chain.Skip(1).FirstOrDefault(u => _settings.TryGetValue((u.Id, def.Name), out var s) && s.Locked);
            var source = lockedAbove ?? chain.FirstOrDefault(u => _settings.ContainsKey((u.Id, def.Name)));
            if (source is null)
            {
                result.Add(new EffectiveSettingDto(def.Name, def.DefaultJson, false, false, false, null, null, null));
                continue;
            }
            var (json, locked) = _settings[(source.Id, def.Name)];
            result.Add(new EffectiveSettingDto(def.Name, json, locked, source.Id == unitId, lockedAbove is not null,
                source.Id, source.NameAr, source.NameEn));
        }
        return result;
    }

    public Task SetSettingAsync(Guid unitId, string name, SetSettingRequest request, CancellationToken ct = default)
    {
        _settings[(unitId, name)] = (request.ValueJson, request.IsLocked);
        return Task.CompletedTask;
    }

    public Task RemoveSettingAsync(Guid unitId, string name, CancellationToken ct = default)
    {
        _settings.Remove((unitId, name));
        return Task.CompletedTask;
    }

    private async Task<IReadOnlyList<OrgUnitNodeDto>> SeedAsync()
    {
        var units = await org.GetOrgUnitsAsync(null);
        if (!_seeded && units.FirstOrDefault(u => u.ParentId is null) is { } root)
        {
            // Demo data: the root blocks unit creation below it and locks the upload limit.
            _policies.Add((root.Id, PolicyCatalog.CreateOrgUnits));
            _settings[(root.Id, SettingKeys.FilesMaxUploadMb.Name)] = ("500", true);
            _seeded = true;
        }
        return units;
    }

    private static List<OrgUnitNodeDto> Chain(IReadOnlyList<OrgUnitNodeDto> units, Guid unitId)
    {
        var chain = new List<OrgUnitNodeDto>();
        for (var current = units.FirstOrDefault(u => u.Id == unitId); current is not null;
             current = units.FirstOrDefault(u => u.Id == current.ParentId))
            chain.Add(current);
        return chain;
    }
}
