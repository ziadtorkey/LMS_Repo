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
        var term = await terms.ResolveAsync(classCourse, gradeEvent.ScoredAtUtc, ct);
        if (term is null)
        {
            return;
        }

        await EnsureItemAsync(classCourse, term.Id, gradeEvent.SourceKind, gradeEvent.SourceId, gradeEvent.MaxPoints, ct);
    }

    /// <summary>The gradebook, its category and the item of one source (shared with the backfill).</summary>
    public async Task<GradeItem> EnsureItemAsync(ClassCourse classCourse, Guid termId, GradeSourceKind kind, Guid sourceId, decimal maxPoints, CancellationToken ct)
    {
        var gradebook = await writer.EnsureGradebookAsync(classCourse, termId, ct);
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
