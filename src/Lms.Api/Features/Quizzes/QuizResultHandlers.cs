using Lms.Api.Features.Classes;
using Lms.Api.Features.Content;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.People;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// Reads and grading around attempts (M4a Task 11): the student start page, results by review policy, the staff attempt list and the
/// hand grading of essays. A student reads only their own; staff need Quizzes.View / Quizzes.Grade on the class course.
/// </summary>
public sealed class QuizResultHandlers(
    LmsDbContext db, ModuleAccess access, QuizResults results, AttemptService attempts, IClock clock)
{
    public async Task<IResult> SummaryAsync(Guid quizId, HttpContext http, CancellationToken ct)
    {
        var quiz = await db.Set<Quiz>().FirstOrDefaultAsync(q => q.Id == quizId, ct);
        if (quiz is null)
        {
            return ApiProblems.NotFound();
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.View, quiz.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!await access.IsStudentAsync(ct))
        {
            return AccessProblem.From(AccessDecision.Deny(AccessDenialReason.MissingPermission), http); // the summary is the student page
        }

        var studentId = await access.StudentIdAsync(ct);
        var group = studentId is { } s ? await access.GroupOfAsync(quiz.ClassCourseId, s, ct) : null;
        var visible = studentId is not null
                      && await access.VisibleTo(quiz.ClassCourseId, group, clock.UtcNow).AnyAsync(p => p.Module.Id == quiz.ModuleId, ct);
        return visible ? Results.Ok(await results.SummaryAsync(quiz, studentId!.Value, ct)) : ApiProblems.NotFound();
    }

    public async Task<IResult> ResultAsync(Guid attemptId, HttpContext http, CancellationToken ct)
    {
        var attempt = await db.Set<QuizAttempt>().FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null)
        {
            return ApiProblems.NotFound();
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.View, attempt.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var student = await access.IsStudentAsync(ct);
        if (student && await access.StudentIdAsync(ct) != attempt.StudentId)
        {
            return ApiProblems.NotFound();
        }

        // The result of an attempt in progress is not there yet (late readers get it auto-submitted first).
        if (attempt.Status == QuizAttempt.InProgress && !await attempts.ExpireIfDueAsync(attempt, ct))
        {
            return ApiProblems.NotFound();
        }

        return Results.Ok(await results.ResultAsync(attempt, staff: !student, ct));
    }

    public async Task<IResult> ListAsync(Guid quizId, int page, int pageSize, HttpContext http, CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > Paging.MaxPageSize)
        {
            return ClassValidators.Invalid(page < 1 ? nameof(page) : nameof(pageSize), "paging_invalid");
        }

        var quiz = await db.Set<Quiz>().FirstOrDefaultAsync(q => q.Id == quizId, ct);
        if (quiz is null)
        {
            return ApiProblems.NotFound();
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.View, quiz.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await access.IsStudentAsync(ct))
        {
            return AccessProblem.From(AccessDecision.Deny(AccessDenialReason.MissingPermission), http); // students read their own in the summary
        }

        var query = from a in db.Set<QuizAttempt>()
                    join s in db.Set<StudentProfile>() on a.StudentId equals s.Id
                    where a.QuizId == quizId
                    orderby s.NameEn, a.Number
                    select new { Attempt = a, s.NameAr, s.NameEn };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(r => new AttemptSummaryDto(
            r.Attempt.Id, r.Attempt.StudentId, r.NameAr, r.NameEn, r.Attempt.Number, r.Attempt.Status, r.Attempt.StartedAtUtc,
            r.Attempt.SubmittedAtUtc, r.Attempt.Score, r.Attempt.MaxScore, r.Attempt.HasPendingEssays)).ToList();
        return Results.Ok(new PagedResult<AttemptSummaryDto>(items, total, page, pageSize));
    }

    public async Task<IResult> GradeAsync(Guid attemptId, GradeAttemptRequest? request, HttpContext http, CancellationToken ct)
    {
        var attempt = await db.Set<QuizAttempt>().FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null)
        {
            return ApiProblems.NotFound();
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.Grade, attempt.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request?.Grades is not { Count: > 0 } grades)
        {
            return ClassValidators.Invalid("Grades", "required");
        }

        // Only a submitted attempt can be graded (a Graded one is a regrade of its essays).
        if (attempt.Status == QuizAttempt.InProgress)
        {
            return ClassValidators.Invalid("attempt", "not_submitted");
        }

        var answers = await db.Set<QuizAnswer>().Where(a => a.AttemptId == attemptId).ToListAsync(ct);
        var ids = answers.Select(a => a.QuestionId).ToList();
        var questions = await db.Set<QuizQuestion>().Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct);
        foreach (var grade in grades)
        {
            var answer = answers.FirstOrDefault(a => a.QuestionId == grade.QuestionId);
            if (answer is null || questions[answer.QuestionId].Type != Lms.Shared.Quizzes.QuestionTypes.Essay)
            {
                return ClassValidators.Invalid("QuestionId", "not_an_essay_of_the_attempt");
            }

            if (grade.Points < 0 || grade.Points > questions[answer.QuestionId].Points || decimal.Round(grade.Points, 2) != grade.Points
                || (grade.Feedback?.Length ?? 0) > 2_000)
            {
                return ClassValidators.Invalid("Points", "points_invalid");
            }
        }

        foreach (var grade in grades)
        {
            answers.Single(a => a.QuestionId == grade.QuestionId).Award(grade.Points, string.IsNullOrWhiteSpace(grade.Feedback) ? null : grade.Feedback.Trim());
        }

        await db.SaveChangesAsync(ct);
        var quiz = await db.Set<Quiz>().FirstAsync(q => q.Id == attempt.QuizId, ct);
        await attempts.GradeAsync(attempt, quiz, ct);
        return Results.Ok(await results.ResultAsync(attempt, staff: true, ct));
    }
}
