using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Grading;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>
/// Creates the gradebook item of a grade event source in the same unit of work as the event (M5 L4: no writes on read), so a new score never
/// waits for someone to open the grid. The item starts hidden in the Uncategorised category (L3). Idempotent: an item that exists is reused.
/// A year without terms has no gradebook yet; the backfill creates the items once it has.
/// </summary>
public sealed class GradebookItemsObserver(LmsDbContext db, EventTermResolver terms, IGradebookWriter writer) : IGradeEventObserver
{
    public async Task OnRecordedAsync(GradeEvent gradeEvent, ClassCourse classCourse, CancellationToken ct)
    {
        // A source belongs to the gradebook of its first attempt for good: a regrade scored in another term updates that item (and its maximum)
        // instead of creating a second item that would count the score twice.
        if (await FindExistingAsync(classCourse, gradeEvent.SourceKind, gradeEvent.SourceId, ct) is { } existing)
        {
            if (!await IsLockedAsync(existing.GradebookId, ct))
            {
                existing.SetMaxPoints(gradeEvent.MaxPoints); // a locked gradebook keeps its items as they were (L5)
            }

            return;
        }

        var term = await terms.ResolveAsync(classCourse, gradeEvent.ScoredAtUtc, ct);
        if (term is null)
        {
            return;
        }

        await EnsureItemAsync(classCourse, term.Id, gradeEvent.SourceKind, gradeEvent.SourceId, gradeEvent.MaxPoints, ct);
    }

    private async Task<bool> IsLockedAsync(Guid gradebookId, CancellationToken ct) =>
        db.Set<Gradebook>().Local.Any(g => g.Id == gradebookId && g.IsLocked)
        || await db.Set<Gradebook>().AnyAsync(g => g.Id == gradebookId && g.State == Gradebook.LockedState, ct);

    /// <summary>The live item of a source in any gradebook of the class course (pending rows of this unit of work included).</summary>
    public async Task<GradeItem?> FindExistingAsync(ClassCourse classCourse, GradeSourceKind kind, Guid sourceId, CancellationToken ct)
    {
        var kindValue = (int)kind;
        var pending = db.Set<GradeItem>().Local.FirstOrDefault(i =>
            i.SourceKind == kindValue && i.SourceId == sourceId
            && db.Set<Gradebook>().Local.Any(g => g.Id == i.GradebookId && g.ClassCourseId == classCourse.Id));
        if (pending is not null)
        {
            return pending;
        }

        return await (from i in db.Set<GradeItem>()
                      join g in db.Set<Gradebook>() on i.GradebookId equals g.Id
                      where g.ClassCourseId == classCourse.Id && i.SourceKind == kindValue && i.SourceId == sourceId
                      select i).FirstOrDefaultAsync(ct);
    }

    /// <summary>The gradebook, its category and the item of one source (shared with the backfill).</summary>
    public async Task<GradeItem?> EnsureItemAsync(ClassCourse classCourse, Guid termId, GradeSourceKind kind, Guid sourceId, decimal maxPoints, CancellationToken ct)
    {
        var gradebook = await writer.EnsureGradebookAsync(classCourse, termId, ct);
        if (gradebook.IsLocked)
        {
            return null; // L5: no new item appears in a locked gradebook; the event is still recorded
        }

        var (titleAr, titleEn) = await TitleAsync(kind, sourceId, ct);
        return await writer.EnsureSourceItemAsync(gradebook, (int)kind, sourceId, titleAr, titleEn, maxPoints, ct);
    }

    /// <summary>The title of the activity module; a source without one (or of a kind this version does not know) gets a generic title.</summary>
    private async Task<(string Ar, string En)> TitleAsync(GradeSourceKind kind, Guid sourceId, CancellationToken ct)
    {
        var moduleKind = kind switch
        {
            GradeSourceKind.Assignment => ModuleKinds.Assignment,
            GradeSourceKind.Quiz => ModuleKinds.Quiz,
            _ => null,
        };
        if (moduleKind is not null)
        {
            var module = await db.Set<LessonModule>().Where(m => m.Kind == moduleKind && m.RefId == sourceId)
                .Select(m => new { m.TitleAr, m.TitleEn }).FirstOrDefaultAsync(ct);
            if (module is not null)
            {
                return (module.TitleAr, module.TitleEn);
            }
        }

        return kind switch
        {
            GradeSourceKind.Assignment => ("واجب", "Assignment"),
            GradeSourceKind.Quiz => ("اختبار", "Quiz"),
            _ => ("نشاط", "Activity"),
        };
    }
}
