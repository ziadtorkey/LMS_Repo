namespace Lms.Shared.YearEnd;

// M7 wave 1: close year, grade-change approval, promotion run, withdrawal/graduation/archive/re-admission, open next year (FR-YEAR-04/05, FR-GRD-06,
// FR-STU-07/08; PRD D12, D78-D84; the FE proposal #808 section 4). Ids and plain data only: a student's name is shown to staff who may read the student,
// a reason is staff-only (D81), no DTO carries a grade value except a change request, which only staff who may see the grade read.

/// <summary>A problem or a warning found while previewing a year-end action: a stable code, how many things it affects and, when it points at one thing, its id (a class course, a class or a student).</summary>
public sealed record YearEndIssueDto(string Code, int Count, Guid? RelatedId);

/// <summary>Stable codes of <see cref="YearEndIssueDto"/>; the client maps each to plain words (<c>YearIssue_&lt;code&gt;</c>).</summary>
public static class YearEndIssueCodes
{
    // Blocking (the action is refused while any exist).
    public const string NoLevelMapping = "NoLevelMapping";
    public const string TargetClassMissing = "TargetClassMissing";
    public const string AlreadyPlacedInTargetYear = "AlreadyPlacedInTargetYear";
    public const string TargetYearClosed = "TargetYearClosed";
    public const string SourceYearNotClosed = "SourceYearNotClosed";

    // Warnings (D79, D84): shown, never blocking.
    public const string UnpublishedGradeItems = "UnpublishedGradeItems";
    public const string OpenAssignments = "OpenAssignments";
    public const string OpenTerms = "OpenTerms";
    public const string PendingGradeChangeRequests = "PendingGradeChangeRequests";
    public const string PendingExcuseRequests = "PendingExcuseRequests";
    public const string OverCapacity = "OverCapacity";
}

// --- close year (FR-YEAR-04, D79)

/// <summary>GET <c>…/close-preview</c>: what closing locks and what the caller should know first. Unpublished grades only warn (D79).</summary>
public sealed record YearClosePreviewDto(
    Guid YearId,
    string NameAr,
    string NameEn,
    int GradebooksToLock,
    int AttendanceSessionsToLock,
    IReadOnlyList<YearEndIssueDto> Blocking,
    IReadOnlyList<YearEndIssueDto> Warnings);

/// <summary>POST <c>…/close</c>: the caller types the year's name (<c>NameEn</c> or <c>NameAr</c>) to confirm; there is no reopen in v1 (D79).</summary>
public sealed record CloseYearRequest(string ConfirmName);

/// <summary>The answer to a close: the year is Closed and its gradebooks and attendance are read-only (409 <c>gradebook_locked</c>, <c>year_closed</c> from then on).</summary>
public sealed record YearClosedDto(Guid YearId, DateTime ClosedAtUtc, int GradebooksLocked, int AttendanceSessionsLocked, DateTime ServerNowUtc, bool LockSnapshotsPending = false);

/// <summary>One class of the closing report (GET <c>…/closing-report</c>, read only).</summary>
public sealed record ClosingReportRowDto(Guid ClassId, string ClassNameAr, string ClassNameEn, int Students, int Gradebooks, int GradebooksPublished, int AttendanceSessions);

public sealed record ClosingReportDto(Guid YearId, DateTime ClosedAtUtc, IReadOnlyList<ClosingReportRowDto> Classes, bool LockSnapshotsPending = false);

// --- grade change after close (FR-GRD-06, D80)

public static class GradeChangeStates
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static IReadOnlyList<string> All { get; } = [Pending, Approved, Rejected];
}

public static class GradeChangeLimits
{
    public const int ReasonMinLength = 20;
    public const int ReasonMaxLength = 500;
    public const int NoteMaxLength = 500;
}

/// <summary>POST <c>…/gradebook/change-requests</c> (<c>Grades.ChangeRequest</c>; Rel): one open request per grade (409 <c>grade_change_pending</c>); the reason is 20 to 500 characters (422 <c>validation_failed</c> on <c>Reason</c>).</summary>
public sealed record CreateGradeChangeRequest(Guid GradeItemId, Guid StudentId, decimal? NewValue, string Reason, long Version);

/// <summary>A request, for the requester and for approvers. <c>OldValue</c> and <c>NewValue</c> are points; null = no score.</summary>
public sealed record GradeChangeRequestDto(
    Guid Id,
    Guid ClassCourseId,
    string CourseNameAr,
    string CourseNameEn,
    Guid GradeItemId,
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    decimal? OldValue,
    decimal? NewValue,
    string Reason,
    string State,
    Guid RequestedByUserId,
    string RequestedByNameAr,
    string RequestedByNameEn,
    DateTime RequestedAtUtc,
    Guid? DecidedByUserId,
    DateTime? DecidedAtUtc,
    string? DecisionNote,
    DateTime ServerNowUtc);

/// <summary>GET <c>/api/grade-change-requests</c> (<c>Grades.ChangeApprove</c>: the approvers' queue; <c>Grades.ChangeRequest</c>: only the caller's own requests).</summary>
public sealed record GradeChangeQuery(string? State = null, Guid? YearId = null, Guid? ClassCourseId = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

/// <summary>POST <c>…/approve</c> or <c>…/reject</c> (<c>Grades.ChangeApprove</c>; never the requester, 403 <c>grade_change_not_allowed</c>); a rejection needs a note (D80).</summary>
public sealed record DecideGradeChangeRequest(string? Note);

// --- promotion (FR-STU-07, D12, D78, D83, D84)

public static class PromotionOutcomes
{
    public const string Promote = "Promote";
    public const string Repeat = "Repeat";
    public const string Graduate = "Graduate";
    public const string TransferOut = "TransferOut";

    public static IReadOnlyList<string> All { get; } = [Promote, Repeat, Graduate, TransferOut];
}

public static class PromotionRunStates
{
    public const string Draft = "Draft";
    public const string Running = "Running";
    public const string Done = "Done";
    public const string PartiallyFailed = "PartiallyFailed";
    public const string Undone = "Undone";
    public const string Failed = "Failed";

    public static IReadOnlyList<string> All { get; } = [Draft, Running, Done, PartiallyFailed, Undone, Failed];
}

public static class PromotionLimits
{
    /// <summary>The most rows a <see cref="SavePromotionDecisionsRequest"/> carries; a bigger change uses the filter route.</summary>
    public const int DecisionsPerCall = 200;

    /// <summary>The undo window when the client has not set another (FR-STU-07, D12).</summary>
    public const int DefaultUndoWindowMinutes = 10;
}

/// <summary>POST <c>/api/promotion/runs</c> (<c>Promotion.Run</c>; D78): a draft. The source year must be Closed (D79, 409 <c>year_not_closed</c>) and the target year Planned or Active; one run per source unit at a time (409 <c>promotion_in_progress</c>).</summary>
public sealed record CreatePromotionRunRequest(Guid SourceYearId, Guid SourceOrgUnitId, Guid TargetYearId, bool IncludeSubUnits);

public sealed record PromotionCountsDto(int Promote, int Repeat, int Graduate, int TransferOut, int WithoutTargetClass);

/// <summary>A run. <c>Processed</c> and <c>Total</c> drive the progress while <c>Running</c>; <c>UndoUntilUtc</c> is set while an undo may still be possible (before the window ends and before any activity in the new year, D12) and the client counts down from <c>ServerNowUtc</c>.</summary>
public sealed record PromotionRunDto(
    Guid Id,
    Guid SourceYearId,
    Guid SourceOrgUnitId,
    Guid TargetYearId,
    bool IncludeSubUnits,
    string State,
    int Total,
    int Processed,
    int Failed,
    PromotionCountsDto Counts,
    IReadOnlyList<YearEndIssueDto> Blocking,
    IReadOnlyList<YearEndIssueDto> Warnings,
    DateTime? UndoUntilUtc,
    DateTime CreatedAtUtc,
    DateTime? ExecutedAtUtc,
    Guid CreatedByUserId,
    DateTime ServerNowUtc);

/// <summary>One student of the preview (GET <c>…/rows</c>, server-paged). <c>SuggestedOutcome</c> comes from the level mapping only (D83); every row stays editable. <c>FailureCode</c> is set after an execution that failed for this student (D84).</summary>
public sealed record PromotionRowDto(
    Guid StudentId,
    string StudentCode,
    string NameAr,
    string NameEn,
    Guid SourceClassId,
    string SourceClassNameAr,
    string SourceClassNameEn,
    string Outcome,
    string SuggestedOutcome,
    Guid? TargetClassId,
    string? TargetClassNameAr,
    string? TargetClassNameEn,
    string? ProblemCode,
    IReadOnlyList<string> WarningCodes,
    string? FailureCode);

/// <summary>GET <c>…/rows</c> and the filter of <see cref="SetPromotionOutcomeForFilterRequest"/>.</summary>
public sealed record PromotionRowsQuery(string? Outcome = null, Guid? ClassId = null, bool ProblemsOnly = false, string? Search = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

public sealed record PromotionDecisionDto(Guid StudentId, string Outcome, Guid? TargetClassId);

/// <summary>PUT <c>…/decisions</c>: at most <see cref="PromotionLimits.DecisionsPerCall"/> rows (400 otherwise); kept on the server as a draft.</summary>
public sealed record SavePromotionDecisionsRequest(IReadOnlyList<PromotionDecisionDto> Decisions);

/// <summary>PUT <c>…/decisions/filter</c>: a filter, never a list of ids, so a run of 50,000 students needs no 50,000 ids (X-09). <c>Outcome</c> null resets the filtered rows to the suggestion.</summary>
public sealed record SetPromotionOutcomeForFilterRequest(PromotionRowsQuery Filter, string? Outcome, Guid? TargetClassId);

/// <summary>POST <c>…/execute</c>: the caller types the number of students (409 <c>validation_failed</c> on <c>ConfirmStudentCount</c> when it differs). The run is a job, per student and idempotent (D84).</summary>
public sealed record ExecutePromotionRequest(int ConfirmStudentCount);

/// <summary>GET <c>…/failures</c> (paged): the students that failed, with a stable code, to fix and re-run (D84).</summary>
public sealed record PromotionFailureDto(Guid StudentId, string StudentCode, string NameAr, string NameEn, string FailureCode);

// --- withdrawal, graduation, archive, re-admission (FR-STU-08, FR-SEC-07, D81, D82)

public static class StudentExitKinds
{
    public const string Withdrawn = "Withdrawn";
    public const string Graduated = "Graduated";
    public const string TransferredOut = "TransferredOut";

    public static IReadOnlyList<string> All { get; } = [Withdrawn, Graduated, TransferredOut];
}

public static class LifecycleLimits
{
    public const int NoteMaxLength = 500;
}

/// <summary>One reason of the client-managed list (D82). <c>Key</c> is stable; the list always offers "Other" (<see cref="WithdrawalReasons.Other"/>) with a note.</summary>
public sealed record WithdrawalReasonDto(string Key, string NameAr, string NameEn, bool IsActive);

public static class WithdrawalReasons
{
    public const string Other = "Other";
}

/// <summary>PUT <c>/api/withdrawal-reasons</c>: the whole list of the client (<c>Settings.Update</c>-class right, client root).</summary>
public sealed record SaveWithdrawalReasonsRequest(IReadOnlyList<WithdrawalReasonDto> Reasons);

public static class WithdrawalReasonLimits
{
    public const int MaxReasons = 30;
    public const int KeyMaxLength = 50;
    public const int NameMaxLength = 100;
}

/// <summary>POST <c>…/withdraw</c> (<c>Students.Withdraw</c>): the student becomes inactive on <c>EffectiveDate</c> (today or earlier acts at once; a later date acts on that date), cannot sign in, leaves active lists; history stays (FR-STU-08). The reason is staff-only (D81); <c>Other</c> needs a note.</summary>
public sealed record WithdrawStudentRequest(string ReasonKey, string? Note, DateOnly EffectiveDate);

/// <summary>POST <c>…/graduate</c> (<c>Students.Withdraw</c>): the same effect with the exit kind Graduated and no reason.</summary>
public sealed record GraduateStudentRequest(DateOnly EffectiveDate);

/// <summary>POST <c>…/readmit</c> (<c>Students.Readmit</c>): back into a class of an Active or Planned year with room; the student code is kept (D25).</summary>
public sealed record ReadmitStudentRequest(Guid ClassId, DateOnly EffectiveDate, string? Reason);

/// <summary>One row of the archive (read only, staff). The reason is shown to staff only (D81).</summary>
public sealed record ArchivedStudentDto(
    Guid StudentId,
    string StudentCode,
    string NameAr,
    string NameEn,
    string ExitKind,
    string? ReasonKey,
    DateOnly EffectiveDate,
    Guid? LastClassId,
    string? LastClassNameAr,
    string? LastClassNameEn,
    string? LastYearNameAr,
    string? LastYearNameEn,
    bool CanReadmit);

/// <summary>GET <c>/api/students/archive</c> (<c>Students.ViewArchive</c>; unit scope): server-paged, never exposes a national id.</summary>
public sealed record ArchiveQuery(string? Search = null, string? ExitKind = null, Guid? YearId = null, Guid? OrgUnitId = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

// --- year-end checklist (D86; M7 wave 2)

/// <summary>The steps of a year end, in order. The client maps each to plain words (<c>YearStep_&lt;step&gt;</c>).</summary>
public static class YearEndChecklistSteps
{
    public const string PublishGrades = "PublishGrades";
    public const string ApproveGradeChanges = "ApproveGradeChanges";
    public const string CloseYear = "CloseYear";
    public const string RunPromotion = "RunPromotion";
    public const string OpenNextYear = "OpenNextYear";
    public static IReadOnlyList<string> All { get; } = [PublishGrades, ApproveGradeChanges, CloseYear, RunPromotion, OpenNextYear];
}

/// <summary><c>Done</c> nothing left; <c>Todo</c> work remains; <c>Warning</c> work remains and the date is near or past; <c>NotApplicable</c> the caller cannot act on it or it does not apply.</summary>
public static class YearEndChecklistStates
{
    public const string Todo = "Todo";
    public const string Warning = "Warning";
    public const string Done = "Done";
    public const string NotApplicable = "NotApplicable";
    public static IReadOnlyList<string> All { get; } = [Todo, Warning, Done, NotApplicable];
}

/// <summary><c>Count</c> is what remains: unpublished items, pending requests, students not yet promoted; 0 when Done or for a step with no count.</summary>
public sealed record YearEndChecklistStepDto(string Step, string State, int Count);

/// <summary>One year the caller manages. <c>OpenPromotionRunId</c> links to a Draft or Running run of the caller's scope.</summary>
public sealed record YearEndChecklistYearDto(
    Guid YearId,
    string NameAr,
    string NameEn,
    DateOnly EndDate,
    int DaysToEnd,
    string YearStatus,
    IReadOnlyList<YearEndChecklistStepDto> Steps,
    Guid? OpenPromotionRunId);

/// <summary>GET <c>/api/me/year-end-checklist</c>. <c>ShowFromDaysBeforeEnd</c> echoes <c>Years.EndChecklistDays</c> (default 60, D86); the client shows nothing when <c>Years</c> is empty.</summary>
public sealed record YearEndChecklistDto(IReadOnlyList<YearEndChecklistYearDto> Years, int ShowFromDaysBeforeEnd, DateTime ServerNowUtc);

// --- open the next year (FR-YEAR-05, FR-ORG-07/08, D22)

/// <summary>What a copy brings: class structure only; students are never copied.</summary>
public static class YearCopyParts
{
    public const string Classes = "Classes";
    public const string ClassCourses = "ClassCourses";
    public const string Blueprints = "Blueprints";
    public const string TimetablePeriods = "TimetablePeriods";

    public static IReadOnlyList<string> All { get; } = [Classes, ClassCourses, Blueprints, TimetablePeriods];
}

/// <summary>
/// POST <c>…/open-next/preview</c> and <c>…/open-next</c> (<c>AcademicYears.Create</c>): a Planned year from <c>CopyFromYearId</c> and/or a template (both null = 400
/// <c>copy_nothing_selected</c>). Terms are copied with shifted dates. <c>OrgUnitIds</c> empty = the whole defining unit.
/// </summary>
public sealed record OpenNextYearRequest(
    string NameAr,
    string NameEn,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? CopyFromYearId,
    IReadOnlyList<string> CopyParts,
    IReadOnlyList<Guid> OrgUnitIds,
    Guid? TemplateId,
    Guid? TemplateParentId);

public sealed record OpenNextYearPreviewDto(
    int Classes,
    int ClassCourses,
    int Periods,
    int UnitsFromTemplate,
    IReadOnlyList<SkippedItemDto> Skipped,
    IReadOnlyList<YearEndIssueDto> Blocking);

/// <summary>Something the copy leaves out, with a stable reason (a locked setting, a closed unit), never a name of a person.</summary>
public sealed record SkippedItemDto(string Kind, string Reason, int Count);

/// <summary>The copy as a job: poll <c>GET …/open-next/{runId}</c> until <c>State</c> is Done or Failed.</summary>
public sealed record NewYearRunDto(Guid Id, Guid? NewYearId, string State, int Total, int Processed, OpenNextYearPreviewDto Result, DateTime ServerNowUtc);

public static class NewYearRunStates
{
    public const string Running = "Running";
    public const string Done = "Done";
    public const string Failed = "Failed";
}
