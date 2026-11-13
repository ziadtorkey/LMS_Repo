using Lms.Domain.Quizzes;
using Lms.Shared.Quizzes;

namespace Lms.Api.Features.Quizzes;

/// <summary>An attempt reduced to what the grading methods compare.</summary>
public sealed record GradedAttempt(int Number, decimal Score, decimal MaxScore)
{
    public decimal Percent => MaxScore == 0 ? 0 : Score * 100m / MaxScore;
}

/// <summary>The quiz result of a student: the percentage and the points normalised to the quiz total (the grade event).</summary>
public sealed record FinalScore(decimal Percent, decimal Points, decimal MaxPoints);

/// <summary>Pure grading rules (FR-QZ-02, FR-QZ-03): objective questions all-or-nothing, and the grading methods over percentages.</summary>
public static class QuizScoring
{
    /// <summary>
    /// Points of an objective answer: full points when the selected options are exactly the correct ones (single choice and true / false
    /// have one correct option, multiple choice is all-or-nothing), else 0. Null for an essay (graded by hand).
    /// </summary>
    public static decimal? GradeObjective(QuizQuestion question, IReadOnlyList<QuizOption> options, IReadOnlyList<Guid> selected)
    {
        if (question.Type == QuestionTypes.Essay)
        {
            return null;
        }

        var correct = options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        return selected.Count > 0 && selected.Distinct().Count() == correct.Count && selected.All(correct.Contains) ? question.Points : 0m;
    }

    /// <summary>
    /// The quiz score from the graded attempts by method: <c>Highest</c> the best percentage, <c>Last</c> the latest attempt, <c>Average</c>
    /// the mean percentage. Percentages make attempts with different pools comparable; the points are that percentage of the quiz total.
    /// </summary>
    public static FinalScore? Final(IReadOnlyList<GradedAttempt> attempts, string method, decimal totalPoints)
    {
        if (attempts.Count == 0)
        {
            return null;
        }

        var percent = method switch
        {
            GradingMethods.Last => attempts.OrderBy(a => a.Number).Last().Percent,
            GradingMethods.Average => attempts.Average(a => a.Percent),
            _ => attempts.Max(a => a.Percent),
        };
        percent = decimal.Round(percent, 2);
        return new FinalScore(percent, decimal.Round(percent * totalPoints / 100m, 2), totalPoints);
    }
}
