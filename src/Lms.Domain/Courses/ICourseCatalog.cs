using Lms.Domain.Organizations;

namespace Lms.Domain.Courses;

/// <summary>A course as the caller sees it; <see cref="IsInherited"/> when an ancestor unit owns it.</summary>
public sealed record CourseView(Course Course, bool IsInherited);

/// <summary>
/// Reads courses across the ancestor chain: a unit sees the courses it owns (and below) plus those owned by its
/// ancestors, which the normal subtree filter cannot return. Never returns a sibling or another client course.
/// </summary>
public interface ICourseCatalog
{
    Task<CourseView?> FindAsync(Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>True when the course is allocated at the unit or at one of its ancestors.</summary>
    Task<bool> IsAvailableAtAsync(Guid courseId, OrgUnit unit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="IsAvailableAtAsync"/> for any unit path of the client, ignoring one allocation (used to ask whether a
    /// course stays available at a class unit when that allocation is removed).
    /// </summary>
    Task<bool> IsAvailableAtPathAsync(
        Guid organizationId, string unitPath, Guid courseId, Guid? exceptAllocationId, CancellationToken cancellationToken = default);

    /// <summary>Courses allocated at the unit or an ancestor, with the allocation that makes each available.</summary>
    Task<IReadOnlyList<AvailableCourse>> AvailableAtAsync(OrgUnit unit, CancellationToken cancellationToken = default);

    /// <summary>Visible courses, filtered and paged (the caller applies the permission and relationship rules).</summary>
    Task<CoursePage> ListVisibleAsync(CourseListQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter of <see cref="ICourseCatalog.ListVisibleAsync"/>. <see cref="OrgUnitPath"/> limits to courses owned at or below
/// that unit, plus those owned by its ancestors when <see cref="IncludeInherited"/>. <see cref="OnlyCourseIds"/> limits to
/// those ids (e.g. the courses a supervisor supervises).
/// </summary>
public sealed record CourseListQuery(
    string? OrgUnitPath, bool IncludeInherited, string? Status, string? Search, IReadOnlyCollection<Guid>? OnlyCourseIds,
    string? Sort, int Page, int PageSize);

public sealed record CoursePage(IReadOnlyList<CourseView> Items, int Total);

public sealed record AvailableCourse(CourseAllocation Allocation, Course Course, bool IsInherited);
