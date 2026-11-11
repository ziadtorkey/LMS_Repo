namespace Lms.Shared.Configuration;

/// <summary>State of a policy at a unit: off, set at this unit, or inherited from an ancestor (children add, never remove).</summary>
public static class PolicyStates
{
    public const string Off = "Off";
    public const string SetHere = "SetHere";
    public const string Inherited = "Inherited";
}

/// <summary>
/// One entry of GET /api/policies/catalog (from <c>PolicyCatalog</c>). <c>BlockedRoles</c> lists the built-in role keys a
/// role-blocking policy prevents assigning (M2, D28); null for a policy that blocks no role.
/// </summary>
public sealed record PolicyDefinitionDto(
    string Key,
    IReadOnlyList<string> BlockedPermissions,
    IReadOnlyList<string>? BlockedRoles = null);

/// <summary>Effective state of one policy at a unit; the source is the highest ancestor holding it, or this unit.</summary>
public sealed record EffectivePolicyDto(
    string Key,
    string State,
    Guid? SourceOrgUnitId,
    string? SourceNameAr,
    string? SourceNameEn);
