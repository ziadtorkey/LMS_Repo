namespace Lms.Shared.Courses;

/// <summary>
/// One course. <c>Language</c> is ISO 639-1. <c>IsInherited</c> = owned at an ancestor of the queried unit (read-only
/// here, D37). <c>Status</c> from <see cref="CourseStatuses"/>.
/// </summary>
public sealed record CourseSummaryDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    string Language,
    int WeeklyPeriods,
    Guid OwnerOrgUnitId,
    string OwnerOrgUnitNameAr,
    string OwnerOrgUnitNameEn,
    string Status,
    bool IsInherited,
    int AllocationCount,
    bool HasPublishedBlueprint);

/// <summary>GET /api/courses/{id}. <c>CanEdit</c> = the caller may edit at the owner unit (and supervises it, for Supervisors).</summary>
public sealed record CourseDetailDto(
    CourseSummaryDto Summary,
    string? DescriptionAr,
    string? DescriptionEn,
    IReadOnlyList<CourseRestrictionDto> Restrictions,
    IReadOnlyList<CourseSupervisorDto> Supervisors,
    bool CanEdit);

/// <summary>
/// Body of POST and PUT /api/courses. Code <c>^[A-Za-z0-9_-]{1,50}$</c> (stored upper-case); <c>WeeklyPeriods</c>
/// 0..40; owner = client root or a course-management level (D37). <c>AllocateToOrgUnitIds</c> allocates at creation
/// (ignored on update).
/// </summary>
public sealed record SaveCourseRequest(
    string Code,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Language,
    int WeeklyPeriods,
    Guid OwnerOrgUnitId,
    IReadOnlyList<Guid>? AllocateToOrgUnitIds);

/// <summary>Body of PUT /api/courses/{id}/status (<see cref="CourseStatuses"/>).</summary>
public sealed record SetCourseStatusRequest(string Status);

/// <summary>
/// Query of GET /api/courses. <c>IncludeInherited</c> adds courses available at the unit from ancestors;
/// <c>SupervisedByMe</c> = only courses the caller supervises. Sort from <see cref="CourseSorts"/>.
/// </summary>
public sealed record CourseQuery(
    Guid? OrgUnitId,
    bool IncludeInherited = true,
    string? Status = null,
    string? Search = null,
    bool SupervisedByMe = false,
    string? Sort = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>An allocation of a course at a unit. <c>IsInherited</c> = the allocation is at an ancestor of the queried unit.</summary>
public sealed record CourseAllocationDto(
    Guid Id,
    Guid CourseId,
    string CourseCode,
    string CourseNameAr,
    string CourseNameEn,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    bool IsInherited,
    int ClassCourseCount);

/// <summary>Body of POST /api/course-allocations: allocates each course at the unit.</summary>
public sealed record AllocateCoursesRequest(Guid OrgUnitId, IReadOnlyList<Guid> CourseIds);

/// <summary>GET /api/org-units/{id}/courses: courses available here (allocated here or at an ancestor) and allocated below.</summary>
public sealed record UnitCoursesDto(IReadOnlyList<CourseAllocationDto> Available, IReadOnlyList<CourseAllocationDto> AllocatedBelow);

/// <summary>One restriction of a course; <c>Type</c> from <see cref="CourseRestrictionTypes"/>.</summary>
public sealed record CourseRestrictionInput(string Type, IReadOnlyList<string> Values);

public sealed record CourseRestrictionDto(string Type, IReadOnlyList<string> Values);

/// <summary>Body of PUT /api/courses/{id}/restrictions: replaces all restrictions (empty = unrestricted).</summary>
public sealed record SetCourseRestrictionsRequest(IReadOnlyList<CourseRestrictionInput> Restrictions);

public sealed record CourseSupervisorDto(Guid UserId, string NameAr, string NameEn, string? Email);

/// <summary>Body of PUT /api/courses/{id}/supervisors: replaces the supervisor list.</summary>
public sealed record SetCourseSupervisorsRequest(IReadOnlyList<Guid> UserIds);
