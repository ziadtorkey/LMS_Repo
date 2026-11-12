namespace Lms.Shared.Teaching;

/// <summary>Role of a teacher on a ClassCourse: one open primary teacher, any number of assistants (FR-TCH-02).</summary>
public static class TeacherRoles
{
    public const string Primary = "Primary";
    public const string Assistant = "Assistant";

    public static IReadOnlyList<string> All { get; } = [Primary, Assistant];
}

/// <summary>A teacher may teach a course at a unit and below (FR-TCH-01).</summary>
public sealed record TeacherEligibilityDto(
    Guid Id,
    Guid UserId,
    string TeacherNameAr,
    string TeacherNameEn,
    Guid CourseId,
    string CourseCode,
    string CourseNameAr,
    string CourseNameEn,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn);

/// <summary>Body of POST /api/teacher-eligibility: every teacher × every course (at most 200 pairs).</summary>
public sealed record GrantEligibilityRequest(Guid OrgUnitId, IReadOnlyList<Guid> UserIds, IReadOnlyList<Guid> CourseIds);

/// <summary>
/// Query of GET /api/teacher-eligibility. By default <c>OrgUnitId</c> returns eligibilities granted at that unit and its descendants.
/// With <c>IncludeAncestors</c> = true it instead returns the eligibilities that apply to a class at <c>OrgUnitId</c>:
/// those granted at that unit or any ancestor (the same rule teacher assignment enforces).
/// </summary>
public sealed record EligibilityQuery(
    Guid? OrgUnitId,
    Guid? UserId,
    Guid? CourseId,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    bool IncludeAncestors = false);

/// <summary>A dated teacher assignment of a ClassCourse; <c>To</c> null = open. <c>Role</c> from <see cref="TeacherRoles"/>.</summary>
public sealed record ClassCourseTeacherDto(
    Guid Id,
    Guid UserId,
    string NameAr,
    string NameEn,
    string Role,
    DateOnly From,
    DateOnly? To,
    bool IsCurrent);

/// <summary>Body of POST /api/class-courses/{id}/teachers; <c>From</c> default today.</summary>
public sealed record AssignTeacherRequest(Guid UserId, string Role, DateOnly? From);

/// <summary>Body of POST …/teachers/{assignmentId}/replace: ends the assignment the day before and opens the new one.</summary>
public sealed record ReplaceTeacherRequest(Guid NewUserId, DateOnly EffectiveOn);

/// <summary>Body of POST …/teachers/{assignmentId}/end.</summary>
public sealed record EndTeacherRequest(DateOnly LastDay);

/// <summary>Body of POST /api/teacher-assignments/bulk (at most 200 ClassCourses).</summary>
public sealed record BulkAssignTeacherRequest(Guid UserId, string Role, IReadOnlyList<Guid> ClassCourseIds);

/// <summary>Result of the bulk teacher assignment: assignments created, and ClassCourses skipped because the teacher already
/// holds an open assignment there.</summary>
public sealed record BulkAssignTeacherResultDto(int Assigned, int Skipped);

/// <summary>One row of GET /api/me/class-courses (the signed-in teacher's current ClassCourses).</summary>
public sealed record MyClassCourseDto(
    Guid ClassCourseId,
    Guid ClassId,
    string ClassNameAr,
    string ClassNameEn,
    string CourseNameAr,
    string CourseNameEn,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    string? TeacherRole,
    int StudentCount);
