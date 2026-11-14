namespace Lms.Shared.Me;

/// <summary>Response of GET /api/me: the caller and every valid role assignment (context) they hold.</summary>
public sealed record MeDto(
    Guid UserId,
    string DisplayNameAr,
    string DisplayNameEn,
    bool IsPlatformAdmin,
    IReadOnlyList<UserContextDto> Contexts);

/// <summary>
/// One selectable active context = one valid role assignment of the caller. <c>TimeZone</c> is the IANA id of the client's <c>Org.TimeZone</c> (D64,
/// default Asia/Riyadh): every signed-in user loads it with the contexts, so a role without <c>Settings.View</c> (teacher, student, guardian) still
/// shows the client's local times and days.
/// </summary>
public sealed record UserContextDto(
    Guid AssignmentId,
    Guid OrganizationId,
    string OrganizationNameAr,
    string OrganizationNameEn,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    string RoleNameAr,
    string RoleNameEn,
    string TimeZone = "Asia/Riyadh");

/// <summary>
/// Response of GET /api/me/permissions: permissions of the active role minus those
/// blocked by policies at the context unit or its ancestors.
/// </summary>
public sealed record EffectivePermissionsDto(Guid? AssignmentId, IReadOnlyList<string> Permissions);
