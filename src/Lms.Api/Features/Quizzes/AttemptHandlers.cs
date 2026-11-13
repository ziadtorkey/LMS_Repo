using Lms.Api.Features.Classes;
using Lms.Api.Features.Content;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// The student side of an attempt (M4a Task 11): start, resume, autosave and submit. Every call needs Quizzes.Attempt, the
/// relationship to the class course and an attempt of the caller; the answers of an attempt are the caller own (404 otherwise).
/// A student never receives correct options here: <see cref="AttemptQuestionDto"/> has none.
/// </summary>
public sealed class AttemptHandlers(
    LmsDbContext db, ModuleAccess access, AttemptService attempts, QuizResults results, IClock clock)
{
    public const int MaxEssayLength = 20_000;

    public async Task<IResult> StartAsync(Guid quizId, HttpContext http, CancellationToken ct)
    {
        var quiz = await db.Set<Quiz>().FirstOrDefaultAsync(q => q.Id == quizId, ct);
        if (quiz is null)
        {
            return ApiProblems.NotFound();
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.Attempt, quiz.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var studentId = await VisibleStudentAsync(quiz, ct);
        if (studentId is null)
        {
            return ApiProblems.NotFound();
        }

        var (startProblem, attempt, created) = await attempts.StartAsync(quizId, studentId.Value, ct);
        if (startProblem is not null)
        {
            return startProblem;
        }

        var dto = await results.AttemptAsync(attempt!, ct);
        return created ? Results.Created(Routes.QuizAttempt(attempt!.Id), dto) : Results.Ok(dto);
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (attempt, problem) = await OwnAttemptAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        await attempts.ExpireIfDueAsync(attempt!, ct); // a late reader sees the auto-submitted state
        return Results.Ok(await results.AttemptAsync(attempt!, ct));
    }

    public async Task<IResult> SaveAnswerAsync(Guid id, SaveAnswerRequest? request, HttpContext http, CancellationToken ct)
    {
        var (attempt, problem) = await OwnAttemptAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null)
        {
            return ClassValidators.Invalid("body", "required");
        }

        if (attempt!.Status != QuizAttempt.InProgress || await attempts.ExpireIfDueAsync(attempt, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.attempt_expired);
        }

        var answer = await db.Set<QuizAnswer>().FirstOrDefaultAsync(a => a.AttemptId == id && a.QuestionId == request.QuestionId, ct);
        if (answer is null)
        {
            return ClassValidators.Invalid(nameof(request.QuestionId), "question_not_in_attempt");
        }

        var question = await db.Set<QuizQuestion>().FirstAsync(q => q.Id == request.QuestionId, ct);
        var selected = request.SelectedOptionIds ?? [];
        if (!ValidAnswer(question, answer, selected, request.Text))
        {
            return ClassValidators.Invalid("answer", "answer_invalid");
        }

        var ids = string.Join(',', selected);
        var text = question.Type == QuestionTypes.Essay ? request.Text : null;
        var sequence = request.Sequence;
        // One conditional update: a lower sequence arriving later never overwrites a higher one, whatever the order of arrival.
        await db.Set<QuizAnswer>().Where(a => a.Id == answer.Id && a.Sequence < sequence)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SelectedOptionIds, ids).SetProperty(a => a.Text, text).SetProperty(a => a.Sequence, sequence), ct);
        var stored = await db.Set<QuizAnswer>().AsNoTracking().Where(a => a.Id == answer.Id).Select(a => a.Sequence).FirstAsync(ct);
        return Results.Ok(new SaveAnswerResultDto(request.QuestionId, stored, results.Remaining(attempt), clock.UtcNow));
    }

    public async Task<IResult> SubmitAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (attempt, problem) = await OwnAttemptAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (attempt!.Status == QuizAttempt.InProgress && !await attempts.ExpireIfDueAsync(attempt, ct))
        {
            await attempts.SubmitAsync(attempt, QuizAttempt.Submitted, ct); // idempotent: a second submit finds it already submitted
        }

        return await results.StudentResultAsync(attempt, ct);
    }

    private static bool ValidAnswer(QuizQuestion question, QuizAnswer answer, IReadOnlyList<Guid> selected, string? text)
    {
        if (question.Type == QuestionTypes.Essay)
        {
            return selected.Count == 0 && (text?.Length ?? 0) <= MaxEssayLength;
        }

        var allowed = answer.OptionOrderIds;
        var single = question.Type is QuestionTypes.SingleChoice or QuestionTypes.TrueFalse;
        return text is null && selected.Distinct().Count() == selected.Count && selected.All(allowed.Contains) && (!single || selected.Count <= 1);
    }

    /// <summary>The student, only when the quiz module is visible to them now (D51): any other reader gets 404.</summary>
    private async Task<Guid?> VisibleStudentAsync(Quiz quiz, CancellationToken ct)
    {
        if (!await access.IsStudentAsync(ct) || await access.StudentIdAsync(ct) is not { } studentId)
        {
            return null;
        }

        var group = await access.GroupOfAsync(quiz.ClassCourseId, studentId, ct);
        var visible = await access.VisibleTo(quiz.ClassCourseId, group, clock.UtcNow).AnyAsync(p => p.Module.Id == quiz.ModuleId, ct);
        return visible ? studentId : null;
    }

    private async Task<(QuizAttempt? Attempt, IResult? Problem)> OwnAttemptAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var attempt = await db.Set<QuizAttempt>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attempt is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.Attempt, attempt.ClassCourseId, ct);
        if (problem is not null)
        {
            return (null, problem);
        }

        var studentId = await access.StudentIdAsync(ct);
        return studentId == attempt.StudentId && await access.IsStudentAsync(ct) ? (attempt, null) : (null, ApiProblems.NotFound());
    }
}
