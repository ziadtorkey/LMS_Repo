namespace Lms.Shared.Enrollment;

/// <summary>How an enrollment was created (FR-ENR-01..05).</summary>
public static class EnrollmentSources
{
    public const string Auto = "Auto";
    public const string Manual = "Manual";
    public const string Exceptional = "Exceptional";
    public const string Transfer = "Transfer";

    public static IReadOnlyList<string> All { get; } = [Auto, Manual, Exceptional, Transfer];
}

public static class EnrollmentStatuses
{
    public const string Active = "Active";
    public const string Dropped = "Dropped";

    public static IReadOnlyList<string> All { get; } = [Active, Dropped];
}

public static class ExceptionStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public static IReadOnlyList<string> All { get; } = [Pending, Approved, Rejected, Cancelled];
}

/// <summary>A student in a class. <c>Status</c> from <c>ClassStudentStatuses</c>.</summary>
public sealed record ClassStudentDto(
    Guid Id,
    Guid StudentId,
    string StudentCode,
    string NameAr,
    string NameEn,
    string Gender,
    DateOnly From,
    DateOnly? To,
    string Status);

/// <summary>
/// Body of POST /api/classes/{id}/students (at most 500). Beyond capacity: <c>Block</c> → 409 <c>class_full</c>;
/// <c>Warn</c> → 409 <c>over_capacity</c> unless <c>ConfirmOverCapacity</c>.
/// </summary>
public sealed record AddStudentsToClassRequest(IReadOnlyList<Guid> StudentIds, DateOnly? EffectiveOn, bool ConfirmOverCapacity);

/// <summary>A student not auto-enrolled in a ClassCourse because of restrictions (<c>CourseRestrictionTypes</c> values).</summary>
public sealed record RestrictionSkipDto(Guid StudentId, Guid ClassCourseId, IReadOnlyList<string> FailedTypes);

public sealed record AddStudentsResultDto(
    int Added,
    int EnrollmentsCreated,
    IReadOnlyList<RestrictionSkipDto> RestrictionSkips,
    bool OverCapacity);

/// <summary>Body of POST /api/classes/{id}/students/{classStudentId}/remove: drops the class's enrollments too.</summary>
public sealed record RemoveFromClassRequest(DateOnly EffectiveOn, string Reason);

/// <summary>One enrollment. <c>Source</c> from <see cref="EnrollmentSources"/>, <c>Status</c> from <see cref="EnrollmentStatuses"/>.</summary>
public sealed record EnrollmentDto(
    Guid Id,
    Guid ClassCourseId,
    Guid StudentId,
    string StudentCode,
    string StudentNameAr,
    string StudentNameEn,
    string CourseCode,
    string CourseNameAr,
    string CourseNameEn,
    string ClassNameAr,
    string ClassNameEn,
    string Source,
    string Status,
    DateOnly From,
    DateOnly? To,
    string? DropReason,
    Guid? GroupId,
    string? GroupNameAr,
    string? GroupNameEn);

/// <summary>Body of POST /api/class-courses/{id}/enrollments: manual enrollment in an optional course.</summary>
public sealed record EnrollStudentsRequest(IReadOnlyList<Guid> StudentIds);

/// <summary>Body of POST /api/enrollments/{id}/drop; <c>EffectiveOn</c> default today.</summary>
public sealed record DropEnrollmentRequest(string Reason, DateOnly? EffectiveOn);

/// <summary>Query of GET /api/enrollments. <c>Status</c> null = all.</summary>
public sealed record EnrollmentQuery(
    Guid? StudentId,
    Guid? ClassCourseId,
    string? Status = EnrollmentStatuses.Active,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>Body of POST /api/class-courses/{id}/exceptions: enroll a student who fails a restriction (FR-CRS-03).</summary>
public sealed record RequestExceptionRequest(Guid StudentId, string Reason);

/// <summary>Body of approve/reject/cancel of an exception; a comment is required to reject.</summary>
public sealed record DecideExceptionRequest(string? Comment);

/// <summary>An enrollment exception request. <c>Status</c> from <see cref="ExceptionStatuses"/>.</summary>
public sealed record EnrollmentExceptionDto(
    Guid Id,
    Guid ClassCourseId,
    Guid StudentId,
    string StudentCode,
    string StudentNameAr,
    string StudentNameEn,
    string CourseNameAr,
    string CourseNameEn,
    string ClassNameAr,
    string ClassNameEn,
    IReadOnlyList<string> FailedTypes,
    string Reason,
    string Status,
    string? RequestedByNameAr,
    string? RequestedByNameEn,
    DateTime CreatedAt,
    string? DecidedByNameAr,
    string? DecidedByNameEn,
    DateTime? DecidedAt,
    string? DecisionComment);

/// <summary>Query of GET /api/enrollment-exceptions. <c>Status</c> null = all.</summary>
public sealed record ExceptionQuery(
    Guid? OrgUnitId,
    string? Status = ExceptionStatuses.Pending,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>Body of POST /api/transfers and /api/transfers/preview (mid-year move between classes of one year).</summary>
public sealed record TransferStudentRequest(
    Guid StudentId,
    Guid ToClassId,
    DateOnly EffectiveOn,
    string Reason,
    bool ConfirmOverCapacity);

/// <summary>What a transfer does to the student's enrollments, by course code.</summary>
public sealed record TransferPreviewDto(
    IReadOnlyList<string> KeptCourseCodes,
    IReadOnlyList<string> DroppedCourseCodes,
    IReadOnlyList<string> AddedCourseCodes,
    IReadOnlyList<string> RestrictedCourseCodes,
    bool OverCapacity);

public sealed record StudentTransferDto(
    Guid Id,
    Guid StudentId,
    string FromClassNameAr,
    string FromClassNameEn,
    string ToClassNameAr,
    string ToClassNameEn,
    DateOnly EffectiveOn,
    string Reason,
    string? ByNameAr,
    string? ByNameEn,
    DateTime CreatedAt);
