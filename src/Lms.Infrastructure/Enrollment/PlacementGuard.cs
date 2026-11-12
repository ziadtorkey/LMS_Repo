using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Enrollment;

/// <summary>
/// The reads the enrollment engine needs that lie outside the caller's unit scope: the academic year of a class (defined
/// at an ancestor unit) and the restrictions of its courses (owned by an ancestor unit). Each is matched to one client and
/// excludes deleted rows by hand; nothing from them is returned to the caller except through the engine's own rules.
/// </summary>
public sealed class PlacementGuard(LmsDbContext db)
{
    /// <summary>The class can take writes: it is Active and its academic year is not Closed.</summary>
    public async Task<AcademicYear> EnsureWritableAsync(Class cls, CancellationToken ct)
    {
        if (cls.Status != Class.ActiveStatus)
        {
            throw new EnrollmentRuleException(ProblemCodes.class_closed);
        }

        // ISOLATION-BYPASS: the year of a class can be defined at an ancestor unit above the caller's scope; the client
        // is matched explicitly and a deleted year is treated like a closed one.
        var year = await db.Set<AcademicYear>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(y => y.Id == cls.AcademicYearId && y.OrganizationId == cls.OrganizationId && !y.IsDeleted, ct);
        if (year is null || year.Status == AcademicYearStatus.Closed)
        {
            throw new EnrollmentRuleException(ProblemCodes.year_closed);
        }

        return year;
    }

    /// <summary>Course codes by course id, for the transfer preview. The course sits at an ancestor unit above the caller's scope.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> CourseCodesAsync(
        Guid organizationId, IReadOnlyCollection<Guid> courseIds, CancellationToken ct)
    {
        if (courseIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // ISOLATION-BYPASS: only the codes of courses the caller already reaches through their class courses; client
        // matched, deleted courses excluded by hand.
        return await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => c.OrganizationId == organizationId && !c.IsDeleted && courseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct);
    }

    /// <summary>The restrictions of the courses, by course id (a course without rows has none).</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<(string Type, IReadOnlyList<string> Values)>>> RestrictionsAsync(
        Guid organizationId, IReadOnlyCollection<Guid> courseIds, CancellationToken ct)
    {
        if (courseIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<(string, IReadOnlyList<string>)>>();
        }

        // ISOLATION-BYPASS: the course (and so its restrictions) is owned by a unit above the class; an invisible
        // restriction must never turn into "no restriction". Client matched, deleted rows excluded by hand.
        var rows = await db.Set<CourseRestriction>().IgnoreQueryFilters()
            .Where(r => r.OrganizationId == organizationId && !r.IsDeleted && courseIds.Contains(r.CourseId))
            .ToListAsync(ct);
        return rows.GroupBy(r => r.CourseId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<(string Type, IReadOnlyList<string> Values)>)g.Select(r => (r.Type, r.Values)).ToList());
    }
}
