using Lms.Shared.Content;

namespace Lms.Shared.Assignments;

/// <summary>
/// An assignment of a ClassCourse (FR-ASG-01). Its lesson module (Kind <c>Assignment</c>, <c>RefId</c> = <c>Id</c>)
/// carries title, visibility, target and completion rule (<see cref="Module"/>). Instructions are HTML sanitised on
/// write (D48). <c>PenaltyPercent</c> is set only for <c>Penalty</c>. Students receive it only while the module is
/// visible to them (else 404). <c>SubmittedCount</c> / <c>GradedCount</c> are filled for staff, null for students.
/// </summary>
public sealed record CourseAssignmentDto(
    Guid Id,
    Guid ClassCourseId,
    Guid LessonKey,
    ModuleNodeDto Module,
    string? InstructionsHtmlAr,
    string? InstructionsHtmlEn,
    IReadOnlyList<AssetDto> Attachments,
    DateTime? DueAtUtc,
    decimal MaxGrade,
    string LatePolicy,
    int? PenaltyPercent,
    string ResubmissionRule,
    bool AllowText,
    bool AllowFiles,
    int? SubmittedCount,
    int? GradedCount,
    bool CanEdit,
    DateTime ServerNowUtc);

/// <summary>
/// One row of GET /api/class-courses/{id}/assignments (paged, ordered by due date then title). Staff see every
/// assignment with counts; a student sees the visible ones with <c>MyStatus</c> (null = not submitted).
/// </summary>
public sealed record CourseAssignmentListItemDto(
    Guid Id,
    Guid ModuleId,
    Guid LessonKey,
    string TitleAr,
    string TitleEn,
    string Visibility,
    bool IsVisibleNow,
    DateTime? DueAtUtc,
    decimal MaxGrade,
    int? SubmittedCount,
    int? GradedCount,
    string? MyStatus,
    bool? MyIsLate);

/// <summary>
/// POST /api/class-courses/{id}/assignments — creates the assignment and its module under <c>LessonKey</c> in one
/// transaction (CourseAssignments.Manage + Modules.Manage on the ClassCourse). Instructions max 200 000 characters
/// each; attachments are assets of this ClassCourse (max 10); <c>MaxGrade</c> 0.01–1000; at least one of
/// <c>AllowText</c> / <c>AllowFiles</c>. <c>Penalty</c> needs <c>PenaltyPercent</c> 1–100 (400
/// <c>late_policy_invalid</c>), the other policies none. <c>Block</c> needs <c>DueAtUtc</c>.
/// </summary>
public sealed record CreateCourseAssignmentRequest(
    Guid LessonKey,
    ModuleSettingsInput Module,
    string? InstructionsHtmlAr,
    string? InstructionsHtmlEn,
    IReadOnlyList<Guid> AttachmentAssetIds,
    DateTime? DueAtUtc,
    decimal MaxGrade,
    string LatePolicy,
    int? PenaltyPercent,
    string ResubmissionRule,
    bool AllowText,
    bool AllowFiles);

/// <summary>PUT /api/course-assignments/{id}. Same rules as <see cref="CreateCourseAssignmentRequest"/>; the lesson never changes here (move the module).</summary>
public sealed record UpdateCourseAssignmentRequest(
    ModuleSettingsInput Module,
    string? InstructionsHtmlAr,
    string? InstructionsHtmlEn,
    IReadOnlyList<Guid> AttachmentAssetIds,
    DateTime? DueAtUtc,
    decimal MaxGrade,
    string LatePolicy,
    int? PenaltyPercent,
    string ResubmissionRule,
    bool AllowText,
    bool AllowFiles);

/// <summary>
/// The grade of a submission. <c>FinalGrade</c> = <c>Grade</c> minus <c>PenaltyPercentApplied</c> % for a late
/// submission under <c>Penalty</c>. Feedback HTML is sanitised on write. A student receives it only once returned.
/// </summary>
public sealed record SubmissionGradeDto(
    decimal Grade,
    int? PenaltyPercentApplied,
    decimal FinalGrade,
    string? FeedbackHtml,
    AssetDto? AnnotatedFile,
    DateTime GradedAtUtc,
    DateTime? ReturnedAtUtc);

/// <summary>
/// A student's submission (GET /api/submissions/{id}: the owning student or staff of the ClassCourse). Text HTML is
/// sanitised on write. <c>Number</c> counts hand-ins (1 = first, 2 = first resubmission …).
/// </summary>
public sealed record SubmissionDto(
    Guid Id,
    Guid AssignmentId,
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string Status,
    string? TextHtml,
    IReadOnlyList<AssetDto> Files,
    DateTime? SubmittedAtUtc,
    bool IsLate,
    int Number,
    SubmissionGradeDto? Grade);

/// <summary>
/// One row of GET /api/course-assignments/{id}/submissions (staff, paged): every targeted, enrolled student, also
/// those without a hand-in (<c>SubmissionId</c> and <c>Status</c> null). Drafts are not shown.
/// </summary>
public sealed record SubmissionListItemDto(
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    Guid? SubmissionId,
    string? Status,
    DateTime? SubmittedAtUtc,
    bool IsLate,
    decimal? FinalGrade);

/// <summary>
/// Query of GET /api/course-assignments/{id}/submissions. <c>Status</c> from <see cref="SubmissionStatuses.HandedIn"/>,
/// or <c>"None"</c> for students without a hand-in.
/// </summary>
public sealed record SubmissionQuery(
    string? Status = null,
    bool? IsLate = null,
    string? Search = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// GET /api/course-assignments/{id}/my-submission — the student's view: the assignment, their live submission (null
/// before the first save) and whether they may submit now (<c>CannotSubmitCode</c> = the <see cref="ProblemCodes"/>
/// value a submit would get, e.g. <c>submission_closed</c>).
/// </summary>
public sealed record MySubmissionDto(
    CourseAssignmentDto Assignment,
    SubmissionDto? Submission,
    bool CanSubmit,
    string? CannotSubmitCode,
    DateTime ServerNowUtc);

/// <summary>
/// POST /api/course-assignments/{id}/submit (CourseAssignments.Submit, own enrollment). Text HTML (max 200 000) and/or
/// files (assets of this ClassCourse uploaded by the caller, max 10) as the assignment allows; at least one. One live
/// submission per student (a resubmission replaces its content and increments <c>Number</c>). <c>SaveAsDraft</c> keeps
/// it private and does not count as a hand-in (no late flag, no completion signal).
/// </summary>
public sealed record SubmitAssignmentRequest(
    string? TextHtml,
    IReadOnlyList<Guid> FileAssetIds,
    bool SaveAsDraft = false);

/// <summary>
/// PUT /api/submissions/{id}/grade (CourseAssignments.Grade). <c>Grade</c> 0..<c>MaxGrade</c> (else 400
/// <c>validation_failed</c>); feedback HTML max 200 000, sanitised; the annotated file is an asset of this ClassCourse.
/// Saving sets <c>Graded</c>; POST /api/submissions/{id}/return releases grade and feedback to the student
/// (<c>Returned</c>, <c>Assignments.Graded</c> notification) and writes the grade event — the next revision each time a
/// changed grade is returned again, so the M5 gradebook never sees an unreleased grade.
/// </summary>
public sealed record GradeSubmissionRequest(
    decimal Grade,
    string? FeedbackHtml,
    Guid? AnnotatedFileAssetId);
