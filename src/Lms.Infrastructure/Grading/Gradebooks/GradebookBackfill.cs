using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>
/// Creates the items of grade events that were recorded before the gradebook existed (M5 L4). Only the grid read of a caller who may manage
/// grades runs it, never in a closed class or year, and never for students, guardians or reports. Idempotent: the unique key
/// (gradebook, source kind, source) turns a concurrent run into a no-op, which is swallowed and left to the winner.
/// </summary>
public sealed class GradebookBackfill(LmsDbContext db, GradebookItemsObserver items, EventTermResolver terms, PlacementGuard placement)
{
    /// <summary>The entry point of a grid read: only a caller who may manage grades backfills; every other read stays read-only (L4).</summary>
    public Task<int> RunForReaderAsync(ClassCourse classCourse, bool canManage, CancellationToken ct) =>
        canManage ? RunAsync(classCourse, ct) : Task.FromResult(0);

    /// <returns>The number of items created.</returns>
    public async Task<int> RunAsync(ClassCourse classCourse, CancellationToken ct)
    {
        var cls = await db.Set<Class>().SingleAsync(c => c.Id == classCourse.ClassId, ct);
        try
        {
            await placement.EnsureWritableAsync(cls, ct);
        }
        catch (Lms.Domain.Enrollment.EnrollmentRuleException)
        {
            return 0; // a closed class or year never gets rows from a read
        }

        // The first attempt of a source decides its term (the observer does the same); the latest revision decides its maximum.
        var sources = await db.Set<GradeEvent>().Where(e => e.ClassCourseId == classCourse.Id)
            .GroupBy(e => new { e.SourceKind, e.SourceId })
            .Select(g => new { g.Key.SourceKind, g.Key.SourceId, FirstScoredAtUtc = g.Min(e => e.ScoredAtUtc), MaxPoints = g.OrderByDescending(e => e.Revision).Select(e => e.MaxPoints).First() })
            .ToListAsync(ct);

        var created = 0;
        foreach (var source in sources)
        {
            if (await items.FindExistingAsync(classCourse, source.SourceKind, source.SourceId, ct) is { } existing)
            {
                existing.SetMaxPoints(source.MaxPoints);
                continue;
            }

            var term = await terms.ResolveAsync(classCourse, source.FirstScoredAtUtc, ct);
            if (term is null)
            {
                continue;
            }

            var before = db.ChangeTracker.Entries<GradeItem>().Count(e => e.State == EntityState.Added);
            await items.EnsureItemAsync(classCourse, term.Id, source.SourceKind, source.SourceId, source.MaxPoints, ct);
            created += db.ChangeTracker.Entries<GradeItem>().Count(e => e.State == EntityState.Added) - before;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent backfill created the same rows first: drop ours; the caller re-reads what is stored.
            db.ChangeTracker.Clear();
            return 0;
        }

        return created;
    }
}
