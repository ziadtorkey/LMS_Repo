namespace Lms.Shared.People;

/// <summary>One entry of a person's status history (newest first).</summary>
public sealed record PersonStatusChangeDto(
    string FromStatus,
    string ToStatus,
    string Reason,
    DateTime ChangedAt,
    string? ChangedByNameAr,
    string? ChangedByNameEn);

/// <summary>Body of PUT /api/staff/{id}/status. <c>Status</c> from <see cref="PersonStatuses"/>; reason 1–500 chars.</summary>
public sealed record SetPersonStatusRequest(string Status, string Reason);

/// <summary>A role to assign to a staff member at a unit (create and share).</summary>
public sealed record StaffRoleInput(Guid RoleId, Guid OrgUnitId, DateOnly? ValidFrom, DateOnly? ValidTo);

/// <summary>A role assignment of a staff member. <c>IsShare</c> = assigned outside the home unit's subtree (D29).</summary>
public sealed record StaffRoleDto(
    Guid AssignmentId,
    Guid RoleId,
    string RoleNameAr,
    string RoleNameEn,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsShare);

/// <summary>
/// One row of GET /api/staff. <c>IsSharedIn</c> = the caller sees this person only through a share into its scope.
/// <c>ExpiresOn</c> is the last usable day (inclusive, D35).
/// </summary>
public sealed record StaffSummaryDto(
    Guid Id,
    Guid UserId,
    string NameAr,
    string NameEn,
    string Email,
    string? Mobile,
    string? JobTitleAr,
    string? JobTitleEn,
    Guid HomeOrgUnitId,
    string HomeOrgUnitNameAr,
    string HomeOrgUnitNameEn,
    string Status,
    DateOnly? ExpiresOn,
    Guid? PhotoFileId,
    bool IsSharedIn,
    IReadOnlyList<StaffRoleDto> Roles);

/// <summary>
/// GET /api/staff/{id}. When <c>IsLimitedView</c> (a share target, D29) the national id, mobile and status history are
/// null/empty.
/// </summary>
public sealed record StaffDetailDto(
    StaffSummaryDto Summary,
    string? NationalId,
    bool IsActivated,
    bool IsLimitedView,
    IReadOnlyList<PersonStatusChangeDto> StatusHistory);

/// <summary>Body of POST /api/staff. Email is the login identity and is read-only after creation.</summary>
public sealed record CreateStaffRequest(
    string NameAr,
    string NameEn,
    string Email,
    string? Mobile,
    string? NationalId,
    string? JobTitleAr,
    string? JobTitleEn,
    Guid? PhotoFileId,
    Guid HomeOrgUnitId,
    DateOnly? ExpiresOn,
    IReadOnlyList<StaffRoleInput> Roles);

/// <summary>Body of PUT /api/staff/{id} (no email: it cannot change, PRD gap G7).</summary>
public sealed record UpdateStaffRequest(
    string NameAr,
    string NameEn,
    string? Mobile,
    string? NationalId,
    string? JobTitleAr,
    string? JobTitleEn,
    Guid? PhotoFileId,
    Guid HomeOrgUnitId,
    DateOnly? ExpiresOn);

/// <summary>Body of POST /api/staff/{id}/shares: role assignments at units outside the home subtree (D29).</summary>
public sealed record ShareStaffRequest(IReadOnlyList<StaffRoleInput> Targets);

/// <summary>Query of GET /api/staff. <c>Sort</c> from <see cref="StaffSorts"/>; page size max 200.</summary>
public sealed record StaffQuery(
    Guid? OrgUnitId,
    bool IncludeDescendants = true,
    Guid? RoleId = null,
    string? Status = null,
    string? Search = null,
    bool IncludeShared = true,
    string? Sort = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);
