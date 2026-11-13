using Lms.Domain.Common;

namespace Lms.Domain.Quizzes;

/// <summary>An option of a choice question. <see cref="IsCorrect"/> never reaches a student before the review policy allows it.</summary>
public sealed class QuizOption : UnitOwnedEntity
{
    private QuizOption()
    {
        Text = string.Empty;
    }

    public QuizOption(QuizQuestion question, int order, string text, bool isCorrect)
    {
        PlaceLike(question);
        QuestionId = question.Id;
        QuizId = question.QuizId;
        Order = order;
        Text = text;
        IsCorrect = isCorrect;
    }

    public Guid QuestionId { get; private set; }
    public Guid QuizId { get; private set; }
    public int Order { get; private set; }
    public string Text { get; private set; }
    public bool IsCorrect { get; private set; }

    public void Apply(int order, string text, bool isCorrect)
    {
        Order = order;
        Text = text;
        IsCorrect = isCorrect;
    }
}
