using Lms.Shared.Content;

namespace Lms.Shared.Quizzes;

/// <summary>
/// Quiz settings (FR-QZ-02). <c>TimeLimitMinutes</c> 1–600 or null (no limit); <c>MaxAttempts</c> 1–20 or null
/// (unlimited); <c>OpensAtUtc</c> &lt; <c>ClosesAtUtc</c> when both are set (400 <c>validation_failed</c>);
/// <c>DrawCount</c> = the question pool: each attempt draws that many questions at random, null = all (1..question
/// count, else 400 <c>pool_draw_invalid</c>). Grading method and review policy from <see cref="GradingMethods"/> and
/// <see cref="ReviewPolicies"/>.
/// </summary>
public sealed record QuizSettingsDto(
    int? TimeLimitMinutes,
    int? MaxAttempts,
    string GradingMethod,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    DateTime? OpensAtUtc,
    DateTime? ClosesAtUtc,
    string ReviewPolicy,
    int? DrawCount);

/// <summary>An option as the builder sees it (with <c>IsCorrect</c>). Never sent to a student before the review policy allows.</summary>
public sealed record QuizOptionDto(Guid Id, int Order, string Text, bool IsCorrect);

/// <summary>A question as the builder sees it. <c>Text</c> 1–4 000 plain text; <c>Points</c> 0.01–1000.</summary>
public sealed record QuizQuestionDto(
    Guid Id,
    int Order,
    string Type,
    string Text,
    decimal Points,
    IReadOnlyList<QuizOptionDto> Options);

/// <summary>
/// GET /api/quizzes/{id} — the builder (Quizzes.Manage on the ClassCourse). The lesson module (Kind <c>Quiz</c>,
/// <c>RefId</c> = <c>Id</c>) carries title, visibility, target and completion rule. <c>HasAttempts</c> = a student
/// has started an attempt: correct options, points, question set and pool are then locked (409
/// <c>quiz_has_attempts</c>; texts and the other settings stay editable). <c>TotalPoints</c> = the sum of all
/// questions; with a pool each attempt's <c>MaxScore</c> is the sum of its drawn questions, and the grading method
/// compares attempts by percentage.
/// </summary>
public sealed record QuizDto(
    Guid Id,
    Guid ClassCourseId,
    Guid LessonKey,
    ModuleNodeDto Module,
    string? DescriptionAr,
    string? DescriptionEn,
    QuizSettingsDto Settings,
    decimal TotalPoints,
    IReadOnlyList<QuizQuestionDto> Questions,
    int AttemptCount,
    bool HasAttempts,
    bool CanEdit,
    DateTime ServerNowUtc);

/// <summary>One row of GET /api/class-courses/{id}/quizzes (paged; staff all, students the visible ones with their state).</summary>
public sealed record QuizListItemDto(
    Guid Id,
    Guid ModuleId,
    Guid LessonKey,
    string TitleAr,
    string TitleEn,
    string Visibility,
    bool IsVisibleNow,
    DateTime? OpensAtUtc,
    DateTime? ClosesAtUtc,
    int QuestionCount,
    int? AttemptCount,
    int? MyAttemptsUsed,
    decimal? MyScore);

/// <summary>
/// POST /api/class-courses/{id}/quizzes — creates the quiz (no questions yet) and its module under <c>LessonKey</c> in
/// one transaction (Quizzes.Manage + Modules.Manage). Descriptions are plain text, max 2 000 each.
/// </summary>
public sealed record CreateQuizRequest(
    Guid LessonKey,
    ModuleSettingsInput Module,
    string? DescriptionAr,
    string? DescriptionEn,
    QuizSettingsDto Settings);

/// <summary>PUT /api/quizzes/{id}. Same rules as <see cref="CreateQuizRequest"/>; a smaller <c>DrawCount</c> after the first attempt is 409 <c>quiz_has_attempts</c>.</summary>
public sealed record UpdateQuizRequest(
    ModuleSettingsInput Module,
    string? DescriptionAr,
    string? DescriptionEn,
    QuizSettingsDto Settings);

/// <summary>An option of <see cref="QuizQuestionInput"/>. <c>Text</c> 1–1 000 plain text.</summary>
public sealed record QuizOptionInput(string Text, bool IsCorrect);

/// <summary>
/// POST /api/quizzes/{id}/questions (appended last) and PUT /api/quiz-questions/{id}. Option rules per
/// <see cref="QuestionTypes"/> (400 <c>validation_failed</c>). After the first attempt only texts may change (else 409
/// <c>quiz_has_attempts</c>); adding or deleting a question is refused too.
/// </summary>
public sealed record QuizQuestionInput(
    string Type,
    string Text,
    decimal Points,
    IReadOnlyList<QuizOptionInput> Options);

/// <summary>PUT /api/quizzes/{id}/questions/order — the quiz's live question ids in the new order (exactly the current set).</summary>
public sealed record ReorderQuizQuestionsRequest(IReadOnlyList<Guid> QuestionIds);
