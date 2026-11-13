namespace Lms.Shared.Quizzes;

/// <summary>
/// Question types (FR-QZ-01). <c>SingleChoice</c>: 2–10 options, exactly one correct. <c>MultipleChoice</c>: 2–10
/// options, at least one correct (all-or-nothing scoring). <c>TrueFalse</c>: exactly two options (the client fills
/// the true / false labels), one correct. <c>Essay</c>: no options, graded by hand (<c>Quizzes.Grade</c>).
/// </summary>
public static class QuestionTypes
{
    public const string SingleChoice = "SingleChoice";
    public const string MultipleChoice = "MultipleChoice";
    public const string TrueFalse = "TrueFalse";
    public const string Essay = "Essay";

    public static IReadOnlyList<string> All { get; } = [SingleChoice, MultipleChoice, TrueFalse, Essay];

    /// <summary>The types the server grades on submit.</summary>
    public static IReadOnlyList<string> AutoGraded { get; } = [SingleChoice, MultipleChoice, TrueFalse];
}

/// <summary>How the quiz score is taken from a student's graded attempts (FR-QZ-02).</summary>
public static class GradingMethods
{
    public const string Highest = "Highest";
    public const string Last = "Last";
    public const string Average = "Average";

    public static IReadOnlyList<string> All { get; } = [Highest, Last, Average];
}

/// <summary>
/// What a student sees of a finished attempt (FR-QZ-02). <c>None</c> = only that it was submitted; <c>ScoreOnly</c> =
/// the score; <c>Answers</c> = the score plus own answers with points per question; <c>AnswersAndCorrect</c> = also the
/// correct options, but only once the student has no attempt left or the quiz has closed (so they cannot be passed on
/// to a later attempt). Scores with a pending essay are shown once the essay is graded.
/// </summary>
public static class ReviewPolicies
{
    public const string None = "None";
    public const string ScoreOnly = "ScoreOnly";
    public const string Answers = "Answers";
    public const string AnswersAndCorrect = "AnswersAndCorrect";

    public static IReadOnlyList<string> All { get; } = [None, ScoreOnly, Answers, AnswersAndCorrect];
}

/// <summary>
/// The state of an attempt. <c>Submitted</c> by the student; <c>AutoSubmitted</c> by the server at <c>EndsAtUtc</c> +
/// grace or at the quiz close (<c>QuizCloseJob</c>); <c>Graded</c> once every question has points (immediately when
/// there is no essay).
/// </summary>
public static class AttemptStatuses
{
    public const string InProgress = "InProgress";
    public const string Submitted = "Submitted";
    public const string AutoSubmitted = "AutoSubmitted";
    public const string Graded = "Graded";

    public static IReadOnlyList<string> All { get; } = [InProgress, Submitted, AutoSubmitted, Graded];
}
