using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Blueprints;
using Lms.Domain.Courses;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Blueprints;

/// <summary>Reading versions and editing the draft of a course blueprint: create, save, delete and copy.</summary>
public sealed class BlueprintDraftHandlers(
    LmsDbContext db, ICourseCatalog catalog, BlueprintReader reader, CallerPermissions caller, IRelationshipScope relationships)
{
    private static IResult NoDraft() =>
        BlueprintValidators.Problem(StatusCodes.Status404NotFound, "Not found", ProblemCodes.blueprint_no_draft, "The course has no draft.");

    private static IResult Stale() =>
        BlueprintValidators.Problem(StatusCodes.Status409Conflict, "Conflict", ProblemCodes.blueprint_stale, "The draft was changed by someone else.");

    public async Task<IResult> ListAsync(Guid courseId, CancellationToken ct)
    {
        if (await catalog.FindAsync(courseId, ct) is not { } view)
        {
            return ApiProblems.NotFound();
        }

        var versions = await reader.ListAsync(view.Course.OrganizationId, courseId, await MaySeeDraftAsync(view, ct), ct);
        return Results.Ok(await reader.SummariesAsync(versions, ct));
    }

    public async Task<IResult> GetVersionAsync(Guid courseId, int version, CancellationToken ct)
    {
        if (await catalog.FindAsync(courseId, ct) is not { } view)
        {
            return ApiProblems.NotFound();
        }

        var blueprint = await reader.FindAsync(view.Course.OrganizationId, courseId, version, await MaySeeDraftAsync(view, ct), ct);
        return blueprint is null ? ApiProblems.NotFound() : Results.Ok(await reader.ToDtoAsync(blueprint, ct));
    }

    public async Task<IResult> CreateDraftAsync(Guid courseId, HttpContext http, CancellationToken ct)
    {
        var (course, problem) = await http.CheckCourseAsync(Permissions.Blueprints.Edit, courseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await FindDraftAsync(courseId, ct) is not null)
        {
            return DraftExists();
        }

        var draft = Blueprint.CreateDraft(course!);
        db.Add(draft);
        // The new draft starts as a copy of the latest published version with the same keys (so class plans can merge).
        if (await reader.LatestPublishedAsync(course!.OrganizationId, courseId, ct) is { } latest)
        {
            var (sections, lessons) = await reader.TreeAsync(latest, ct);
            var tree = sections.Select(s => new TreeSection(
                s.Key, s.TitleAr, s.TitleEn,
                lessons.Where(l => l.SectionId == s.Id).OrderBy(l => l.Order).Select(l => new TreeLesson(
                    l.Key, l.TitleAr, l.TitleEn, l.ObjectivesAr, l.ObjectivesEn, l.PlannedOffsetDays)).ToList())).ToList();
            AddTree(draft, tree);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsConcurrentKeyConflict(ex))
        {
            return DraftExists(); // a concurrent request created the draft first
        }

        return Results.Created(Routes.CourseBlueprintDraft(courseId), await reader.ToDtoAsync(draft, ct));
    }

    public async Task<IResult> GetDraftAsync(Guid courseId, HttpContext http, CancellationToken ct)
    {
        var (_, problem) = await http.CheckCourseAsync(Permissions.Blueprints.Edit, courseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        return await FindDraftAsync(courseId, ct) is { } draft ? Results.Ok(await reader.ToDtoAsync(draft, ct)) : NoDraft();
    }

    public async Task<IResult> SaveDraftAsync(Guid courseId, SaveBlueprintDraftRequest request, HttpContext http, CancellationToken ct)
    {
        var (course, problem) = await http.CheckCourseAsync(Permissions.Blueprints.Edit, courseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var draft = await FindDraftAsync(courseId, ct);
        if (draft is null)
        {
            return NoDraft();
        }

        if (request is null || request.Revision != draft.Revision)
        {
            return Stale();
        }

        var (knownSections, knownLessons) = await reader.KeysAsync(course!.OrganizationId, courseId, ct);
        var (tree, invalid) = BlueprintValidators.Validate(request.Sections, knownSections, knownLessons);
        if (invalid is not null)
        {
            return invalid;
        }

        return await ReplaceTreeAsync(draft, tree!, ct) ?? Results.Ok(await reader.ToDtoAsync(draft, ct));
    }

    public async Task<IResult> DeleteDraftAsync(Guid courseId, HttpContext http, CancellationToken ct)
    {
        var (_, problem) = await http.CheckCourseAsync(Permissions.Blueprints.Edit, courseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var draft = await FindDraftAsync(courseId, ct);
        if (draft is null)
        {
            return NoDraft();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await RemoveTreeAsync(draft, ct);
        db.Remove(draft);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> CopyFromAsync(Guid courseId, CopyBlueprintRequest request, HttpContext http, CancellationToken ct)
    {
        var (course, problem) = await http.CheckCourseAsync(Permissions.Blueprints.Edit, courseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var draft = await FindDraftAsync(courseId, ct);
        if (draft is null)
        {
            return NoDraft();
        }

        // Any course the caller can see (own, inherited or another one) may be copied from; others are 404.
        if (request is null || await catalog.FindAsync(request.SourceCourseId, ct) is not { } sourceView)
        {
            return ApiProblems.NotFound();
        }

        var source = request.SourceVersion is { } version
            ? await reader.FindAsync(sourceView.Course.OrganizationId, request.SourceCourseId, version, includeDraft: false, ct)
            : await reader.LatestPublishedAsync(sourceView.Course.OrganizationId, request.SourceCourseId, ct);
        if (source is null || sourceView.Course.OrganizationId != course!.OrganizationId)
        {
            return ApiProblems.NotFound();
        }

        // The copy gets new keys: it is a new lesson plan, not a new version of the source course's.
        var (sections, lessons) = await reader.TreeAsync(source, ct);
        var tree = sections.Select(s => new TreeSection(
            Guid.NewGuid(), s.TitleAr, s.TitleEn,
            lessons.Where(l => l.SectionId == s.Id).OrderBy(l => l.Order).Select(l => new TreeLesson(
                Guid.NewGuid(), l.TitleAr, l.TitleEn, l.ObjectivesAr, l.ObjectivesEn, l.PlannedOffsetDays)).ToList())).ToList();
        return await ReplaceTreeAsync(draft, tree, ct) ?? Results.Ok(await reader.ToDtoAsync(draft, ct));
    }

    internal Task<Blueprint?> FindDraftAsync(Guid courseId, CancellationToken ct) =>
        db.Set<Blueprint>().FirstOrDefaultAsync(b => b.CourseId == courseId && b.Status == Blueprint.DraftStatus, ct);

    /// <summary>
    /// Replaces the whole tree of the draft and bumps its revision in one transaction. The revision is the concurrency
    /// token: of two saves that read the same revision only one commits, the other answers 409 <c>blueprint_stale</c>.
    /// Returns a problem result, or null on success.
    /// </summary>
    private async Task<IResult?> ReplaceTreeAsync(Blueprint draft, IReadOnlyList<TreeSection> tree, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await RemoveTreeAsync(draft, ct);
            draft.NextRevision();
            await db.SaveChangesAsync(ct); // deletes + the revision check in one statement batch
            AddTree(draft, tree);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException update && DbErrors.IsConcurrentKeyConflict(update)))
        {
            return Stale();
        }
    }

    private async Task RemoveTreeAsync(Blueprint draft, CancellationToken ct)
    {
        // The draft is in the caller's scope (its course is owned there), so the filtered sets see its rows.
        db.RemoveRange(await db.Set<BlueprintLesson>().Where(l => l.BlueprintId == draft.Id).ToListAsync(ct));
        db.RemoveRange(await db.Set<BlueprintSection>().Where(s => s.BlueprintId == draft.Id).ToListAsync(ct));
    }

    private void AddTree(Blueprint draft, IReadOnlyList<TreeSection> tree)
    {
        for (var i = 0; i < tree.Count; i++)
        {
            var section = new BlueprintSection(draft, tree[i].Key, i, tree[i].TitleAr, tree[i].TitleEn);
            db.Add(section);
            for (var j = 0; j < tree[i].Lessons.Count; j++)
            {
                var l = tree[i].Lessons[j];
                db.Add(new BlueprintLesson(section, l.Key, j, l.TitleAr, l.TitleEn, l.ObjectivesAr, l.ObjectivesEn, l.PlannedOffsetDays));
            }
        }
    }

    private static IResult DraftExists() =>
        BlueprintValidators.Problem(StatusCodes.Status409Conflict, "Conflict", ProblemCodes.blueprint_draft_exists, "The course already has a draft.");

    /// <summary>Drafts are for editors of the course: Blueprints.Edit in scope, owning the course and, for a Supervisor, supervising it.</summary>
    private async Task<bool> MaySeeDraftAsync(CourseView view, CancellationToken ct) =>
        !view.IsInherited
        && (await caller.GetAsync(ct)).Contains(Permissions.Blueprints.Edit)
        && (await relationships.ForAsync(Permissions.Blueprints.Edit, ct)).AllowsCourse(view.Course.Id);
}
