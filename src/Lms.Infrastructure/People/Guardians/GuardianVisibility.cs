using Lms.Domain.People;
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
}
