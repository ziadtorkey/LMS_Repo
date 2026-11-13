using Lms.Api.Features.Classes;
using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Assignments;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>
/// Assignment authoring and reading (M4a Task 8, FR-ASG-01). Writes need CourseAssignments.Manage and Modules.Manage with the
/// relationship to the class course, go through the closed class / year guard and create or update the lesson module in the same
/// unit of work. A student reads an assignment only while its module is visible to them (404 otherwise).
/// </summary>
public sealed class AssignmentHandlers(
    LmsDbContext db, ModuleAccess access, IRichTextSanitizer sanitizer, IModuleWriter writer, IClock clock, PlacementGuard guard,
    CourseAssignmentNotifier notifier)
{
    private AssignmentMapper Mapper => new(db);

    public async Task<IResult> ListAsync(Guid classCourseId, int? page, int? pageSize, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.View, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var now = clock.UtcNow;
        var isStudent = await access.IsStudentAsync(ct);
        Guid? studentId = null;
        IQueryable<LessonModule> modules;
        if (isStudent)
        {
            studentId = await access.StudentIdAsync(ct);
            if (studentId is null)
            {
                return Results.Ok(new PagedResult<CourseAssignmentListItemDto>([], 0, 1, Paging.DefaultPageSize));
            }

            var group = await access.GroupOfAsync(cc!.Id, studentId.Value, ct);
            modules = access.VisibleTo(cc.Id, group, now).Select(p => p.Module);
        }
        else
        {
            modules = db.Set<LessonModule>().Where(m => m.ClassCourseId == cc!.Id);
        }

        var rows = from a in db.Set<Assignment>()
                   join m in modules on a.Id equals m.RefId
                   where a.ClassCourseId == cc!.Id && m.Kind == ModuleKinds.Assignment
                   select new { A = a, M = m };
        var total = await rows.CountAsync(ct);
        var size = Math.Clamp(pageSize ?? Paging.DefaultPageSize, 1, Paging.MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var pageRows = await rows.OrderBy(r => r.A.DueAtUtc == null).ThenBy(r => r.A.DueAtUtc).ThenBy(r => r.M.TitleEn).ThenBy(r => r.A.Id)
            .Skip((number - 1) * size).Take(size).ToListAsync(ct);

        var ids = pageRows.Select(r => r.A.Id).ToList();
        var counts = isStudent
            ? []
            : (await db.Set<Submission>().Where(s => ids.Contains(s.AssignmentId) && s.Status != Submission.DraftStatus)
                .Select(s => new { s.AssignmentId, s.Status }).ToListAsync(ct))
                .GroupBy(s => s.AssignmentId)
                .ToDictionary(g => g.Key, g => (Submitted: g.Count(), Graded: g.Count(s => s.Status is SubmissionStatuses.Graded or SubmissionStatuses.Returned)));
        var mine = isStudent
            ? await db.Set<Submission>().Where(s => ids.Contains(s.AssignmentId) && s.StudentId == studentId)
                .ToDictionaryAsync(s => s.AssignmentId, ct)
            : [];

        var items = pageRows.Select(r =>
        {
            counts.TryGetValue(r.A.Id, out var c);
            mine.TryGetValue(r.A.Id, out var my);
            return new CourseAssignmentListItemDto(
                r.A.Id, r.M.Id, r.M.LessonKey, r.M.TitleAr, r.M.TitleEn, r.M.Visibility, r.M.IsVisibleNow(now), ModuleMapper.Utc(r.A.DueAtUtc),
                r.A.MaxGrade, isStudent ? null : c.Submitted, isStudent ? null : c.Graded, my?.Status, my?.IsLate);
        }).ToList();
        return Results.Ok(new PagedResult<CourseAssignmentListItemDto>(items, total, number, size));
    }

    public async Task<IResult> CreateAsync(Guid classCourseId, CreateCourseAssignmentRequest? request, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await ManageAsync(http, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var body = request is null ? null : AssignmentValidators.Of(request);
        if (AssignmentValidators.Validate(body) is { } invalid)
        {
            return invalid;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var lessonExists = await (from p in db.Set<ClassLessonPlan>()
                                  join l in db.Set<ClassLesson>() on p.Id equals l.PlanId
                                  where p.ClassCourseId == cc!.Id && l.Key == request!.LessonKey
                                  select l.Id).AnyAsync(ct);
        if (!lessonExists)
        {
            return ApiProblems.NotFound();
        }

        var assignment = new Assignment(cc!);
        if (await ApplyAsync(assignment, body!, cc!, ct) is { } bad)
        {
            return bad;
        }

        db.Add(assignment);
        await AddAttachmentsAsync(assignment, body!.AttachmentAssetIds ?? [], ct);
        Guid moduleId;
        try
        {
            moduleId = await writer.CreateForRefAsync(
                cc!.Id, request!.LessonKey, ModuleKinds.Assignment, assignment.Id, MetadataOf(body.Module!), ct);
        }
        catch (ModuleRuleException rule)
        {
            db.ChangeTracker.Clear();
            return ApiProblems.BadRequest(rule.Code, rule.Message);
        }

        var module = await db.Set<LessonModule>().FirstAsync(m => m.Id == moduleId, ct);
        await notifier.PublishedAsync(assignment, module, cc, ct);
        return Results.Created(Routes.CourseAssignment(assignment.Id), await Mapper.AssignmentAsync(assignment, module, true, true, clock.UtcNow, ct));
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var assignment = await db.Set<Assignment>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assignment is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.View, assignment.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var module = await ModuleOfAsync(assignment.Id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        var isStudent = await access.IsStudentAsync(ct);
        if (isStudent && !await AssignmentAccess.StudentSeesAsync(db, access, module, clock.UtcNow, ct))
        {
            return ApiProblems.NotFound();
        }

        var canEdit = !isStudent && (await http.CheckClassCourseAsync(Permissions.CourseAssignments.Manage, cc!.Id, ct)).Problem is null;
        return Results.Ok(await Mapper.AssignmentAsync(assignment, module, !isStudent, canEdit, clock.UtcNow, ct));
    }

    public async Task<IResult> UpdateAsync(Guid id, UpdateCourseAssignmentRequest? request, HttpContext http, CancellationToken ct)
    {
        var assignment = await db.Set<Assignment>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assignment is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await ManageAsync(http, assignment.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var body = request is null ? null : AssignmentValidators.Of(request);
        if (AssignmentValidators.Validate(body) is { } invalid)
        {
            return invalid;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var module = await ModuleOfAsync(assignment.Id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        if (await ApplyAsync(assignment, body!, cc!, ct) is { } bad)
        {
            return bad;
        }

        db.RemoveRange(await db.Set<AssignmentAttachment>().Where(t => t.AssignmentId == id).ToListAsync(ct));
        await AddAttachmentsAsync(assignment, body!.AttachmentAssetIds ?? [], ct);
        try
        {
            await writer.UpdateMetadataAsync(module.Id, MetadataOf(body.Module!), ct);
        }
        catch (ModuleRuleException rule)
        {
            db.ChangeTracker.Clear();
            return ApiProblems.BadRequest(rule.Code, rule.Message);
        }

        module = await db.Set<LessonModule>().FirstAsync(m => m.Id == module.Id, ct);
        return Results.Ok(await Mapper.AssignmentAsync(assignment, module, true, true, clock.UtcNow, ct));
    }

    public async Task<IResult> DeleteAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var assignment = await db.Set<Assignment>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assignment is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await ManageAsync(http, assignment.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var submissions = await db.Set<Submission>().Where(s => s.AssignmentId == id).ToListAsync(ct);
        var submissionIds = submissions.Select(s => s.Id).ToList();
        db.RemoveRange(await db.Set<SubmissionFile>().Where(f => submissionIds.Contains(f.SubmissionId)).ToListAsync(ct));
        db.RemoveRange(submissions);
        db.RemoveRange(await db.Set<AssignmentAttachment>().Where(t => t.AssignmentId == id).ToListAsync(ct));
        db.Remove(assignment);
        await writer.DeleteForRefAsync(ModuleKinds.Assignment, id, ct);
        return Results.NoContent();
    }

    /// <summary>CourseAssignments.Manage and Modules.Manage at the class course (both, with the relationship filter).</summary>
    private static async Task<(ClassCourse? Cc, IResult? Problem)> ManageAsync(HttpContext http, Guid classCourseId, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return (null, problem);
        }

        return await http.CheckClassCourseAsync(Permissions.Modules.Manage, classCourseId, ct);
    }

    private Task<LessonModule?> ModuleOfAsync(Guid assignmentId, CancellationToken ct) =>
        db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Kind == ModuleKinds.Assignment && m.RefId == assignmentId, ct);

    private static ModuleMetadata MetadataOf(ModuleSettingsInput m) => new(
        m.TitleAr.Trim(), m.TitleEn.Trim(), m.Visibility, m.VisibleFromUtc, m.VisibleToUtc, m.Target, m.GroupId, m.CompletionRule,
        m.CompletionMinScore);

    /// <summary>Sets the policy and the sanitised instructions; an over-long body is a 400 (null when fine).</summary>
    private async Task<IResult?> ApplyAsync(Assignment assignment, AssignmentValidators.Body body, ClassCourse cc, CancellationToken ct)
    {
        string? ar, en;
        try
        {
            ar = AssignmentValidators.Clean(sanitizer, body.InstructionsHtmlAr);
            en = AssignmentValidators.Clean(sanitizer, body.InstructionsHtmlEn);
        }
        catch (RichTextTooLongException)
        {
            return ClassValidators.Invalid(nameof(AssignmentValidators.Body.InstructionsHtmlAr), "too_long");
        }

        var ids = body.AttachmentAssetIds ?? [];
        var found = ids.Count == 0 ? 0 : await db.Set<Asset>().CountAsync(a => ids.Contains(a.Id) && a.ClassCourseId == cc.Id, ct);
        if (found != ids.Count)
        {
            return ClassValidators.Invalid(nameof(AssignmentValidators.Body.AttachmentAssetIds), "unknown_asset");
        }

        assignment.Apply(
            ar, en, body.DueAtUtc, body.MaxGrade, body.LatePolicy!, body.PenaltyPercent, body.ResubmissionRule!, body.AllowText, body.AllowFiles);
        return null;
    }

    private Task AddAttachmentsAsync(Assignment assignment, IReadOnlyList<Guid> assetIds, CancellationToken ct)
    {
        for (var i = 0; i < assetIds.Count; i++)
        {
            db.Add(new AssignmentAttachment(assignment, assetIds[i], i + 1));
        }

        return Task.CompletedTask;
    }
}
