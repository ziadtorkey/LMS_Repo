using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary><see cref="IGradebookWriter"/>: the single write path. It never saves (the unit of work of the caller does).</summary>
public sealed class GradebookWriter(LmsDbContext db, ISettingsResolver settings, PlacementGuard placement, IClock clock) : IGradebookWriter
{
    public async Task<Gradebook> EnsureGradebookAsync(ClassCourse classCourse, Guid termId, CancellationToken ct)
    {
        var existing = db.Set<Gradebook>().Local.FirstOrDefault(g => g.ClassCourseId == classCourse.Id && g.TermId == termId)
                       ?? await db.Set<Gradebook>().FirstOrDefaultAsync(g => g.ClassCourseId == classCourse.Id && g.TermId == termId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var path = classCourse.OrgUnitPath;
        var gradebook = new Gradebook(
            classCourse, termId, scaleId: null,
            (await settings.ResolveAsync(SettingKeys.GradesDecimalPlaces, path, ct)).Value,
            (await settings.ResolveAsync(SettingKeys.GradesCategoryMethod, path, ct)).Value,
            (await settings.ResolveAsync(SettingKeys.GradesMissingPolicy, path, ct)).Value);
        db.Add(gradebook);
        db.Add(new GradeCategory(gradebook, "غير مصنف", "Uncategorised", 0m, 0, isUncategorised: true));
        return gradebook;
    }

    public async Task<GradeItem> EnsureSourceItemAsync(
        Gradebook gradebook, int sourceKind, Guid sourceId, string titleAr, string titleEn, decimal maxPoints, CancellationToken ct)
    {
        var existing = db.Set<GradeItem>().Local.FirstOrDefault(i => i.GradebookId == gradebook.Id && i.SourceKind == sourceKind && i.SourceId == sourceId)
                       ?? await db.Set<GradeItem>().FirstOrDefaultAsync(
                           i => i.GradebookId == gradebook.Id && i.SourceKind == sourceKind && i.SourceId == sourceId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var category = await UncategorisedAsync(gradebook, ct);
        var item = GradeItem.ForSource(gradebook, category, sourceKind, sourceId, titleAr, titleEn, maxPoints, await NextOrderAsync(gradebook, ct));
        db.Add(item);
        return item;
    }

    public async Task<GradeItem> AddManualItemAsync(
        Gradebook gradebook, Guid categoryId, string titleAr, string titleEn, decimal maxPoints, CancellationToken ct)
    {
        await EnsureWritableAsync(gradebook, ct);
        var category = db.Set<GradeCategory>().Local.FirstOrDefault(c => c.Id == categoryId && c.GradebookId == gradebook.Id)
                       ?? await db.Set<GradeCategory>().FirstOrDefaultAsync(c => c.Id == categoryId && c.GradebookId == gradebook.Id, ct)
                       ?? throw new GradebookRuleException(ProblemCodes.validation_failed, 400);
        var item = GradeItem.Manual(gradebook, category, titleAr, titleEn, maxPoints, await NextOrderAsync(gradebook, ct));
        db.Add(item);
        return item;
    }

    public async Task<GradeEntry> SetPointsAsync(
        GradeItem item, Guid studentProfileId, decimal? points, int? expectedVersion, Guid? byUserId, CancellationToken ct)
    {
        var gradebook = db.Set<Gradebook>().Local.FirstOrDefault(g => g.Id == item.GradebookId)
                        ?? await db.Set<Gradebook>().SingleAsync(g => g.Id == item.GradebookId, ct);
        await EnsureWritableAsync(gradebook, ct);

        var entry = db.Set<GradeEntry>().Local.FirstOrDefault(e => e.GradeItemId == item.Id && e.StudentProfileId == studentProfileId)
                    ?? await db.Set<GradeEntry>().FirstOrDefaultAsync(e => e.GradeItemId == item.Id && e.StudentProfileId == studentProfileId, ct);
        if (entry is null)
        {
            if (expectedVersion is > 0)
            {
                throw new GradebookRuleException(ProblemCodes.concurrency_conflict); // the row the caller read has been removed since
            }

            entry = new GradeEntry(item, studentProfileId, byUserId, clock.UtcNow);
            db.Add(entry);
        }
        else if ((expectedVersion ?? 0) != entry.Version)
        {
            throw new GradebookRuleException(ProblemCodes.concurrency_conflict);
        }

        entry.SetPoints(points, byUserId, clock.UtcNow);
        return entry;
    }

    /// <summary>Locked gradebook, closed class or closed year: 409.</summary>
    public async Task EnsureWritableAsync(Gradebook gradebook, CancellationToken ct)
    {
        if (gradebook.IsLocked)
        {
            throw new GradebookRuleException(ProblemCodes.gradebook_locked);
        }

        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == gradebook.ClassCourseId, ct);
        var cls = await db.Set<Class>().SingleAsync(c => c.Id == classCourse.ClassId, ct);
        try
        {
            await placement.EnsureWritableAsync(cls, ct);
        }
        catch (EnrollmentRuleException rule)
        {
            throw new GradebookRuleException(rule.Code, rule.StatusCode);
        }
    }

    private async Task<GradeCategory> UncategorisedAsync(Gradebook gradebook, CancellationToken ct) =>
        db.Set<GradeCategory>().Local.FirstOrDefault(c => c.GradebookId == gradebook.Id && c.IsUncategorised)
        ?? await db.Set<GradeCategory>().FirstAsync(c => c.GradebookId == gradebook.Id && c.IsUncategorised, ct);

    private async Task<int> NextOrderAsync(Gradebook gradebook, CancellationToken ct)
    {
        var stored = await db.Set<GradeItem>().Where(i => i.GradebookId == gradebook.Id).Select(i => (int?)i.Order).MaxAsync(ct) ?? 0;
        var pending = db.Set<GradeItem>().Local.Where(i => i.GradebookId == gradebook.Id).Select(i => i.Order).DefaultIfEmpty(0).Max();
        return Math.Max(stored, pending) + 1;
    }
}
