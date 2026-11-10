namespace Lms.Shared.Organizations;

/// <summary>
/// One node of the org tree (FR-ORG-03/09). <c>BlockedPermissions</c> = permissions blocked at this unit by its
/// own or ancestor policies, so the UI can hide actions (the server still enforces them).
/// </summary>
public sealed record OrgUnitNodeDto(
    Guid Id,
    Guid? ParentId,
    Guid LevelTypeId,
    string NameAr,
    string NameEn,
    string Code,
    int Depth,
    string Status,
    bool IsLevelTransfer,
    Guid? NextTransferUnitId,
    bool IsCourseManagementLevel,
    bool HasChildren,
    IReadOnlyList<string> BlockedPermissions);

/// <summary>Body of POST /api/org-units.</summary>
public sealed record CreateOrgUnitRequest(Guid ParentId, Guid LevelTypeId, string NameAr, string NameEn, string Code);

/// <summary>Body of PUT /api/org-units/{id}.</summary>
public sealed record UpdateOrgUnitRequest(Guid LevelTypeId, string NameAr, string NameEn, string Code);

/// <summary>Body of PUT /api/org-units/{id}/status (values from <see cref="OrgUnitStatuses"/>).</summary>
public sealed record SetOrgUnitStatusRequest(string Status);

/// <summary>Body of PUT /api/org-units/{id}/move.</summary>
public sealed record MoveOrgUnitRequest(Guid NewParentId);

/// <summary>Body of PUT /api/org-units/{id}/academic-flags (FR-ORG-05, FR-ORG-09).</summary>
public sealed record OrgUnitAcademicFlagsRequest(bool IsLevelTransfer, Guid? NextTransferUnitId, bool IsCourseManagementLevel);
