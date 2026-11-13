using Lms.Api.Features.Classes;
using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Assignments;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Grading;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>
/// Grading of submissions (M4a Task 9, FR-ASG-02): the teacher saves a grade and feedback (the grade is 0..MaxGrade, a late
/// hand-in under the Penalty policy loses the penalty percent), then returns it. Only the return releases the grade to the student:
/// it sets Returned, writes the grade event (next revision when the returned grade changed, so the gradebook never sees an unreleased
/// grade), records the Score completion signal and notifies the student. Writes need CourseAssignments.Grade on the class course
/// (relationship rule) and a writable class.
/// </summary>
public sealed class GradingHandlers(
    LmsDbContext db, IRichTextSanitizer sanitizer, IClock clock, PlacementGuard guard, IGradeEvents gradeEvents, CompletionRecorder recorder,
    CourseAssignmentNotifier notifier)
{
    private AssignmentMapper Mapper => new(db);

    public async Task<IResult> GradeAsync(Guid submissionId, GradeSubmissionRequest? request, HttpContext http, CancellationToken ct)
    {
        var found = await LoadAsync(submissionId, http, ct);
        if (found.Problem is not null)
        {
            return found.Problem;
        }

        var (submission, assignment, cc) = (found.Submission!, found.Assignment!, found.Cc!);
        if (request is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        if (request.Grade < 0 || request.Grade > assignment.MaxGrade || decimal.Round(request.Grade, 2) != request.Grade)
        {
            return ClassValidators.Invalid(nameof(GradeSubmissionRequest.Grade), "grade_out_of_range");
        }

        string? feedback;
        try
        {
            feedback = AssignmentValidators.Clean(sanitizer, request.FeedbackHtml);
        }
        catch (RichTextTooLongException)
        {
            return ClassValidators.Invalid(nameof(GradeSubmissionRequest.FeedbackHtml), "too_long");
        }

        if (request.AnnotatedFileAssetId is { } assetId
            && !await db.Set<Asset>().AnyAsync(a => a.Id == assetId && a.ClassCourseId == cc.Id, ct))
        {
            return ClassValidators.Invalid(nameof(GradeSubmissionRequest.AnnotatedFileAssetId), "unknown_asset");
        }

        if (await GroupEndpoints.WritableAsync(cc, db, guard, ct) is { } closed)
        {
            return closed;
        }

        int? penalty = submission.IsLate && assignment.LatePolicy == LatePolicies.Penalty ? assignment.PenaltyPercent : null;
        var final = penalty is { } p ? decimal.Round(request.Grade * (100 - p) / 100m, 2) : request.Grade;
        var row = await db.Set<SubmissionFeedback>().FirstOrDefaultAsync(f => f.SubmissionId == submission.Id, ct);
        if (row is null)
        {
            row = new SubmissionFeedback(submission);
            db.Add(row);
        }

        row.Set(request.Grade, penalty, final, feedback, request.AnnotatedFileAssetId, clock.UtcNow);
        submission.MarkGraded();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Mapper.SubmissionAsync(submission, false, ct));
    }

    public async Task<IResult> ReturnAsync(Guid submissionId, HttpContext http, CancellationToken ct)
    {
        var found = await LoadAsync(submissionId, http, ct);
        if (found.Problem is not null)
        {
            return found.Problem;
        }

        var (submission, assignment, cc) = (found.Submission!, found.Assignment!, found.Cc!);
        var row = await db.Set<SubmissionFeedback>().FirstOrDefaultAsync(f => f.SubmissionId == submission.Id, ct);
        if (row is null || submission.Status is not (Submission.GradedStatus or Submission.ReturnedStatus))
        {
            // Nothing saved to release, or a newer hand-in is waiting for a new grade.
            return ClassValidators.Invalid("Status", "not_graded");
        }

        if (submission.Status == Submission.ReturnedStatus)
        {
            return Results.Ok(await Mapper.SubmissionAsync(submission, false, ct)); // already released: nothing changes, nobody is told again
        }

        if (await GroupEndpoints.WritableAsync(cc, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var now = clock.UtcNow;
        var revision = row.Return(now);
        submission.MarkReturned();
        if (revision is { } number)
        {
            await gradeEvents.RecordAsync(
                new GradeEventInput(GradeSourceKind.Assignment, assignment.Id, cc.Id, submission.StudentId, row.FinalGrade, assignment.MaxGrade, now, number), ct);
        }

        await db.SaveChangesAsync(ct);

        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Kind == Lms.Shared.Content.ModuleKinds.Assignment && m.RefId == assignment.Id, ct);
        if (module is not null)
        {
            await recorder.RecordAsync(module, submission.StudentId, CompletionSignal.Scored, row.FinalGrade, ct);
            await notifier.GradedAsync(assignment, module, cc, submission.StudentId, ct);
        }

        return Results.Ok(await Mapper.SubmissionAsync(submission, false, ct));
    }

    private sealed record Loaded(Submission? Submission, Assignment? Assignment, ClassCourse? Cc, IResult? Problem);

    /// <summary>A handed-in submission (a draft is invisible to staff) in a class course the caller may grade; a miss is a plain 404.</summary>
    private async Task<Loaded> LoadAsync(Guid submissionId, HttpContext http, CancellationToken ct)
    {
        var submission = await db.Set<Submission>().FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null || submission.Status == Submission.DraftStatus)
        {
            return new Loaded(null, null, null, ApiProblems.NotFound());
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.CourseAssignments.Grade, submission.ClassCourseId, ct);
        if (problem is not null)
        {
            return new Loaded(null, null, null, problem);
        }

        var assignment = await db.Set<Assignment>().FirstOrDefaultAsync(a => a.Id == submission.AssignmentId, ct);
        return assignment is null
            ? new Loaded(null, null, null, ApiProblems.NotFound())
            : new Loaded(submission, assignment, cc, null);
    }
}
