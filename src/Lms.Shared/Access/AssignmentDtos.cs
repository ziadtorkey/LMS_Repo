namespace Lms.Shared.Access;

/// <summary>A role assignment (user, role, unit, validity; PRD §3.3). <c>IsCurrentlyValid</c> = valid today.</summary>
public sealed record AssignmentDto(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string UserDisplayNameAr,
    string UserDisplayNameEn,
    Guid RoleId,
    string RoleNameAr,
    string RoleNameEn,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsCurrentlyValid);

/// <summary>
/// Body of POST /api/assignments. Display names are required only when the email is unknown
/// (a new user is created and invited).
/// </summary>
public sealed record CreateAssignmentRequest(
    string Email,
    string? DisplayNameAr,
    string? DisplayNameEn,
    Guid RoleId,
    Guid OrgUnitId,
    DateOnly? ValidFrom,
    DateOnly? ValidTo);

/// <summary>Body of PUT /api/assignments/{id}.</summary>
public sealed record UpdateAssignmentRequest(DateOnly? ValidFrom, DateOnly? ValidTo);
