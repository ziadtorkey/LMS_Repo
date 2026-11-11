using Lms.Shared.Courses;
using Lms.Shared.Teaching;

namespace Lms.Shared.Classes;

public static class ClassStatuses
{
    public const string Active = "Active";
    public const string Closed = "Closed";

    public static IReadOnlyList<string> All { get; } = [Active, Closed];
}

/// <summary>Status of a student's membership in a class (M7 adds Promoted, Graduated, Withdrawn).</summary>
public static class ClassStudentStatuses
{
    public const string Active = "Active";
    public const string TransferredOut = "TransferredOut";
    public const string Removed = "Removed";

    public static IReadOnlyList<string> All { get; } = [Active, TransferredOut, Removed];
}

/// <summary>
/// One class. <c>IsReadOnly</c> = the class or its academic year is Closed (writes answer 409). <c>Status</c> from
/// <see cref="ClassStatuses"/>.
/// </summary>
public sealed record ClassSummaryDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    Guid AcademicYearId,
    string YearNameAr,
    string YearNameEn,
    int? Capacity,
    int StudentCount,
    int CourseCount,
    Guid? HomeroomTeacherUserId,
    string? HomeroomTeacherNameAr,
    string? HomeroomTeacherNameEn,
    string Status,
    bool IsReadOnly);

/// <summary>
/// Body of POST and PUT /api/classes. Code <c>^[A-Za-z0-9_-]{1,50}$</c> (stored upper-case), unique among live classes
/// of the year; <c>Capacity</c> 1..500; the year must be effective at the unit.
/// </summary>
public sealed record SaveClassRequest(
    string Code,
    string NameAr,
    string NameEn,
    Guid OrgUnitId,
    Guid AcademicYearId,
    int? Capacity,
    Guid? HomeroomTeacherUserId);

/// <summary>Body of POST /api/classes/bulk: classes From..To (at most 50); <c>{N}</c> exactly once in each pattern.</summary>
public sealed record BulkCreateClassesRequest(
    Guid OrgUnitId,
    Guid AcademicYearId,
    string NamePatternAr,
    string NamePatternEn,
    string CodePattern,
    int From,
    int To,
    int? Capacity);

/// <summary>
/// Body of POST /api/classes/copy: copies the unit's classes (at most 500) from one year to another;
/// <c>ReplaceFrom</c>→<c>ReplaceTo</c> is a text replace on names and codes (e.g. <c>2026</c> → <c>2027</c>).
/// </summary>
public sealed record CopyClassesRequest(
    Guid SourceYearId,
    Guid TargetYearId,
    Guid OrgUnitId,
    bool IncludeCourses,
    string? ReplaceFrom,
    string? ReplaceTo);

/// <summary>Result of the copy; <c>SkippedCodes</c> already existed in the target year.</summary>
public sealed record CopyClassesResultDto(int Created, IReadOnlyList<string> SkippedCodes);

/// <summary>Query of GET /api/classes. <c>AcademicYearId</c> default: the current year of the queried unit.</summary>
public sealed record ClassQuery(
    Guid? OrgUnitId,
    bool IncludeDescendants = true,
    Guid? AcademicYearId = null,
    string? Status = null,
    string? Search = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>A course taught in a class. <c>LessonPlanVersion</c> = the blueprint version of its plan (null = none yet).</summary>
public sealed record ClassCourseDto(
    Guid Id,
    Guid ClassId,
    string ClassNameAr,
    string ClassNameEn,
    Guid CourseId,
    string CourseCode,
    string CourseNameAr,
    string CourseNameEn,
    bool IsOptional,
    int EnrolledCount,
    IReadOnlyList<ClassCourseTeacherDto> CurrentTeachers,
    int? LessonPlanVersion);

/// <summary>Body of POST /api/classes/{id}/courses: courses must be available at the class's unit.</summary>
public sealed record AddClassCoursesRequest(IReadOnlyList<Guid> CourseIds, bool IsOptional);

/// <summary>Body of PUT /api/class-courses/{id}.</summary>
public sealed record UpdateClassCourseRequest(bool IsOptional);

/// <summary>Body of POST /api/class-courses/bulk-assign: adds the course to every live class at the unit and below.</summary>
public sealed record BulkAssignCourseRequest(Guid OrgUnitId, Guid AcademicYearId, Guid CourseId, bool IsOptional);

/// <summary>GET /api/classes/matrix: classes × available courses; a cell exists where the class has the course.</summary>
public sealed record DistributionMatrixDto(
    IReadOnlyList<ClassSummaryDto> Classes,
    IReadOnlyList<CourseSummaryDto> Courses,
    IReadOnlyList<MatrixCellDto> Cells);

public sealed record MatrixCellDto(Guid ClassId, Guid CourseId, Guid ClassCourseId, bool IsOptional);
