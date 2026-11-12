using Lms.Domain.Access;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;

namespace Lms.Infrastructure.People.Guardians;

/// <summary>
/// The only entry point for reading guardians through the API. <c>GuardianProfile</c> is tenant-owned (client-wide table
/// filter), so a guardian is visible to a caller only through a live <c>StudentGuardian</c> row in the caller's scope; the link
/// table carries the unit scope and the soft-delete filter, so a guardian whose children are all elsewhere is invisible.
/// </summary>
public static class GuardianVisibility
{
    public static IQueryable<GuardianProfile> Visible(LmsDbContext db) =>
        db.Set<GuardianProfile>().Where(g => db.Set<StudentGuardian>().Any(l => l.GuardianId == g.Id));

    /// <summary>
    /// Visible guardians for a caller whose permission is limited by a relationship (D32/D42: a Teacher reaches only students
    /// through their own class courses or homeroom class): only guardians linked to a student the caller reaches. An unrestricted
    /// filter is <see cref="Visible(LmsDbContext)"/>.
    /// </summary>
    public static IQueryable<GuardianProfile> Visible(LmsDbContext db, RelationshipFilter filter)
    {
        if (filter.IsUnrestricted)
        {
            return Visible(db);
        }

        var reachable = ReachableStudentIds(db, filter);
        return db.Set<GuardianProfile>().Where(g =>
            db.Set<StudentGuardian>().Any(l => l.GuardianId == g.Id && reachable.Contains(l.StudentId)));
    }

    /// <summary>The students the filter reaches (all of the unit scope when unrestricted).</summary>
    public static IQueryable<Guid> ReachableStudentIds(LmsDbContext db, RelationshipFilter filter) =>
        filter.Students(db.Set<StudentProfile>(), db).Select(s => s.Id);
}
