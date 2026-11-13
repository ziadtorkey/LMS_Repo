using Lms.Domain.Quizzes;

namespace Lms.Api.Features.Quizzes;

/// <summary>A drawn question with its options in the order this draw shows them.</summary>
public sealed record DrawnQuestion(QuizQuestion Question, IReadOnlyList<QuizOption> Options);

/// <summary>
/// The draw of one attempt (FR-QZ-02): the pool (<c>DrawCount</c> questions at random, null = all), then the shuffles. Used by a
/// real start and by the builder preview, so the preview shows exactly what a start would.
/// </summary>
public static class QuizDraw
{
    private const string TrueFalse = "TrueFalse";
    private const string Essay = "Essay";

    public static List<DrawnQuestion> Draw(
        Quiz quiz, IReadOnlyList<QuizQuestion> questions, IReadOnlyList<QuizOption> options, Random random)
    {
        var ordered = questions.OrderBy(q => q.Order).ThenBy(q => q.Id).ToList();
        if (quiz.DrawCount is { } draw && draw < ordered.Count)
        {
            ordered = Shuffled(ordered, random).Take(draw).OrderBy(q => q.Order).ThenBy(q => q.Id).ToList();
        }

        if (quiz.ShuffleQuestions)
        {
            ordered = Shuffled(ordered, random);
        }

        return ordered.Select(q =>
        {
            var own = options.Where(o => o.QuestionId == q.Id).OrderBy(o => o.Order).ThenBy(o => o.Id).ToList();
            // True / false keeps its two labels in place.
            var shown = quiz.ShuffleOptions && q.Type is not (TrueFalse or Essay) ? Shuffled(own, random) : own;
            return new DrawnQuestion(q, shown);
        }).ToList();
    }

    private static List<T> Shuffled<T>(IEnumerable<T> items, Random random)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
