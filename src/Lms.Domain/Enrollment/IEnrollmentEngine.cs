using Lms.Domain.Classes;
using Lms.Domain.People;

namespace Lms.Domain.Enrollment;

/// <summary>A class course a student was not enrolled in because the course restrictions are not met.</summary>
public sealed record RestrictionSkip(Guid StudentId, Guid ClassCourseId, IReadOnlyList<string> FailedTypes);

public sealed record PlacementResult(ClassStudent Placement, int EnrollmentsCreated, IReadOnlyList<RestrictionSkip> Skips, bool OverCapacity);

/// <summary>
/// The only writer of <see cref="CourseEnrollment"/> and <see cref="ClassStudent"/> rows (implemented in Task 24): class
/// placement, automatic and manual enrollment, drops and transfers. Restrictions are checked on every path.
/// </summary>
public interface IEnrollmentEngine
{
    Task<PlacementResult> PlaceAsync(
        StudentProfile student, Class cls, DateOnly effectiveOn, bool confirmOverCapacity, CancellationToken cancellationToken);

    /// <summary>
    /// Places many students in one go (all or nothing): one query per kind of data, one save. The students must all be
    /// Active, none placed in the class's year yet, and the class capacity (mode Block/Warn) is checked for the whole set.
    /// </summary>
    Task<IReadOnlyList<PlacementResult>> PlaceManyAsync(
        IReadOnlyList<StudentProfile> students, Class cls, DateOnly effectiveOn, bool confirmOverCapacity, CancellationToken cancellationToken);

    /// <param name="source">One of <c>EnrollmentSources</c> (Manual or Exceptional).</param>
    Task<CourseEnrollment> EnrollManualAsync(ClassCourse classCourse, StudentProfile student, string source, CancellationToken cancellationToken);

    Task DropAsync(CourseEnrollment enrollment, string reason, DateOnly on, CancellationToken cancellationToken);

    Task RemoveFromClassAsync(ClassStudent placement, string reason, DateOnly on, CancellationToken cancellationToken);

    Task<StudentTransfer> TransferAsync(
        StudentProfile student, Class to, DateOnly on, string reason, bool confirmOverCapacity, CancellationToken cancellationToken);
}
