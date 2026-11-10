namespace Lms.Shared.Configuration;

/// <summary>
/// Effective value of one catalog setting at a unit. <c>SourceOrgUnitId</c> is the unit that defines the value
/// (null = catalog default); <c>LockedByAncestor</c> = a strict ancestor locked it, so this unit cannot override.
/// </summary>
public sealed record EffectiveSettingDto(
    string Name,
    string ValueJson,
    bool IsLocked,
    bool DefinedHere,
    bool LockedByAncestor,
    Guid? SourceOrgUnitId,
    string? SourceNameAr,
    string? SourceNameEn);

/// <summary>Body of PUT /api/org-units/{id}/settings/{name}.</summary>
public sealed record SetSettingRequest(string ValueJson, bool IsLocked);
