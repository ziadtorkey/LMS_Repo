namespace Lms.Shared.People;

/// <summary>
/// One changed field: <c>Field</c> from <see cref="StudentFields"/>; values as strings (<c>yyyy-MM-dd</c> dates, catalog
/// keys, file ids).
/// </summary>
public sealed record FieldChangeDto(string Field, string? Before, string? After);

/// <summary>A student's request to change critical data (FR-STU-04). <c>Status</c> from <see cref="ChangeRequestStatuses"/>.</summary>
public sealed record StudentChangeRequestDto(
    Guid Id,
    Guid StudentId,
    string StudentCode,
    string StudentNameAr,
    string StudentNameEn,
    string HomeOrgUnitNameAr,
    string HomeOrgUnitNameEn,
    string Status,
    IReadOnlyList<FieldChangeDto> Changes,
    string? Note,
    DateTime CreatedAt,
    string? DecidedByNameAr,
    string? DecidedByNameEn,
    DateTime? DecidedAt,
    string? DecisionComment);

/// <summary>Body of POST /api/me/student-profile/change-requests: field → new value (fields must be requestable).</summary>
public sealed record CreateChangeRequestRequest(IReadOnlyDictionary<string, string?> Changes, string? Note);

/// <summary>Body of approve/reject; a comment is required to reject.</summary>
public sealed record DecideChangeRequestRequest(string? Comment);

/// <summary>
/// GET /api/me/student-profile. <c>EditableFields</c> = requestable fields that are not critical (edited directly);
/// <c>RequestableFields</c> = critical fields (changed through a request).
/// </summary>
public sealed record MyStudentProfileDto(
    StudentDetailDto Student,
    IReadOnlyList<string> EditableFields,
    IReadOnlyList<string> RequestableFields);

/// <summary>Body of PUT /api/me/student-profile: field → new value (only editable fields; critical ones → 400 <c>critical_field</c>).</summary>
public sealed record UpdateMyStudentProfileRequest(IReadOnlyDictionary<string, string?> Changes);

/// <summary>Query of GET /api/student-change-requests. <c>Status</c> null = all.</summary>
public sealed record ChangeRequestQuery(
    Guid? OrgUnitId,
    string? Status = ChangeRequestStatuses.Pending,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);
