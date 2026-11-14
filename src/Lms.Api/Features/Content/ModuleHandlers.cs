using Lms.Api.Features.Classes;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>
/// Module authoring and reading under a lesson of a class course (M4a Task 3, FR-LP-05, FR-CNT-01..02, D48, D51). Writes need
/// Modules.Manage and the relationship to the class course; every write goes through the closed class / year guard; a
/// student reads only what <see cref="ModuleAccess"/> lets them see (404 otherwise).
/// </summary>
public sealed class ModuleHandlers(
    LmsDbContext db, ModuleAccess access, IRichTextSanitizer sanitizer, IClock clock, PlacementGuard guard, CompletionRecorder recorder)
{
    public async Task<IResult> ListAsync(Guid classCourseId, Guid lessonKey, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.View, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await FindLessonAsync(cc!, lessonKey, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var now = clock.UtcNow;
        List<LessonModule> modules;
        Dictionary<Guid, string>? states = null;
        if (await access.IsStudentAsync(ct))
        {
            var studentId = await access.StudentIdAsync(ct);
            if (studentId is null)
            {
                return Results.Ok(new List<ModuleNodeDto>());
            }

            var group = await access.GroupOfAsync(classCourseId, studentId.Value, ct);
            modules = (await access.VisibleTo(classCourseId, group, now).Where(p => p.Module.LessonKey == lessonKey).ToListAsync(ct))
                .Select(p => p.Module).ToList();
            states = await StatesAsync(modules, studentId.Value, ct);
        }
        else
        {
            modules = await db.Set<LessonModule>().Where(m => m.ClassCourseId == classCourseId && m.LessonKey == lessonKey)
                .OrderBy(m => m.Order).ThenBy(m => m.Id).ToListAsync(ct);
        }

        var groups = await ModuleMapper.GroupsAsync(db, modules, ct);
        var removed = await ModuleRefState.RemovedAsync(db, modules, ct);
        return Results.Ok(modules.Select(m => ModuleMapper.Node(m, now, groups, states, removed)).ToList());
    }

    public async Task<IResult> CreateAsync(Guid classCourseId, Guid lessonKey, CreateModuleRequest? request, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ModuleValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await GroupProblemAsync(cc!, request!.Target, request.GroupId, ct) is { } badGroup)
        {
            return badGroup;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var lesson = await FindLessonAsync(cc!, lessonKey, ct);
        if (lesson is null)
        {
            return ApiProblems.NotFound();
        }

        var last = await db.Set<LessonModule>().Where(m => m.ClassLessonId == lesson.Id).MaxAsync(m => (int?)m.Order, ct) ?? 0;
        var module = new LessonModule(cc!, lesson, request.Kind, last + 1);
        module.Apply(
            request.TitleAr.Trim(), request.TitleEn.Trim(), request.Visibility, request.VisibleFromUtc, request.VisibleToUtc, request.Target,
            request.GroupId, request.CompletionRule, null);
        db.Add(module);
        var content = new ModuleContent(module);
        if (await ApplyContentAsync(content, cc!, module.Kind, request.Content, ct) is { } badContent)
        {
            return badContent;
        }

        db.Add(content);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.Module(module.Id), await DetailAsync(module, content, cc!, http, ct));
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.View, module.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await access.IsStudentAsync(ct))
        {
            var studentId = await access.StudentIdAsync(ct);
            if (studentId is null || !await CanStudentSeeAsync(module, studentId.Value, ct))
            {
                return ApiProblems.NotFound();
            }

            // Opening a module counts as viewing it (a View rule completes). A closed class keeps reads open but records nothing.
            if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is null)
            {
                await recorder.RecordAsync(module, studentId.Value, CompletionSignal.Viewed, null, ct);
            }
        }

        var content = await db.Set<ModuleContent>().FirstOrDefaultAsync(c => c.ModuleId == id, ct);
        return Results.Ok(await DetailAsync(module, content, cc!, http, ct));
    }

    public async Task<IResult> UpdateAsync(Guid id, UpdateModuleRequest? request, HttpContext http, CancellationToken ct)
    {
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, module.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (ModuleValidators.Validate(request, module.Kind) is { } invalid)
        {
            return invalid;
        }

        if (await GroupProblemAsync(cc!, request!.Target, request.GroupId, ct) is { } badGroup)
        {
            return badGroup;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var content = await db.Set<ModuleContent>().FirstOrDefaultAsync(c => c.ModuleId == id, ct);
        var isNew = content is null;
        content ??= new ModuleContent(module);
        if (await ApplyContentAsync(content, cc!, module.Kind, request.Content, ct) is { } badContent)
        {
            return badContent;
        }

        if (isNew)
        {
            db.Add(content);
        }

        module.Apply(
            request.TitleAr.Trim(), request.TitleEn.Trim(), request.Visibility, request.VisibleFromUtc, request.VisibleToUtc, request.Target,
            request.GroupId, request.CompletionRule, null);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await DetailAsync(module, content, cc!, http, ct));
    }

    public async Task<IResult> DeleteAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, module.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        // An assignment, quiz or library module belongs to its feature (IModuleWriter): removing it here would orphan the activity.
        if (!ModuleKinds.Content.Contains(module.Kind))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Remove it through its own feature.");
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        db.RemoveRange(await db.Set<ModuleContent>().Where(c => c.ModuleId == id).ToListAsync(ct));
        db.Remove(module);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> ReorderAsync(Guid classCourseId, Guid lessonKey, ReorderModulesRequest? request, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await FindLessonAsync(cc!, lessonKey, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var modules = await db.Set<LessonModule>().Where(m => m.ClassCourseId == classCourseId && m.LessonKey == lessonKey).ToListAsync(ct);
        var ids = request?.ModuleIds;
        if (ids is null || ids.Count != modules.Count || ids.Distinct().Count() != ids.Count || !modules.All(m => ids.Contains(m.Id)))
        {
            return ClassValidators.Invalid(nameof(ReorderModulesRequest.ModuleIds), "set_mismatch");
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            modules.Single(m => m.Id == ids[i]).SetOrder(i + 1);
        }

        await db.SaveChangesAsync(ct);
        var groups = await ModuleMapper.GroupsAsync(db, modules, ct);
        return Results.Ok(modules.OrderBy(m => m.Order).Select(m => ModuleMapper.Node(m, clock.UtcNow, groups, null)).ToList());
    }

    public async Task<IResult> MoveAsync(Guid id, MoveModuleRequest? request, HttpContext http, CancellationToken ct)
    {
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, module.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        // Only a lesson of the same class lesson plan: another plan's key is a plain 404.
        var target = await FindLessonAsync(cc!, request.ToLessonKey, ct);
        if (target is null)
        {
            return ApiProblems.NotFound();
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var last = await db.Set<LessonModule>().Where(m => m.ClassLessonId == target.Id && m.Id != id).MaxAsync(m => (int?)m.Order, ct) ?? 0;
        module.MoveTo(target, last + 1);
        await db.SaveChangesAsync(ct);
        var content = await db.Set<ModuleContent>().FirstOrDefaultAsync(c => c.ModuleId == id, ct);
        return Results.Ok(await DetailAsync(module, content, cc!, http, ct));
    }

    private async Task<ClassLesson?> FindLessonAsync(ClassCourse classCourse, Guid lessonKey, CancellationToken ct) =>
        await (from p in db.Set<ClassLessonPlan>()
               join l in db.Set<ClassLesson>() on p.Id equals l.PlanId
               where p.ClassCourseId == classCourse.Id && l.Key == lessonKey
               select l).FirstOrDefaultAsync(ct);

    private async Task<IResult?> GroupProblemAsync(ClassCourse classCourse, string target, Guid? groupId, CancellationToken ct)
    {
        if (target != ModuleTargets.Group || groupId is not { } id)
        {
            return null;
        }

        var exists = await db.Set<StudentGroup>().AnyAsync(g => g.Id == id && g.ClassCourseId == classCourse.Id, ct);
        return exists ? null : ApiProblems.BadRequest(ProblemCodes.module_target_invalid);
    }

    /// <summary>Sets the payload of the kind; a File/Video/Audio asset must belong to this class course and be of an accepted kind.</summary>
    private async Task<IResult?> ApplyContentAsync(
        ModuleContent content, ClassCourse classCourse, string kind, ModuleContentInput? input, CancellationToken ct)
    {
        input ??= new ModuleContentInput();
        if (kind == ModuleKinds.Page)
        {
            content.Set(null, NullIfEmpty(sanitizer.Sanitize(input.BodyHtmlAr)), NullIfEmpty(sanitizer.Sanitize(input.BodyHtmlEn)), null);
        }
        else if (kind == ModuleKinds.Link)
        {
            content.Set(null, null, null, input.Url!.Trim());
        }
        else
        {
            // Through the filtered set: another client, another course or a deleted asset all look like "no such asset".
            var asset = await db.Set<Asset>().FirstOrDefaultAsync(a => a.Id == input.AssetId && a.ClassCourseId == classCourse.Id, ct);
            if (asset is null)
            {
                return ClassValidators.Invalid("Content", "asset_invalid");
            }

            if (!AssetKinds.AcceptedBy(kind).Contains(asset.Kind))
            {
                return ApiProblems.BadRequest(ProblemCodes.asset_type_not_allowed);
            }

            content.Set(asset.Id, null, null, null);
        }

        return null;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private async Task<bool> CanStudentSeeAsync(LessonModule module, Guid studentId, CancellationToken ct)
    {
        var group = await access.GroupOfAsync(module.ClassCourseId, studentId, ct);
        return await access.VisibleTo(module.ClassCourseId, group, clock.UtcNow).AnyAsync(p => p.Module.Id == module.Id, ct);
    }

    private async Task<Dictionary<Guid, string>> StatesAsync(IReadOnlyCollection<LessonModule> modules, Guid studentId, CancellationToken ct)
    {
        var ids = modules.Select(m => m.Id).ToList();
        return await db.Set<ModuleCompletion>().Where(c => c.StudentId == studentId && ids.Contains(c.ModuleId))
            .ToDictionaryAsync(c => c.ModuleId, c => c.State, ct);
    }

    private async Task<Asset?> AssetOfAsync(ModuleContent? content, CancellationToken ct) =>
        content?.AssetId is { } assetId ? await db.Set<Asset>().FirstOrDefaultAsync(a => a.Id == assetId, ct) : null;

    private async Task<ModuleDetailDto> DetailAsync(LessonModule module, ModuleContent? content, ClassCourse cc, HttpContext http, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var isStudent = await access.IsStudentAsync(ct);
        Dictionary<Guid, string>? states = null;
        List<Guid> order;
        if (isStudent)
        {
            var studentId = await access.StudentIdAsync(ct);
            var group = studentId is { } s ? await access.GroupOfAsync(cc.Id, s, ct) : null;
            order = (await access.VisibleTo(cc.Id, group, now).ToListAsync(ct)).Select(p => p.Module.Id).ToList();
            states = studentId is { } sid ? await StatesAsync([module], sid, ct) : [];
        }
        else
        {
            order = (await access.AllOf(cc.Id).ToListAsync(ct)).Select(p => p.Module.Id).ToList();
        }

        var index = order.IndexOf(module.Id);
        var groups = await ModuleMapper.GroupsAsync(db, [module], ct);
        var canEdit = (await http.CheckClassCourseAsync(Permissions.Modules.Manage, cc.Id, ct)).Problem is null;
        return new ModuleDetailDto(
            ModuleMapper.Node(module, now, groups, states), cc.Id, module.LessonKey, module.CompletionMinScore,
            ModuleMapper.Content(module, content, await AssetOfAsync(content, ct)),
            index > 0 ? order[index - 1] : null,
            index >= 0 && index < order.Count - 1 ? order[index + 1] : null,
            canEdit, now);
    }
}
