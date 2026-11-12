using Lms.Api.Features.Content;
using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.People;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// What an attempt and a quiz show, and to whom (FR-QZ-02 review policy). The one rule for correct options: a student gets
/// <c>IsCorrect</c> only when the policy is <c>AnswersAndCorrect</c> AND they have no attempt left or the quiz has closed. Staff with
/// Quizzes.View on the class course always see everything.
/// </summary>
public sealed class QuizResults(LmsDbContext db, IClock clock, AttemptService attempts)
{
    public int? Remaining(QuizAttempt attempt) =>
        attempt.EndsAtUtc is { } ends ? Math.Max(0, (int)Math.Ceiling((ends - clock.UtcNow).TotalSeconds)) : null;

    /// <summary>The attempt as the student taking it sees it: the snapshot questions in order, never the correct options.</summary>
    public async Task<AttemptDto> AttemptAsync(QuizAttempt attempt, CancellationToken ct)
    {
        var answers = await db.Set<QuizAnswer>().Where(a => a.AttemptId == attempt.Id).OrderBy(a => a.Order).ToListAsync(ct);
        var ids = answers.Select(a => a.QuestionId).ToList();
        var questions = await db.Set<QuizQuestion>().Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct);
        var options = await db.Set<QuizOption>().Where(o => o.QuizId == attempt.QuizId).ToDictionaryAsync(o => o.Id, ct);
        var drawn = answers.Select(a => new DrawnQuestion(
            questions[a.QuestionId], a.OptionOrderIds.Where(options.ContainsKey).Select(i => options[i]).ToList())).ToList();
        var saved = answers.Where(a => a.Sequence > 0)
            .Select(a => new AttemptAnswerDto(a.QuestionId, a.SelectedIds, a.Text, a.Sequence)).ToList();
        return new AttemptDto(
            attempt.Id, attempt.QuizId, attempt.Number, attempt.Status, attempt.StartedAtUtc, Utc(attempt.EndsAtUtc),
            attempt.Status == QuizAttempt.InProgress ? Remaining(attempt) : null, clock.UtcNow, QuizMapper.AttemptQuestions(drawn), saved);
    }

    public async Task<IResult> StudentResultAsync(QuizAttempt attempt, CancellationToken ct) =>
        Results.Ok(await ResultAsync(attempt, staff: false, ct));

    public async Task<AttemptResultDto> ResultAsync(QuizAttempt attempt, bool staff, CancellationToken ct)
    {
        var quiz = await db.Set<Quiz>().FirstAsync(q => q.Id == attempt.QuizId, ct);
        var now = clock.UtcNow;
        var policy = quiz.ReviewPolicy;
        var pending = attempt.HasPendingEssays;
        var showQuestions = staff || policy is ReviewPolicies.Answers or ReviewPolicies.AnswersAndCorrect;
        var score = staff ? attempt.Score : (policy == ReviewPolicies.None || pending || attempt.Score is null ? null : attempt.Score);
        List<AttemptResultQuestionDto>? questions = null;
        if (showQuestions)
        {
            var showCorrect = staff || (policy == ReviewPolicies.AnswersAndCorrect && await CorrectAllowedAsync(quiz, attempt.StudentId, now, ct));
            questions = await QuestionsAsync(attempt, showCorrect, ct);
        }

        return new AttemptResultDto(
            attempt.Id, attempt.QuizId, attempt.Number, attempt.Status, policy, score, attempt.MaxScore, pending, questions, now);
    }

    /// <summary>Correct options reach a student only once they cannot carry them to a later attempt: none left, or the quiz closed.</summary>
    public async Task<bool> CorrectAllowedAsync(Quiz quiz, Guid studentId, DateTime now, CancellationToken ct)
    {
        if (quiz.ClosesAtUtc is { } closes && closes <= now)
        {
            return true;
        }

        if (quiz.MaxAttempts is not { } max)
        {
            return false; // unlimited attempts: only the close releases them
        }

        return await db.Set<QuizAttempt>().CountAsync(a => a.QuizId == quiz.Id && a.StudentId == studentId, ct) >= max;
    }

    private async Task<List<AttemptResultQuestionDto>> QuestionsAsync(QuizAttempt attempt, bool showCorrect, CancellationToken ct)
    {
        var answers = await db.Set<QuizAnswer>().Where(a => a.AttemptId == attempt.Id).OrderBy(a => a.Order).ToListAsync(ct);
        var ids = answers.Select(a => a.QuestionId).ToList();
        var questions = await db.Set<QuizQuestion>().Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct);
        var options = await db.Set<QuizOption>().Where(o => o.QuizId == attempt.QuizId).ToDictionaryAsync(o => o.Id, ct);
        return answers.Select(a =>
        {
            var q = questions[a.QuestionId];
            var chosen = a.SelectedIds.ToHashSet();
            var shown = a.OptionOrderIds.Where(options.ContainsKey).Select(i => options[i])
                .Select(o => new AttemptResultOptionDto(o.Id, o.Text, chosen.Contains(o.Id), showCorrect ? o.IsCorrect : null)).ToList();
            return new AttemptResultQuestionDto(
                q.Id, a.Order, q.Type, q.Text, q.Points, a.AwardedPoints, shown, q.Type == QuestionTypes.Essay ? a.Text : null, a.Feedback);
        }).ToList();
    }

    /// <summary>The student start page: the state of the quiz and their attempts, with scores shown per the review policy.</summary>
    public async Task<QuizSummaryDto> SummaryAsync(Quiz quiz, Guid studentId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var module = await db.Set<LessonModule>().FirstAsync(m => m.Id == quiz.ModuleId, ct);
        var mine = await db.Set<QuizAttempt>().Where(a => a.QuizId == quiz.Id && a.StudentId == studentId).OrderBy(a => a.Number).ToListAsync(ct);
        var student = await db.Set<StudentProfile>().FirstAsync(s => s.Id == studentId, ct);
        var questionCount = await db.Set<QuizQuestion>().CountAsync(q => q.QuizId == quiz.Id, ct);
        var hide = quiz.ReviewPolicy == ReviewPolicies.None;
        var totalPoints = await db.Set<QuizQuestion>().Where(q => q.QuizId == quiz.Id).SumAsync(q => (decimal?)q.Points, ct) ?? 0m;
        var graded = await attempts.GradedAttemptsAsync(quiz.Id, studentId, ct);
        var final = hide ? null : QuizScoring.Final(graded, quiz.GradingMethod, totalPoints);
        var summaries = mine.Select(a => new AttemptSummaryDto(
            a.Id, studentId, student.NameAr, student.NameEn, a.Number, a.Status, a.StartedAtUtc, a.SubmittedAtUtc,
            hide || a.HasPendingEssays ? null : a.Score, a.MaxScore, a.HasPendingEssays)).ToList();
        var open = (quiz.OpensAtUtc is null || quiz.OpensAtUtc <= now) && (quiz.ClosesAtUtc is null || now < quiz.ClosesAtUtc) && questionCount > 0;
        return new QuizSummaryDto(
            quiz.Id, module.Id, module.TitleAr, module.TitleEn, quiz.DescriptionAr, quiz.DescriptionEn, questionCount, quiz.TimeLimitMinutes,
            quiz.MaxAttempts, mine.Count, quiz.MaxAttempts is { } max ? Math.Max(0, max - mine.Count) : null, Utc(quiz.OpensAtUtc),
            Utc(quiz.ClosesAtUtc), quiz.GradingMethod, quiz.ReviewPolicy, open, mine.FirstOrDefault(a => a.Status == QuizAttempt.InProgress)?.Id,
            summaries, final?.Points, now);
    }

    private static DateTime? Utc(DateTime? value) => ModuleMapper.Utc(value);
}
