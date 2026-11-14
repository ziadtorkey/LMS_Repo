using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Quizzes;

/// <summary>A question of a quiz: choice types carry options, an essay none (FR-QZ-01). Plain text, points 0.01 to 1000.</summary>
[Audited]
public sealed class QuizQuestion : UnitOwnedEntity
{
    private QuizQuestion()
    {
        Type = Text = string.Empty;
    }

    public QuizQuestion(Quiz quiz, int order, string type, string text, decimal points)
    {
        PlaceLike(quiz);
        QuizId = quiz.Id;
        ClassCourseId = quiz.ClassCourseId;
        Order = order;
        Type = type;
        Text = text;
        Points = points;
    }

    public Guid QuizId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public int Order { get; private set; }
    public string Type { get; private set; }
    public string Text { get; private set; }
    public decimal Points { get; private set; }

    public void Apply(string type, string text, decimal points)
    {
        Type = type;
        Text = text;
        Points = points;
    }

    public void SetOrder(int order) => Order = order;
}
