using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>
/// A student module completion and course progress (M4a Task 6, FR-CNT-04). A student reads and marks only their own;
/// a teacher or other staff caller reads the progress of an enrolled student.
/// </summary>
public sealed class CompletionHandlers(
    LmsDbContext db, ModuleAccess access, ProgressReader progress, CompletionRecorder recorder, PlacementGuard guard, IClock clock)
{
    public async Task<IResult> GetAsync(Guid moduleId, HttpContext http, CancellationToken ct)
    {
        var (module, studentId, problem) = await ReachAsync(moduleId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        var row = await db.Set<ModuleCompletion>().FirstOrDefaultAsync(c => c.ModuleId == moduleId && c.StudentId == studentId, ct);
        return Results.Ok(Dto(module!, row));
    }

    public async Task<IResult> MarkAsync(Guid moduleId, HttpContext http, CancellationToken ct)
    {
        var (module, studentId, problem) = await ReachAsync(moduleId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (module!.CompletionRule != CompletionRules.Manual)
        {
            return ApiProblems.Conflict(ProblemCodes.completion_not_manual);
        }

        var cc = await db.Set<Lms.Domain.Classes.ClassCourse>().FirstAsync(c => c.Id == module.ClassCourseId, ct);
        if (await GroupEndpoints.WritableAsync(cc, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var row = await recorder.RecordAsync(module, studentId!.Value, CompletionSignal.Marked, null, ct);
        return Results.Ok(Dto(module, row));
    }

    public async Task<IResult> ProgressAsync(Guid classCourseId, Guid? studentId, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.View, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        Guid student;
        var asStudent = await access.IsStudentAsync(ct);
        if (asStudent)
        {
            // A student reads only their own progress; a studentId in the query is ignored.
            if (await access.StudentIdAsync(ct) is not { } own)
            {
                return ApiProblems.NotFound();
            }

            student = own;
        }
        else
        {
            if (studentId is not { } requested)
            {
                return ClassValidatorsInvalid();
            }

            var enrolled = await db.Set<CourseEnrollment>()
                .AnyAsync(e => e.ClassCourseId == classCourseId && e.StudentId == requested && e.Status == CourseEnrollment.ActiveStatus, ct);
            if (!enrolled)
            {
                return ApiProblems.NotFound();
            }

            student = requested;
        }

        var snapshot = await progress.ReadAsync(cc!.Id, student, ct);
        var lessons = await progress.LessonsAsync(cc.Id, snapshot, hideRemoved: true, ct);
        return Results.Ok(new CourseProgressDto(
            cc.Id, student, snapshot.Course, lessons, snapshot.Modules, snapshot.NextModuleId, clock.UtcNow));
    }

    /// <summary>The module, the calling student and the problem (staff have no completion of their own).</summary>
    private async Task<(LessonModule? Module, Guid? StudentId, IResult? Problem)> ReachAsync(Guid moduleId, HttpContext http, CancellationToken ct)
    {
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == moduleId, ct);
        if (module is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Modules.View, module.ClassCourseId, ct);
        if (problem is not null)
        {
            return (null, null, problem);
        }

        if (!await access.IsStudentAsync(ct))
        {
            return (null, null, AccessProblem.From(AccessDecision.Deny(AccessDenialReason.MissingPermission), http));
        }

        var student = await access.StudentIdAsync(ct);
        var group = student is { } s ? await access.GroupOfAsync(module.ClassCourseId, s, ct) : null;
        var visible = student is not null
                      && await access.VisibleTo(module.ClassCourseId, group, clock.UtcNow).AnyAsync(p => p.Module.Id == moduleId, ct);
        return visible ? (module, student, null) : (null, null, ApiProblems.NotFound());
    }

    private static ModuleCompletionDto Dto(LessonModule module, ModuleCompletion? row) =>
        new(module.Id, module.CompletionRule, row?.State ?? CompletionStates.NotStarted, ModuleMapper.Utc(row?.CompletedAtUtc), row?.Score);

    private static IResult ClassValidatorsInvalid() => Lms.Api.Features.Classes.ClassValidators.Invalid("studentId", "required");
}
