using Lms.Domain.Grading;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>
/// The version of a student's row: the sum of the versions of all their entries in the gradebook, <b>deleted ones included</b>. Every cell change raises
/// an entry's version and deleting an item bumps its entries before they are soft-deleted, so the sum never falls: a stale version can never match
/// again after the row changed (no lost update).
/// </summary>
public static class GradeRowVersions
{
    public static async Task<IReadOnlyDictionary<Guid, long>> LoadAsync(
        LmsDbContext db, Gradebook gradebook, IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the soft-deleted entries of an item that was removed still count towards the row version; bound to this gradebook, its client and the students asked for.
        var sums = await db.Set<GradeEntry>().IgnoreQueryFilters()
            .Where(e => e.OrganizationId == gradebook.OrganizationId && e.GradebookId == gradebook.Id && studentIds.Contains(e.StudentProfileId))
            .GroupBy(e => e.StudentProfileId)
            .Select(g => new { Student = g.Key, Version = g.Sum(e => (long)e.Version) })
            .ToListAsync(ct);
        return sums.ToDictionary(x => x.Student, x => x.Version);
    }
}
