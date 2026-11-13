using Lms.Api.Features.Content;
using Lms.Domain.Content;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>Maps quiz rows to the builder DTOs. Only the builder (Quizzes.Manage) ever gets <c>IsCorrect</c>.</summary>
public sealed class QuizMapper(LmsDbContext db)
{
    public static QuizSettingsDto Settings(Quiz q) =>
        new(q.TimeLimitMinutes, q.MaxAttempts, q.GradingMethod, q.ShuffleQuestions, q.ShuffleOptions,
            ModuleMapper.Utc(q.OpensAtUtc), ModuleMapper.Utc(q.ClosesAtUtc), q.ReviewPolicy, q.DrawCount);

    public async Task<QuizDto> BuilderAsync(Quiz quiz, bool canEdit, DateTime nowUtc, CancellationToken ct)
    {
        var module = await db.Set<LessonModule>().FirstAsync(m => m.Id == quiz.ModuleId, ct);
        var groups = await ModuleMapper.GroupsAsync(db, [module], ct);
        var questions = await QuestionsAsync(quiz.Id, ct);
        var attempts = await db.Set<QuizAttempt>().CountAsync(a => a.QuizId == quiz.Id, ct);
        return new QuizDto(
            quiz.Id, quiz.ClassCourseId, quiz.LessonKey, ModuleMapper.Node(module, nowUtc, groups, null), quiz.DescriptionAr, quiz.DescriptionEn,
            Settings(quiz), questions.Sum(q => q.Points), questions, attempts, attempts > 0, canEdit, nowUtc);
    }

    public async Task<List<QuizQuestionDto>> QuestionsAsync(Guid quizId, CancellationToken ct)
    {
        var questions = await db.Set<QuizQuestion>().Where(q => q.QuizId == quizId).OrderBy(q => q.Order).ThenBy(q => q.Id).ToListAsync(ct);
        var options = await db.Set<QuizOption>().Where(o => o.QuizId == quizId).OrderBy(o => o.Order).ThenBy(o => o.Id).ToListAsync(ct);
        return questions.Select(q => Question(q, options.Where(o => o.QuestionId == q.Id))).ToList();
    }

    public static QuizQuestionDto Question(QuizQuestion q, IEnumerable<QuizOption> options) =>
        new(q.Id, q.Order, q.Type, q.Text, q.Points, options.Select(o => new QuizOptionDto(o.Id, o.Order, o.Text, o.IsCorrect)).ToList());

    /// <summary>The questions as a student taking the attempt sees them: never <c>IsCorrect</c>.</summary>
    public static List<AttemptQuestionDto> AttemptQuestions(IReadOnlyList<DrawnQuestion> drawn) =>
        drawn.Select((d, i) => new AttemptQuestionDto(
            d.Question.Id, i + 1, d.Question.Type, d.Question.Text, d.Question.Points,
            d.Options.Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList())).ToList();
}
