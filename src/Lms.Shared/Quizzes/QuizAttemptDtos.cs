namespace Lms.Shared.Quizzes;

/// <summary>An attempt in a list (the student's own in <see cref="QuizSummaryDto"/>; staff: GET /api/quizzes/{id}/attempts, paged).</summary>
public sealed record AttemptSummaryDto(
    Guid Id,
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    int Number,
    string Status,
    DateTime StartedAtUtc,
    DateTime? SubmittedAtUtc,
    decimal? Score,
    decimal MaxScore,
    bool HasPendingEssays);

/// <summary>
/// GET /api/quizzes/{id}/summary — the student's start page (visible quiz only, else 404). <c>AttemptsLeft</c> null =
/// unlimited; <c>InProgressAttemptId</c> = resume instead of starting; <c>FinalScore</c> = the score by grading method
/// (null until one attempt is graded, or when the review policy is <c>None</c>). <c>Score</c> of each attempt follows
/// the review policy the same way.
/// </summary>
public sealed record QuizSummaryDto(
    Guid QuizId,
    Guid ModuleId,
    string TitleAr,
    string TitleEn,
    string? DescriptionAr,
    string? DescriptionEn,
    int QuestionCount,
    int? TimeLimitMinutes,
    int? MaxAttempts,
    int AttemptsUsed,
    int? AttemptsLeft,
    DateTime? OpensAtUtc,
    DateTime? ClosesAtUtc,
    string GradingMethod,
    string ReviewPolicy,
    bool IsOpenNow,
    Guid? InProgressAttemptId,
    IReadOnlyList<AttemptSummaryDto> MyAttempts,
    decimal? FinalScore,
    DateTime ServerNowUtc);

/// <summary>An option as a student taking the attempt sees it: never <c>IsCorrect</c>.</summary>
public sealed record AttemptOptionDto(Guid Id, string Text);

/// <summary>A drawn question of an attempt, in the attempt's (shuffled) order, without the correct options.</summary>
public sealed record AttemptQuestionDto(
    Guid QuestionId,
    int Order,
    string Type,
    string Text,
    decimal Points,
    IReadOnlyList<AttemptOptionDto> Options);

/// <summary>The saved answer of a question: selected options (choice types) or <c>Text</c> (essay).</summary>
public sealed record AttemptAnswerDto(
    Guid QuestionId,
    IReadOnlyList<Guid> SelectedOptionIds,
    string? Text,
    long Sequence);

/// <summary>
/// POST /api/quizzes/{id}/attempts (start; Quizzes.Attempt, 409 <c>quiz_not_open</c> / <c>quiz_closed</c> /
/// <c>attempt_limit_reached</c>; an attempt already in progress is returned instead of a new one) and
/// GET /api/quiz-attempts/{id} (resume; own attempt only). The question set and order are a snapshot taken at start.
/// <c>EndsAtUtc</c> = min(start + time limit, close), null when neither applies; <c>RemainingSeconds</c> is computed
/// against <c>ServerNowUtc</c> — the client timer is display only.
/// </summary>
public sealed record AttemptDto(
    Guid Id,
    Guid QuizId,
    int Number,
    string Status,
    DateTime StartedAtUtc,
    DateTime? EndsAtUtc,
    int? RemainingSeconds,
    DateTime ServerNowUtc,
    IReadOnlyList<AttemptQuestionDto> Questions,
    IReadOnlyList<AttemptAnswerDto> Answers);

/// <summary>
/// GET /api/quizzes/{id}/preview (Quizzes.Manage) — the quiz as a student would see one attempt (drawn and shuffled
/// like a real start, without correct options), with no attempt created.
/// </summary>
public sealed record QuizPreviewDto(
    Guid QuizId,
    int? TimeLimitMinutes,
    IReadOnlyList<AttemptQuestionDto> Questions,
    DateTime ServerNowUtc);

/// <summary>
/// PUT /api/quiz-attempts/{id}/answers — autosave of one answer. <c>Sequence</c> is a client counter per attempt: a
/// save with a sequence not above the stored one is ignored (last write wins by sequence, not by arrival). Essay text
/// is plain, max 20 000. After <c>EndsAtUtc</c> + <c>Quizzes.AutoSubmitGraceSeconds</c>, or once submitted: 409
/// <c>attempt_expired</c>.
/// </summary>
public sealed record SaveAnswerRequest(
    Guid QuestionId,
    IReadOnlyList<Guid> SelectedOptionIds,
    string? Text,
    long Sequence);

/// <summary>Reply to an autosave: the sequence now stored for that question and the server's remaining time.</summary>
public sealed record SaveAnswerResultDto(
    Guid QuestionId,
    long StoredSequence,
    int? RemainingSeconds,
    DateTime ServerNowUtc);

/// <summary>An option in a reviewed attempt. <c>IsCorrect</c> is null unless the review policy shows correct options now.</summary>
public sealed record AttemptResultOptionDto(Guid Id, string Text, bool Selected, bool? IsCorrect);

/// <summary>A question in a reviewed attempt. <c>AwardedPoints</c> null = essay not graded yet.</summary>
public sealed record AttemptResultQuestionDto(
    Guid QuestionId,
    int Order,
    string Type,
    string Text,
    decimal Points,
    decimal? AwardedPoints,
    IReadOnlyList<AttemptResultOptionDto> Options,
    string? EssayText,
    string? Feedback);

/// <summary>
/// POST /api/quiz-attempts/{id}/submit (idempotent) and GET /api/quiz-attempts/{id}/result. For a student the content
/// follows <see cref="ReviewPolicies"/>: <c>Score</c> null for <c>None</c> or while an essay is pending;
/// <c>Questions</c> null for <c>None</c> and <c>ScoreOnly</c>. Staff with Quizzes.View on the ClassCourse always get
/// everything (with <c>IsCorrect</c>).
/// </summary>
public sealed record AttemptResultDto(
    Guid AttemptId,
    Guid QuizId,
    int Number,
    string Status,
    string ReviewPolicy,
    decimal? Score,
    decimal MaxScore,
    bool HasPendingEssays,
    IReadOnlyList<AttemptResultQuestionDto>? Questions,
    DateTime ServerNowUtc);

/// <summary>Points (0..question points) and optional plain-text feedback (max 2 000) for one essay answer.</summary>
public sealed record EssayGradeInput(Guid QuestionId, decimal Points, string? Feedback);

/// <summary>
/// PUT /api/quiz-attempts/{id}/grade (Quizzes.Grade). Only essay questions of a submitted attempt. When the last
/// essay is graded the attempt becomes <c>Graded</c>, the quiz score is recomputed by grading method and a grade event
/// is written (next revision on a regrade).
/// </summary>
public sealed record GradeAttemptRequest(IReadOnlyList<EssayGradeInput> Grades);
