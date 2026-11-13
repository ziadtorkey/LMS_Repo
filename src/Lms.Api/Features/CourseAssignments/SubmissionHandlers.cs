using Lms.Api.Features.Classes;
using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Assignments;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Shared.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Assignments;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>
/// Student submission and the staff views of submissions (M4a Task 8, FR-ASG-02). The student endpoints act only on the caller's
/// own enrollment and only while the assignment module is visible to them (404 otherwise); lateness and refusal are decided on the
/// server clock; there is one live submission per student, also under concurrent submits.
/// </summary>
public sealed class SubmissionHandlers(
    LmsDbContext db, ModuleAccess access, IRichTextSanitizer sanitizer, IClock clock, ICurrentContext context, PlacementGuard guard,
    CompletionRecorder recorder)
{
    private AssignmentMapper Mapper => new(db);

    public async Task<IResult> MyAsync(Guid assignmentId, HttpContext http, CancellationToken ct)
    {
        var found = await StudentContextAsync(assignmentId, http, ct);
        if (found.Problem is not null)
        {
            return found.Problem;
        }

        var (assignment, module, cc, studentId) = (found.Assignment!, found.Module!, found.Cc!, found.StudentId);
        var now = clock.UtcNow;
        var submission = await db.Set<Submission>().FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId, ct);
        var code = await CannotSubmitAsync(assignment, submission, cc, now, ct);
        return Results.Ok(new MySubmissionDto(
            await Mapper.AssignmentAsync(assignment, module, false, false, now, ct),
            submission is null ? null : await Mapper.SubmissionAsync(submission, ct), code is null, code, now));
    }

    public async Task<IResult> SubmitAsync(Guid assignmentId, SubmitAssignmentRequest? request, HttpContext http, CancellationToken ct)
    {
        var found = await StudentContextAsync(assignmentId, http, ct);
        if (found.Problem is not null)
        {
            return found.Problem;
        }

        if (AssignmentValidators.ValidateSubmit(request) is { } invalid)
        {
            return invalid;
        }

        var (assignment, module, cc, studentId) = (found.Assignment!, found.Module!, found.Cc!, found.StudentId);
        var fileIds = request!.FileAssetIds ?? [];
        string? text;
        try
        {
            text = AssignmentValidators.Clean(sanitizer, request.TextHtml);
        }
        catch (RichTextTooLongException)
        {
            return ClassValidators.Invalid(nameof(SubmitAssignmentRequest.TextHtml), "too_long");
        }

        if ((text is not null && !assignment.AllowText) || (fileIds.Count > 0 && !assignment.AllowFiles)
            || (!request.SaveAsDraft && text is null && fileIds.Count == 0))
        {
            return ClassValidators.Invalid(nameof(SubmitAssignmentRequest.TextHtml), "content_required");
        }

        if (await GroupEndpoints.WritableAsync(cc, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var userId = context.UserId;
        var ownFiles = fileIds.Count == 0
            ? 0
            : await db.Set<Asset>().CountAsync(a => fileIds.Contains(a.Id) && a.ClassCourseId == cc.Id && a.CreatedBy == userId, ct);
        if (ownFiles != fileIds.Count)
        {
            return ClassValidators.Invalid(nameof(SubmitAssignmentRequest.FileAssetIds), "unknown_asset");
        }

        for (var attempt = 0; ; attempt++)
        {
            var now = clock.UtcNow;
            var submission = await db.Set<Submission>().FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId, ct);
            var isNew = submission is null;
            submission ??= new Submission(assignment, studentId);
            if (request.SaveAsDraft ? submission.Number > 0 : false)
            {
                return ApiProblems.Conflict(ProblemCodes.resubmission_not_allowed);
            }

            var code = request.SaveAsDraft ? null : await CannotSubmitAsync(assignment, isNew ? null : submission, cc, now, ct);
            if (code is not null)
            {
                return ApiProblems.Conflict(code);
            }

            if (request.SaveAsDraft)
            {
                submission.SaveDraft(text);
            }
            else
            {
                submission.HandIn(text, LatePolicyEvaluator.Evaluate(assignment.LatePolicy, assignment.DueAtUtc, now).IsLate, now);
            }

            if (isNew)
            {
                db.Add(submission);
            }
            else
            {
                db.RemoveRange(await db.Set<SubmissionFile>().Where(f => f.SubmissionId == submission.Id).ToListAsync(ct));
            }

            foreach (var assetId in fileIds)
            {
                db.Add(new SubmissionFile(submission, assetId));
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (attempt < 2 && DbErrors.IsConcurrentKeyConflict(ex))
            {
                // A concurrent submit created the live row first: start over on top of it.
                db.ChangeTracker.Clear();
                continue;
            }

            if (!request.SaveAsDraft)
            {
                await recorder.RecordAsync(module, studentId, CompletionSignal.Submitted, null, ct);
            }

            return Results.Ok(await Mapper.SubmissionAsync(submission, ct));
        }
    }

    public async Task<IResult> ListAsync(Guid assignmentId, SubmissionQuery query, HttpContext http, CancellationToken ct)
    {
        var assignment = await db.Set<Assignment>().FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.Grade, assignment.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Kind == ModuleKinds.Assignment && m.RefId == assignmentId, ct);
        var students = from e in db.Set<CourseEnrollment>()
                       join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
                       where e.ClassCourseId == cc!.Id && e.Status == CourseEnrollment.ActiveStatus
                       select s;
        if (module?.GroupId is { } groupId)
        {
            var members = db.Set<GroupMember>().Where(m => m.GroupId == groupId).Select(m => m.StudentId);
            students = students.Where(s => members.Contains(s.Id));
        }

        var rows = from s in students
                   join sub in db.Set<Submission>().Where(x => x.AssignmentId == assignmentId && x.Status != Submission.DraftStatus)
                       on s.Id equals sub.StudentId into subs
                   from sub in subs.DefaultIfEmpty()
                   select new { Student = s, Sub = sub };
        if (query.Status == "None")
        {
            rows = rows.Where(r => r.Sub == null);
        }
        else if (query.Status is { } status)
        {
            rows = rows.Where(r => r.Sub != null && r.Sub.Status == status);
        }

        if (query.IsLate is { } late)
        {
            rows = rows.Where(r => r.Sub != null && r.Sub.IsLate == late);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r => r.Student.NameAr.Contains(term) || r.Student.NameEn.Contains(term) || r.Student.StudentCode.Contains(term));
        }

        var total = await rows.CountAsync(ct);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var page = Math.Max(query.Page, 1);
        var pageRows = await rows.OrderBy(r => r.Student.NameEn).ThenBy(r => r.Student.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var items = pageRows.Select(r => new SubmissionListItemDto(
            r.Student.Id, r.Student.NameAr, r.Student.NameEn, r.Sub?.Id, r.Sub?.Status, ModuleMapper.Utc(r.Sub?.SubmittedAtUtc),
            r.Sub?.IsLate ?? false, null)).ToList();
        return Results.Ok(new PagedResult<SubmissionListItemDto>(items, total, page, size));
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var submission = await db.Set<Submission>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (submission is null)
        {
            return ApiProblems.NotFound();
        }

        if (await access.IsStudentAsync(ct))
        {
            var (_, problem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.View, submission.ClassCourseId, ct);
            return problem is not null || await access.StudentIdAsync(ct) != submission.StudentId
                ? problem ?? ApiProblems.NotFound()
                : Results.Ok(await Mapper.SubmissionAsync(submission, ct));
        }

        var (_, staffProblem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.Grade, submission.ClassCourseId, ct);
        return staffProblem is not null || submission.Status == Submission.DraftStatus
            ? staffProblem ?? ApiProblems.NotFound()
            : Results.Ok(await Mapper.SubmissionAsync(submission, ct));
    }

    private sealed record StudentContext(Assignment? Assignment, LessonModule? Module, ClassCourse? Cc, Guid StudentId, IResult? Problem);

    /// <summary>The calling student, their enrollment and the visible assignment module; a miss anywhere is a plain 404.</summary>
    private async Task<StudentContext> StudentContextAsync(Guid assignmentId, HttpContext http, CancellationToken ct)
    {
        var assignment = await db.Set<Assignment>().FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null)
        {
            return Miss(ApiProblems.NotFound());
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.Submit, assignment.ClassCourseId, ct);
        if (problem is not null)
        {
            return Miss(problem);
        }

        var studentId = await access.StudentIdAsync(ct);
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Kind == ModuleKinds.Assignment && m.RefId == assignmentId, ct);
        if (studentId is null || module is null || !await access.IsStudentAsync(ct)
            || !await AssignmentAccess.EnrolledAsync(db, assignment.ClassCourseId, studentId.Value, ct)
            || !await AssignmentAccess.StudentSeesAsync(db, access, module, clock.UtcNow, ct))
        {
            return Miss(ApiProblems.NotFound());
        }

        return new StudentContext(assignment, module, cc, studentId.Value, null);

        static StudentContext Miss(IResult p) => new(null, null, null, Guid.Empty, p);
    }

    /// <summary>The problem code a submit would get now (null = the student may submit), on the server clock.</summary>
    private async Task<string?> CannotSubmitAsync(Assignment a, Submission? existing, ClassCourse cc, DateTime now, CancellationToken ct)
    {
        if (await GroupEndpoints.WritableAsync(cc, db, guard, ct) is not null)
        {
            return ProblemCodes.submission_closed;
        }

        if (LatePolicyEvaluator.Evaluate(a.LatePolicy, a.DueAtUtc, now).Refused)
        {
            return ProblemCodes.submission_closed;
        }

        if (existing is null || existing.Number == 0)
        {
            return null;
        }

        var allowed = a.ResubmissionRule switch
        {
            ResubmissionRules.UntilDue => a.DueAtUtc is null || now <= a.DueAtUtc,
            ResubmissionRules.UntilGraded => existing.Status != SubmissionStatuses.Graded,
            _ => false,
        };
        // A returned submission may always be resubmitted when the rule is not None.
        if (a.ResubmissionRule != ResubmissionRules.None && existing.Status == SubmissionStatuses.Returned)
        {
            allowed = true;
        }

        return allowed ? null : ProblemCodes.resubmission_not_allowed;
    }
}
