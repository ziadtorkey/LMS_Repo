using Lms.Domain.Common;

namespace Lms.Domain.Quizzes;

/// <summary>
/// One attempt of a student at a quiz (FR-QZ-03). Not audited row by row (volume). The question set and order are a snapshot
/// taken at start (the <see cref="QuizAnswer"/> rows). Unique per (quiz, student, number).
/// </summary>
public sealed class QuizAttempt : Entity, IOrgUnitScoped
{
    // The same values as Lms.Shared.Quizzes.AttemptStatuses (a unit test pins them).
    public const string InProgress = "InProgress";
    public const string Submitted = "Submitted";
    public const string AutoSubmitted = "AutoSubmitted";
    public const string Graded = "Graded";

    private QuizAttempt()
    {
        OrgUnitPath = Status = string.Empty;
    }

    public QuizAttempt(Quiz quiz, Guid studentId, int number, DateTime startedAtUtc, DateTime? endsAtUtc, decimal maxScore)
    {
        OrganizationId = quiz.OrganizationId;
        OrgUnitId = quiz.OrgUnitId;
        OrgUnitPath = quiz.OrgUnitPath;
        QuizId = quiz.Id;
        ClassCourseId = quiz.ClassCourseId;
        StudentId = studentId;
        Number = number;
        Status = InProgress;
        StartedAtUtc = Micros(startedAtUtc);
        EndsAtUtc = endsAtUtc is { } e ? Micros(e) : null;
        MaxScore = maxScore;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid QuizId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid StudentId { get; private set; }
    public int Number { get; private set; }
    public string Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? EndsAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public decimal? Score { get; private set; }
    public decimal MaxScore { get; private set; }
    public bool HasPendingEssays { get; private set; }

    /// <summary>MySql keeps microseconds: the value the caller sees is the value that is stored.</summary>
    public static DateTime Micros(DateTime value) => new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);

    public void Submit(string status, DateTime nowUtc)
    {
        Status = status;
        SubmittedAtUtc = Micros(nowUtc);
    }

    public void SetResult(string status, decimal score, bool hasPendingEssays)
    {
        Status = status;
        Score = score;
        HasPendingEssays = hasPendingEssays;
    }
}
