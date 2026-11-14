namespace Lms.Shared.Excuses;

// M5 contract, wave 3: absence excuses (FR-EXC-01..04). The student's home unit owns the excuse (the counselor queue is a unit-subtree query).

/// <summary>
/// GET <c>/api/students/{id}/excuse-policy</c> (<c>Excuses.Submit</c>): what the submit dialog needs. <c>CanSubmit</c> is the server's answer from <c>Excuses.Submitters</c> and the
/// caller's relationship (L21); the client never reads the setting. Days are school days counted with <c>IClientCalendar</c>.
/// </summary>
public sealed record ExcusePolicyDto(
    Guid StudentId,
    string Submitters,
    bool CanSubmit,
    int MaxDaysAfterAbsence,
    IReadOnlyList<string> AllowedReasons,
    IReadOnlyList<string> AttachmentRequiredReasons,
    int MaxAttachmentMb,
    IReadOnlyList<string> AttachmentContentTypes,
    DateTime ServerNowUtc);

/// <summary>Query of <c>GET /api/students/{id}/excusable-sessions</c> (local dates; both required).</summary>
public sealed record ExcusableSessionsQuery(DateOnly From, DateOnly To);

/// <summary>An Absent or Late record the student may still be excused for (not already covered by a pending or approved excuse, inside the policy).</summary>
public sealed record ExcusableSessionDto(
    Guid SessionId,
    DateOnly Date,
    string CourseNameAr,
    string CourseNameEn,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string Status);

/// <summary>The answer of <c>POST /api/excuses/attachments</c> (multipart, one file): a stored private file to pass as <c>AttachmentFileId</c>. 400 <c>excuse_attachment_invalid</c> for a type outside the allow-list, a size over <c>Excuses.MaxAttachmentMb</c> or a content sniff mismatch.</summary>
public sealed record ExcuseAttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// Body of <c>POST /api/excuses</c>. Cover the absences either by <c>SessionIds</c> or by a local date range (<c>DateFrom</c>/<c>DateTo</c>); the sessions are computed at submission and frozen.
/// Only <c>Absent</c> or <c>Late</c> records count; later absences need a new excuse.
/// </summary>
public sealed record SubmitExcuseRequest(
    Guid StudentId,
    string Reason,
    string? ReasonText,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    IReadOnlyList<Guid>? SessionIds,
    Guid? AttachmentFileId);

/// <summary>A session an excuse covers, with the record's current status (it becomes <c>Excused</c> when the excuse is approved).</summary>
public sealed record ExcuseSessionDto(Guid SessionId, DateOnly Date, string CourseNameAr, string CourseNameEn, string RecordStatus);

/// <summary>One step of an excuse's history (submitted, escalated, decided, withdrawn).</summary>
public sealed record ExcuseHistoryDto(string Status, Guid ByUserId, string ByNameAr, string ByNameEn, string? Comment, DateTime AtUtc);

/// <summary>
/// An excuse as the submitter, the student's guardians and reviewers see it. <c>HasAttachment</c> only (false once the retention job purged the file of a rejected or withdrawn excuse; the excuse stays readable): the file is fetched through <c>GET /api/excuses/{id}/attachment-url</c>.
/// <c>CanWithdraw</c> is true for the submitter while <c>Submitted</c>.
/// </summary>
public sealed record ExcuseDto(
    Guid Id,
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string StudentNumber,
    Guid SubmittedByUserId,
    string SubmittedByNameAr,
    string SubmittedByNameEn,
    string Reason,
    string? ReasonText,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string Status,
    IReadOnlyList<ExcuseSessionDto> Sessions,
    bool HasAttachment,
    string? AttachmentFileName,
    string? DecisionComment,
    Guid? ReviewerUserId,
    string? ReviewerNameAr,
    string? ReviewerNameEn,
    Guid? EscalatedToUserId,
    string? EscalatedToNameAr,
    string? EscalatedToNameEn,
    DateTime SubmittedAtUtc,
    DateTime? DecidedAtUtc,
    bool CanWithdraw,
    IReadOnlyList<ExcuseHistoryDto> History,
    DateTime ServerNowUtc);

/// <summary>Query of <c>GET /api/me/excuses</c>: excuses the caller submitted, or (a student) that concern the caller. <c>StudentId</c> narrows to one child (an active guardian link, a miss is 404).</summary>
public sealed record MyExcusesQuery(Guid? StudentId = null, string? Status = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

/// <summary>Query of <c>GET /api/excuses/review</c>. <c>Status</c> null = <c>Submitted</c> and <c>Escalated</c> (the pending ones); <c>UnitId</c> narrows within the caller's subtree.</summary>
public sealed record ExcuseReviewQuery(
    string? Status = null,
    Guid? UnitId = null,
    Guid? StudentId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>One recent absence of the student, for the review drawer's context (<see cref="ExcuseLimits.RecentAbsencesShown"/> at most).</summary>
public sealed record RecentAbsenceDto(DateOnly Date, string CourseNameAr, string CourseNameEn, string Status);

/// <summary>A row of the counselor queue: the excuse, the student's recent absences, and how long it has been waiting.</summary>
public sealed record ExcuseReviewItemDto(ExcuseDto Excuse, IReadOnlyList<RecentAbsenceDto> RecentAbsences, int AgeDays);

/// <summary>Body of <c>POST /api/excuses/{id}/approve</c>; the comment is optional.</summary>
public sealed record ApproveExcuseRequest(string? Comment);

/// <summary>Body of <c>POST /api/excuses/{id}/reject</c>; a comment of 1-500 characters is required (400 <c>excuse_comment_required</c>).</summary>
public sealed record RejectExcuseRequest(string Comment);

/// <summary>Body of <c>POST /api/excuses/{id}/escalate</c>; <c>TargetUserId</c> comes from <c>GET /api/excuses/{id}/escalation-targets</c>.</summary>
public sealed record EscalateExcuseRequest(Guid TargetUserId, string? Comment);

/// <summary>A staff user an excuse can be escalated to: <c>Excuses.Review</c> or <c>Excuses.Escalate</c> whose scope covers the student's unit; the Manager is the default pick (L14).</summary>
public sealed record EscalationTargetDto(Guid UserId, string NameAr, string NameEn, string? RoleNameAr, string? RoleNameEn, bool IsDefault);

/// <summary>
/// The answer of <c>GET /api/excuses/{id}/attachment-url</c>: a signed URL valid <see cref="ExcuseLimits.AttachmentUrlMinutes"/> minutes, served with
/// <c>Content-Disposition: attachment</c> and <c>nosniff</c>. <c>InlinePreview</c> is true only for the raster types (L10).
/// </summary>
public sealed record ExcuseAttachmentUrlDto(string Url, DateTime ExpiresAtUtc, string FileName, string ContentType, bool InlinePreview);
