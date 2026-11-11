namespace Lms.Shared.People;

/// <summary>The editable data of a guardian. Mobile is required (E.164); names 1–200 chars in both languages.</summary>
public sealed record GuardianDataInput(string NameAr, string NameEn, string Mobile, string? Email, string? NationalId);

/// <summary>
/// A guardian to link to a student: exactly one of <c>ExistingGuardianId</c> and <c>NewGuardian</c>.
/// <c>Relation</c> from <see cref="GuardianRelations"/>; one primary guardian per student.
/// </summary>
public sealed record GuardianLinkInput(Guid? ExistingGuardianId, GuardianDataInput? NewGuardian, string Relation, bool IsPrimary);

/// <summary>A guardian of a student. <c>OtherChildrenInScope</c> = siblings the caller can see.</summary>
public sealed record StudentGuardianDto(
    Guid GuardianId,
    string NameAr,
    string NameEn,
    string Relation,
    bool IsPrimary,
    string Mobile,
    string? Email,
    string? NationalId,
    bool HasPortalAccount,
    int OtherChildrenInScope);

/// <summary>Body of PUT /api/students/{id}/guardians/{guardianId}.</summary>
public sealed record UpdateStudentGuardianRequest(string Relation, bool IsPrimary);

/// <summary>One row of GET /api/guardians.</summary>
public sealed record GuardianSummaryDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Mobile,
    string? Email,
    int ChildrenInScope,
    bool HasPortalAccount);

/// <summary>A child of a guardian (inside the caller's scope).</summary>
public sealed record GuardianChildDto(
    Guid StudentId,
    string StudentCode,
    string NameAr,
    string NameEn,
    string Relation,
    bool IsPrimary,
    string HomeOrgUnitNameAr,
    string HomeOrgUnitNameEn);

/// <summary>GET /api/guardians/{id}.</summary>
public sealed record GuardianDetailDto(GuardianSummaryDto Summary, string? NationalId, IReadOnlyList<GuardianChildDto> Children);

/// <summary>Body of PUT /api/guardians/{id}.</summary>
public sealed record UpdateGuardianRequest(GuardianDataInput Data);

/// <summary>
/// A registered guardian matching a national id or mobile (GET /api/guardians/match). Reveals only the name and a
/// masked mobile (keep <c>+</c> and the last 3 digits, e.g. <c>+*********123</c>; M2 Review Focus 1).
/// </summary>
public sealed record GuardianMatchDto(Guid Id, string NameAr, string NameEn, string MaskedMobile, int ChildrenCount);

/// <summary>One child of the signed-in guardian (GET /api/me/children, FR-STU-06).</summary>
public sealed record MyChildDto(
    Guid StudentId,
    string StudentCode,
    string NameAr,
    string NameEn,
    string HomeOrgUnitNameAr,
    string HomeOrgUnitNameEn,
    Guid? PhotoFileId,
    string Relation);
