using Lms.Domain.AcademicYears;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.Grading.Calculation;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>Reads the staff grid of a class course and term (M5 Task 8, L17: 50 students a page, at most 100).</summary>
public sealed class GradebookReader(
    LmsDbContext db, EventTermResolver terms, GradebookBackfill backfill, IGradebookWriter writer, IScaleResolver scales, IClock clock)
{
    /// <summary>The term to show: the one asked for (it must belong to the class year) or the current term of the class.</summary>
    public Task<Term?> ResolveTermAsync(ClassCourse classCourse, Guid? termId, CancellationToken ct) =>
        termId is { } id ? terms.FindAsync(classCourse, id, ct) : terms.ResolveAsync(classCourse, clock.UtcNow, ct);

    /// <summary>
    /// The gradebook of the term. A caller who may manage grades runs the backfill and, when the gradebook does not exist yet, creates it; every other
    /// reader gets null for a gradebook that was never created (reads never write for them, L4).
    /// </summary>
    public async Task<Gradebook?> FindAsync(ClassCourse classCourse, Term term, bool canManage, CancellationToken ct)
    {
        await backfill.RunForReaderAsync(classCourse, canManage, ct);
        var gradebook = await db.Set<Gradebook>().FirstOrDefaultAsync(g => g.ClassCourseId == classCourse.Id && g.TermId == term.Id, ct);
        if (gradebook is not null || !canManage)
        {
            return gradebook;
        }

        try
        {
            gradebook = await writer.EnsureGradebookAsync(classCourse, term.Id, ct);
            await writer.EnsureWritableAsync(gradebook, ct);
            await db.SaveChangesAsync(ct);
            return gradebook;
        }
        catch (GradebookRuleException)
        {
            db.ChangeTracker.Clear(); // a closed class or year: no gradebook is created by a read
            return null;
        }
    }

    /// <summary>The gradebook of the term for a write: created when missing (no backfill). Null in a closed class or year.</summary>
    public async Task<Gradebook?> GetOrCreateAsync(ClassCourse classCourse, Term term, CancellationToken ct)
    {
        var existing = await db.Set<Gradebook>().FirstOrDefaultAsync(g => g.ClassCourseId == classCourse.Id && g.TermId == term.Id, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await writer.EnsureGradebookAsync(classCourse, term.Id, ct);
            await writer.EnsureWritableAsync(created, ct);
            await db.SaveChangesAsync(ct);
            return created;
        }
        catch (GradebookRuleException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task<GradebookDto> BuildAsync(
        ClassCourse classCourse, Gradebook gradebook, Term term, GridPaging paging, bool canManage, bool canPublish, CancellationToken ct)
    {
        var (students, total) = await StudentsAsync(classCourse, paging, ct);
        var data = await LoadAsync(classCourse, gradebook, students, ct);
        var result = data.Calculate();
        var scale = data.Scale;
        var weights = data.Categories.Sum(c => c.Weight);
        var locked = gradebook.IsLocked && gradebook.LockedAtUtc is { } at
            ? await db.Set<GradeEvent>().CountAsync(e => e.ClassCourseId == classCourse.Id && e.ScoredAtUtc > at, ct)
            : 0;
        return new GradebookDto(
            gradebook.Id, classCourse.Id, term.Id, term.NameAr, term.NameEn,
            new GradebookConfigDto(
                scale?.Scale.Id, scale?.Scale.NameAr, scale?.Scale.NameEn, scale?.Scale.Provisional ?? false, gradebook.DecimalPlaces,
                gradebook.CategoryMethod, gradebook.MissingPolicy, gradebook.RoundingMode),
            data.Categories.OrderBy(c => c.Order).Select(c => new GradeCategoryDto(c.Id, c.NameAr, c.NameEn, c.Weight, c.Order, c.IsUncategorised)).ToList(),
            data.Items.Select(ToDto).ToList(),
            new PagedResult<GradeRowDto>(GradeRowBuilder.Build(data, result), total, paging.Page, paging.PageSize),
            result.IsValid, weights, gradebook.State, gradebook.LockedAtUtc, locked, canManage, canPublish, clock.UtcNow, clock.UtcNow);
    }

    /// <summary>The recomputed row of one student (the answer of an entry write).</summary>
    public async Task<GradeRowDto?> RowAsync(ClassCourse classCourse, Gradebook gradebook, Guid studentId, CancellationToken ct)
    {
        var student = await db.Set<StudentProfile>().Where(s => s.Id == studentId)
            .Select(s => new GridStudent(s.Id, s.StudentCode, s.NameAr, s.NameEn)).FirstOrDefaultAsync(ct);
        if (student is null)
        {
            return null;
        }

        var data = await LoadAsync(classCourse, gradebook, [student], ct);
        return GradeRowBuilder.Build(data, data.Calculate()).Single();
    }

    public static GradeItemDto ToDto(GradeItem i) =>
        new(i.Id, i.CategoryId, i.TitleAr, i.TitleEn, i.SourceKind is { } k ? ((GradeSourceKind)k).ToString() : null, i.SourceId, i.IsManual,
            i.MaxPoints, i.PublishState, i.PublishAtUtc, i.PublishedAtUtc, i.Order);

    private async Task<(IReadOnlyList<GridStudent> Students, int Total)> StudentsAsync(
        ClassCourse classCourse, GridPaging paging, CancellationToken ct)
    {
        var enrolled = db.Set<CourseEnrollment>()
            .Where(e => e.ClassCourseId == classCourse.Id && e.Status == CourseEnrollment.ActiveStatus)
            .Select(e => e.StudentId);
        var query = db.Set<StudentProfile>().Where(s => enrolled.Contains(s.Id));
        var total = await query.CountAsync(ct);
        var page = await query.OrderBy(s => s.NameEn).ThenBy(s => s.Id)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize)
            .Select(s => new GridStudent(s.Id, s.StudentCode, s.NameAr, s.NameEn)).ToListAsync(ct);
        return (page, total);
    }

    private async Task<GradebookData> LoadAsync(
        ClassCourse classCourse, Gradebook gradebook, IReadOnlyList<GridStudent> students, CancellationToken ct)
    {
        var ids = students.Select(s => s.Id).ToList();
        var items = await db.Set<GradeItem>().Where(i => i.GradebookId == gradebook.Id).OrderBy(i => i.Order).ThenBy(i => i.Id).ToListAsync(ct);
        var sources = items.Where(i => i.SourceId is not null).Select(i => i.SourceId!.Value).ToList();
        var events = await db.Set<GradeEvent>()
            .Where(e => e.ClassCourseId == classCourse.Id && ids.Contains(e.StudentProfileId) && sources.Contains(e.SourceId)).ToListAsync(ct);
        var entries = await db.Set<GradeEntry>().Where(e => e.GradebookId == gradebook.Id && ids.Contains(e.StudentProfileId)).ToListAsync(ct);
        var categories = await db.Set<GradeCategory>().Where(c => c.GradebookId == gradebook.Id).OrderBy(c => c.Order).ThenBy(c => c.Id).ToListAsync(ct);

        VisibleScale? scale = null;
        if (gradebook.GradingScaleId is { } scaleId)
        {
            var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == classCourse.OrgUnitId, ct);
            scale = unit is null ? null : await scales.FindScaleAsync(unit, scaleId, ct);
        }

        var bands = (scale?.Bands ?? []).Select(b =>
            new GridBand(b.Code, new CalcBand(b.MinPercent, b.LabelAr ?? b.Code, b.LabelEn ?? b.Code, b.Points))).ToList();
        return new GradebookData
        {
            Gradebook = gradebook, Categories = categories, Items = items, Events = events, Entries = entries, Bands = bands,
            Students = students, StudentIds = ids, Scale = scale,
        };
    }
}

/// <summary>The page asked for (already clamped: page at least 1, size 1 to 100).</summary>
public sealed record GridPaging(int Page, int PageSize)
{
    public static GridPaging From(int page, int pageSize) =>
        new(Math.Max(1, page), Math.Clamp(pageSize <= 0 ? GradingLimits.GridPageSize : pageSize, 1, GradingLimits.GridPageSizeMax));
}
