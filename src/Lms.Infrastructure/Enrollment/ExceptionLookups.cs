using Lms.Domain.Courses;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Enrollment;

/// <summary>
/// The reads of the exceptional-enrollment flow (D40) that lie outside the caller's unit scope: a course and its supervisors are
/// owned by a unit above the class, so a school-level approver could never see them through the filters. Each query matches the
/// client explicitly and excludes deleted rows; only names and user ids leave, and only through the endpoint's own rules.
/// </summary>
public sealed class ExceptionLookups(LmsDbContext db)
{
    public async Task<IReadOnlyDictionary<Guid, Course>> CoursesAsync(Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        // ISOLATION-BYPASS: the course is owned by an ancestor unit of the class; client matched, deleted rows excluded by hand.
        await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => c.OrganizationId == organizationId && !c.IsDeleted && ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

    public async Task<IReadOnlyList<Guid>> SupervisorUserIdsAsync(Guid organizationId, Guid courseId, CancellationToken ct) =>
        // ISOLATION-BYPASS: supervisor rows sit at the course owner unit, above the class; client matched, deleted rows excluded.
        await db.Set<CourseSupervisor>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && !s.IsDeleted && s.CourseId == courseId)
            .Select(s => s.UserId).ToListAsync(ct);

    /// <summary>The courses of the client that have at least one supervisor (their exceptions go to the supervisors, not the managers).</summary>
    public async Task<IReadOnlyList<Guid>> SupervisedCourseIdsAsync(Guid organizationId, CancellationToken ct) =>
        // ISOLATION-BYPASS: same as above, for every course of the client.
        await db.Set<CourseSupervisor>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && !s.IsDeleted)
            .Select(s => s.CourseId).Distinct().ToListAsync(ct);

    /// <summary>The courses the user supervises in the client.</summary>
    public async Task<IReadOnlyList<Guid>> SupervisedByAsync(Guid organizationId, Guid userId, CancellationToken ct) =>
        // ISOLATION-BYPASS: same as above, restricted to one user.
        await db.Set<CourseSupervisor>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && !s.IsDeleted && s.UserId == userId)
            .Select(s => s.CourseId).Distinct().ToListAsync(ct);
}
