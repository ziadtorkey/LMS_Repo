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

    /// <summary>Courses allocated at the unit or an ancestor, with the allocation that makes each available.</summary>
    Task<IReadOnlyList<AvailableCourse>> AvailableAtAsync(OrgUnit unit, CancellationToken cancellationToken = default);
}

public sealed record AvailableCourse(CourseAllocation Allocation, Course Course, bool IsInherited);
