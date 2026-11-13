using Lms.Domain.Common;

namespace Lms.Domain.Quizzes;

/// <summary>
/// The answer to one question of an attempt. Created at start for every drawn question: it holds the snapshot order and the
/// option order of that attempt, then the autosaved answer. <see cref="Sequence"/> is the client counter (last write wins by
/// sequence). Unique per (attempt, question).
/// </summary>
public sealed class QuizAnswer : Entity, IOrgUnitScoped
{
    private QuizAnswer()
    {
        OrgUnitPath = OptionOrder = SelectedOptionIds = string.Empty;
    }

    public QuizAnswer(QuizAttempt attempt, Guid questionId, int order, IReadOnlyList<Guid> optionOrder)
    {
        OrganizationId = attempt.OrganizationId;
        OrgUnitId = attempt.OrgUnitId;
        OrgUnitPath = attempt.OrgUnitPath;
        AttemptId = attempt.Id;
        QuizId = attempt.QuizId;
        ClassCourseId = attempt.ClassCourseId;
        QuestionId = questionId;
        Order = order;
        OptionOrder = string.Join(',', optionOrder);
        SelectedOptionIds = string.Empty;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid QuizId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid QuestionId { get; private set; }
    public int Order { get; private set; }

    /// <summary>The option ids of this attempt in the order shown (comma separated).</summary>
    public string OptionOrder { get; private set; }

    /// <summary>The selected option ids (comma separated).</summary>
    public string SelectedOptionIds { get; private set; }

    public string? Text { get; private set; }
    public long Sequence { get; private set; }
    public decimal? AwardedPoints { get; private set; }
    public string? Feedback { get; private set; }

    public IReadOnlyList<Guid> OptionOrderIds => Split(OptionOrder);
    public IReadOnlyList<Guid> SelectedIds => Split(SelectedOptionIds);

    /// <summary>Saves the answer when the sequence is above the stored one; returns false (and keeps the old one) otherwise.</summary>
    public bool Save(IReadOnlyList<Guid> selected, string? text, long sequence)
    {
        if (sequence <= Sequence)
        {
            return false;
        }

        SelectedOptionIds = string.Join(',', selected);
        Text = text;
        Sequence = sequence;
        return true;
    }

    public void Award(decimal? points, string? feedback = null)
    {
        AwardedPoints = points;
        Feedback = feedback;
    }

    private static List<Guid> Split(string value) =>
        value.Length == 0 ? [] : value.Split(',').Select(Guid.Parse).ToList();
}
