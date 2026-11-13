using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Grading;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// The attempt life cycle (M4a Task 11, FR-QZ-02, FR-QZ-03): start, autosave, submit, auto-submit and grading. Every deadline is
/// judged on the server clock. A start races on the unique (quiz, student, number) index: the loser reads again and gets the attempt in
/// progress or the limit answer, so the attempt limit holds under concurrency.
/// </summary>
public sealed class AttemptService(
    LmsDbContext db, IClock clock, PlacementGuard guard, CompletionRecorder recorder, IGradeEvents gradeEvents, ISettingsResolver settings)
{
    public async Task<int> GraceSecondsAsync(string orgUnitPath, CancellationToken ct) =>
        Math.Clamp((await settings.ResolveAsync(SettingKeys.QuizzesAutoSubmitGraceSeconds, orgUnitPath, ct)).Value, 0, 300);

    /// <summary>Starts an attempt, or returns the one in progress (<c>Created</c> false).</summary>
    public async Task<(IResult? Problem, QuizAttempt? Attempt, bool Created)> StartAsync(Guid quizId, Guid studentId, CancellationToken ct)
    {
        for (var tries = 0; ; tries++)
        {
            try
            {
                return await StartOnceAsync(quizId, studentId, ct);
            }
            catch (DbUpdateException ex) when (tries < 3 && DbErrors.IsConcurrentKeyConflict(ex))
            {
                db.ChangeTracker.Clear(); // a concurrent start took the number: read again
            }
        }
    }

    private async Task<(IResult?, QuizAttempt?, bool)> StartOnceAsync(Guid quizId, Guid studentId, CancellationToken ct)
    {
        var quiz = await db.Set<Quiz>().FirstAsync(q => q.Id == quizId, ct);
        var now = clock.UtcNow;
        var open = await db.Set<QuizAttempt>().FirstOrDefaultAsync(
            a => a.QuizId == quizId && a.StudentId == studentId && a.Status == QuizAttempt.InProgress, ct);
        if (open is not null)
        {
            if (!await ExpireIfDueAsync(open, ct))
            {
                return (null, open, false);
            }
        }

        var questions = await db.Set<QuizQuestion>().Where(q => q.QuizId == quizId).ToListAsync(ct);
        if (quiz.OpensAtUtc > now || questions.Count == 0)
        {
            return (ApiProblems.Conflict(ProblemCodes.quiz_not_open), null, false);
        }

        if (quiz.ClosesAtUtc <= now)
        {
            return (ApiProblems.Conflict(ProblemCodes.quiz_closed), null, false);
        }

        var classCourse = await db.Set<ClassCourse>().FirstAsync(c => c.Id == quiz.ClassCourseId, ct);
        if (await GroupEndpoints.WritableAsync(classCourse, db, guard, ct) is { } closed)
        {
            return (closed, null, false);
        }

        var used = await db.Set<QuizAttempt>().Where(a => a.QuizId == quizId && a.StudentId == studentId).Select(a => (int?)a.Number).MaxAsync(ct) ?? 0;
        if (quiz.MaxAttempts is { } max && await db.Set<QuizAttempt>().CountAsync(a => a.QuizId == quizId && a.StudentId == studentId, ct) >= max)
        {
            return (ApiProblems.Conflict(ProblemCodes.attempt_limit_reached), null, false);
        }

        var options = await db.Set<QuizOption>().Where(o => o.QuizId == quizId).ToListAsync(ct);
        var drawn = QuizDraw.Draw(quiz, questions, options, Random.Shared);
        DateTime? ends = quiz.TimeLimitMinutes is { } minutes ? now.AddMinutes(minutes) : null;
        if (quiz.ClosesAtUtc is { } closes && (ends is null || closes < ends))
        {
            ends = closes;
        }

        var attempt = new QuizAttempt(quiz, studentId, used + 1, now, ends, drawn.Sum(d => d.Question.Points));
        db.Add(attempt);
        for (var i = 0; i < drawn.Count; i++)
        {
            db.Add(new QuizAnswer(attempt, drawn[i].Question.Id, i + 1, drawn[i].Options.Select(o => o.Id).ToList()));
        }

        await db.SaveChangesAsync(ct);
        return (null, attempt, true);
    }

    /// <summary>Submits an in-progress attempt that has run past its end and grace (the lazy half of the auto-submit). True when it did.</summary>
    public async Task<bool> ExpireIfDueAsync(QuizAttempt attempt, CancellationToken ct, bool ignoreGrace = false)
    {
        if (attempt.Status != QuizAttempt.InProgress || attempt.EndsAtUtc is null)
        {
            return false;
        }

        var grace = ignoreGrace ? 0 : await GraceSecondsAsync(attempt.OrgUnitPath, ct);
        if (clock.UtcNow <= attempt.EndsAtUtc.Value.AddSeconds(grace))
        {
            return false;
        }

        await SubmitAsync(attempt, QuizAttempt.AutoSubmitted, ct);
        return true;
    }

    /// <summary>Marks the attempt submitted and grades it; false when it was already submitted (a retry or a race), which is harmless.</summary>
    public async Task<bool> SubmitAsync(QuizAttempt attempt, string status, CancellationToken ct)
    {
        var now = QuizAttempt.Micros(clock.UtcNow);
        var affected = await db.Set<QuizAttempt>().Where(a => a.Id == attempt.Id && a.Status == QuizAttempt.InProgress)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status).SetProperty(a => a.SubmittedAtUtc, now), ct);
        await db.Entry(attempt).ReloadAsync(ct);
        if (affected == 0)
        {
            return false;
        }

        var quiz = await db.Set<Quiz>().FirstAsync(q => q.Id == attempt.QuizId, ct);
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == quiz.ModuleId, ct);
        if (module is { CompletionRule: CompletionRules.Submit })
        {
            await recorder.RecordAsync(module, attempt.StudentId, CompletionSignal.Submitted, null, ct);
        }

        await GradeAsync(attempt, quiz, ct);
        return true;
    }

    /// <summary>
    /// Grades the objective questions and totals the attempt; essays stay pending. When nothing is pending the attempt is <c>Graded</c>
    /// and the quiz score, the grade event and the completion follow.
    /// </summary>
    public async Task GradeAsync(QuizAttempt attempt, Quiz quiz, CancellationToken ct)
    {
        var answers = await db.Set<QuizAnswer>().Where(a => a.AttemptId == attempt.Id).ToListAsync(ct);
        var ids = answers.Select(a => a.QuestionId).ToList();
        var questions = await db.Set<QuizQuestion>().Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct);
        var options = await db.Set<QuizOption>().Where(o => o.QuizId == quiz.Id).ToListAsync(ct);
        foreach (var answer in answers)
        {
            var question = questions[answer.QuestionId];
            if (question.Type != QuestionTypes.Essay)
            {
                answer.Award(QuizScoring.GradeObjective(question, options.Where(o => o.QuestionId == question.Id).ToList(), answer.SelectedIds));
            }
        }

        var pending = answers.Any(a => questions[a.QuestionId].Type == QuestionTypes.Essay && a.AwardedPoints is null);
        var total = answers.Sum(a => a.AwardedPoints ?? 0m);
        attempt.SetResult(pending ? attempt.Status : QuizAttempt.Graded, total, pending);
        await db.SaveChangesAsync(ct);
        if (!pending)
        {
            await OnGradedAsync(attempt, quiz, ct);
        }
    }

    /// <summary>The quiz score by grading method, a new grade event revision when it changed, and the completion signal.</summary>
    public async Task OnGradedAsync(QuizAttempt attempt, Quiz quiz, CancellationToken ct)
    {
        var graded = await GradedAttemptsAsync(quiz.Id, attempt.StudentId, ct);
        var totalPoints = await db.Set<QuizQuestion>().Where(q => q.QuizId == quiz.Id).SumAsync(q => (decimal?)q.Points, ct) ?? 0m;
        if (QuizScoring.Final(graded, quiz.GradingMethod, totalPoints) is not { } final)
        {
            return;
        }

        var latest = await db.Set<GradeEvent>().Where(e => e.SourceKind == GradeSourceKind.Quiz && e.SourceId == quiz.Id && e.StudentProfileId == attempt.StudentId)
            .OrderByDescending(e => e.Revision).FirstOrDefaultAsync(ct);
        if (latest is null || latest.Points != final.Points || latest.MaxPoints != final.MaxPoints)
        {
            await gradeEvents.RecordAsync(new GradeEventInput(
                GradeSourceKind.Quiz, quiz.Id, quiz.ClassCourseId, attempt.StudentId, final.Points, final.MaxPoints, clock.UtcNow,
                (latest?.Revision ?? 0) + 1), ct);
            await db.SaveChangesAsync(ct);
        }

        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == quiz.ModuleId, ct);
        if (module is { CompletionRule: CompletionRules.Score })
        {
            await recorder.RecordAsync(module, attempt.StudentId, CompletionSignal.Scored, final.Percent, ct);
        }
    }

    public async Task<List<GradedAttempt>> GradedAttemptsAsync(Guid quizId, Guid studentId, CancellationToken ct) =>
        (await db.Set<QuizAttempt>().Where(a => a.QuizId == quizId && a.StudentId == studentId && a.Status == QuizAttempt.Graded && a.Score != null)
            .OrderBy(a => a.Number).Select(a => new { a.Number, Score = a.Score!.Value, a.MaxScore }).ToListAsync(ct))
        .Select(a => new GradedAttempt(a.Number, a.Score, a.MaxScore)).ToList();
}
