namespace Lms.Shared.People;

/// <summary>
/// The editable data of a student. <c>Gender</c> from <see cref="Genders"/>, <c>Religion</c> from <see cref="Religions"/>,
/// <c>Nationality</c> a <see cref="Nationalities"/> code. Names 1–200 chars in both languages.
/// </summary>
public sealed record StudentDataInput(
    string NameAr,
    string NameEn,
    string? NicknameAr,
    string? NicknameEn,
    DateOnly DateOfBirth,
    string Gender,
    string Religion,
    string Nationality,
    string? NationalId,
    string? Mobile,
    string? Email,
    Guid? PhotoFileId);

/// <summary>
/// Body of POST /api/students. <c>StudentCode</c> must be empty when <c>Students.CodeMode</c> is <c>Auto</c> and is
/// required when it is <c>Manual</c> (D25). <c>ClassId</c> places the student (optional, D45). <c>DefaultPassword</c>
/// blank = a strong password is generated (D24, D45). <c>ConfirmPossibleDuplicate</c> answers 409
/// <c>possible_duplicate</c>. <c>ConfirmOverCapacity</c> answers 409 <c>over_capacity</c> when
/// <c>ClassId</c> is full and <c>Classes.CapacityMode</c> is <c>Warn</c> (as for <see cref="Lms.Shared.Enrollment.AddStudentsToClassRequest"/>).
/// </summary>
public sealed record CreateStudentRequest(
    StudentDataInput Data,
    Guid HomeOrgUnitId,
    string? StudentCode,
    bool ConfirmPossibleDuplicate,
    IReadOnlyList<GuardianLinkInput> Guardians,
    Guid? ClassId = null,
    string? DefaultPassword = null,
    bool ConfirmOverCapacity = false);

/// <summary>Body of PUT /api/students/{id}.</summary>
public sealed record UpdateStudentRequest(
    StudentDataInput Data,
    Guid HomeOrgUnitId,
    string? StudentCode,
    bool ConfirmPossibleDuplicate);

/// <summary>One row of GET /api/students. The current class is the student's active class in the queried year.</summary>
public sealed record StudentSummaryDto(
    Guid Id,
    string StudentCode,
    string NameAr,
    string NameEn,
    DateOnly DateOfBirth,
    string Gender,
    Guid HomeOrgUnitId,
    string HomeOrgUnitNameAr,
    string HomeOrgUnitNameEn,
    string Status,
    Guid? PhotoFileId,
    string? PrimaryGuardianNameAr,
    string? PrimaryGuardianNameEn,
    string? PrimaryGuardianMobile,
    Guid? CurrentClassId,
    string? CurrentClassNameAr,
    string? CurrentClassNameEn);

/// <summary>
/// GET /api/students/{id}. <c>FeatureFlagOverrides</c> holds only the flags overridden for this student (§4.4).
/// <c>MustChangePassword</c> = the default password was not changed yet (D24).
/// </summary>
public sealed record StudentDetailDto(
    StudentSummaryDto Summary,
    string? NicknameAr,
    string? NicknameEn,
    string Religion,
    string Nationality,
    string? NationalId,
    string? Mobile,
    string? Email,
    bool HasAccount,
    bool IsActivated,
    bool MustChangePassword,
    IReadOnlyDictionary<string, bool> FeatureFlagOverrides,
    IReadOnlyList<StudentGuardianDto> Guardians,
    IReadOnlyList<PersonStatusChangeDto> StatusHistory,
    int PendingChangeRequests);

/// <summary>Where the student's credentials were emailed (D24).</summary>
public static class CredentialsDelivery
{
    public const string Student = "Student";
    public const string PrimaryGuardian = "PrimaryGuardian";

    /// <summary>No email: the school hands the credentials over (shown once in the response).</summary>
    public const string None = "None";

    public static IReadOnlyList<string> All { get; } = [Student, PrimaryGuardian, None];
}

/// <summary>
/// Response of POST /api/students and POST /api/students/{id}/reset-password: the only DTO that ever carries a password
/// (D24). <c>IssuedPassword</c> is shown once and never returned again. <c>CredentialsSentTo</c> from
/// <see cref="CredentialsDelivery"/>.
/// </summary>
/// <summary>Result of creating a student: the issued sign-in name (D47: <c>studentcode@clientshortcode</c>) and the
/// default password, shown once (D24, D45).</summary>
public sealed record StudentCreatedDto(StudentDetailDto Student, string IssuedPassword, string CredentialsSentTo, string UserName = "");

/// <summary>Body of POST /api/students/{id}/reset-password; blank = a strong password is generated.</summary>
public sealed record ResetStudentPasswordRequest(string? DefaultPassword);

/// <summary>Body of PUT /api/students/{id}/feature-flags: flag name → value, null = inherit (§4.4).</summary>
public sealed record SetStudentFeatureFlagsRequest(IReadOnlyDictionary<string, bool?> Overrides);

/// <summary>
/// Query of GET /api/students. Sort from <see cref="StudentSorts"/>. <c>ClassId</c> = students active in that class;
/// <c>Unplaced</c> = students with no active class in <c>AcademicYearId</c> (default: the unit's current year).
/// </summary>
public sealed record StudentQuery(
    Guid? OrgUnitId,
    bool IncludeDescendants = true,
    string? Status = null,
    string? Gender = null,
    string? Search = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    Guid? ClassId = null,
    bool Unplaced = false,
    Guid? AcademicYearId = null);
