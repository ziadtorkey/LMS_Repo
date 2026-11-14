namespace Lms.Shared;

// M7 wave 1: close year, grade-change approval, promotion, withdrawal / graduation / archive / re-admission, open next year (FR-YEAR-04/05, FR-GRD-06,
// FR-STU-07/08; D12, D78-D84). A relationship miss is 404, a missing permission 403. The Org Admin closes a year and opens the next (D17: years are the Org Admin's);
// the Org Admin anywhere and a Manager inside their own units run a promotion (D78).
public static partial class Routes
{
    // --- close year (AcademicYears.Close, Org Admin; UnitAccess on the year's defining unit)

    /// <summary>GET (<c>AcademicYears.Close</c>) → <c>YearClosePreviewDto</c>: what a close would lock; unpublished grades warn but do not block (D79).</summary>
    public const string AcademicYearClosePreviewPattern = AcademicYearPattern + "/close-preview";

    /// <summary>POST (<c>CloseYearRequest</c> → <c>YearClosedDto</c>) — <c>AcademicYears.Close</c>; audited; Active years only (409 <c>year_close_blocked</c> with the blocking list when a blocker exists); no reopen in v1 (D79).</summary>
    public const string AcademicYearClosePattern = AcademicYearPattern + "/close";

    /// <summary>GET (<c>AcademicYears.View</c>) → <c>ClosingReportDto</c> of a Closed year; read only.</summary>
    public const string AcademicYearClosingReportPattern = AcademicYearPattern + "/closing-report";

    // --- open the next year (AcademicYears.Create)

    /// <summary>POST (<c>OpenNextYearRequest</c> → <c>OpenNextYearPreviewDto</c>) — <c>AcademicYears.Create</c>: counts and skipped items, nothing is written.</summary>
    public const string AcademicYearOpenNextPreviewPattern = AcademicYearPattern + "/open-next/preview";

    /// <summary>POST (<c>OpenNextYearRequest</c> → <c>NewYearRunDto</c>, 202) — <c>AcademicYears.Create</c>: a Planned year created by a job; 400 <c>copy_nothing_selected</c>, 409 <c>year_overlap</c>.</summary>
    public const string AcademicYearOpenNextPattern = AcademicYearPattern + "/open-next";

    /// <summary>GET (<c>AcademicYears.Create</c>) → <c>NewYearRunDto</c>: the job's progress and result.</summary>
    public const string AcademicYearOpenNextRunPattern = AcademicYearOpenNextPattern + "/{runId:guid}";

    // --- grade change after close (FR-GRD-06, D80)

    /// <summary>POST (<c>CreateGradeChangeRequest</c> → <c>GradeChangeRequestDto</c>) — <c>Grades.ChangeRequest</c>; Rel (teacher: own ClassCourse); only in a locked gradebook (otherwise the normal entry route applies); 409 <c>grade_change_pending</c>; audited.</summary>
    public const string ClassCourseGradeChangeRequestsPattern = ClassCoursePattern + "/gradebook/change-requests";

    /// <summary>GET <c>GradeChangeQuery</c> → <c>PagedResult&lt;GradeChangeRequestDto&gt;</c> — <c>Grades.ChangeApprove</c> (unit subtree) or <c>Grades.ChangeRequest</c> (the caller's own); server-paged.</summary>
    public const string GradeChangeRequests = "/api/grade-change-requests";

    public const string GradeChangeRequestPattern = GradeChangeRequests + "/{id:guid}";

    /// <summary>POST (<c>DecideGradeChangeRequest</c> → <c>GradeChangeRequestDto</c>) — <c>Grades.ChangeApprove</c>; never the requester (403 <c>grade_change_not_allowed</c>); applies the value and records an audited grade event; 409 <c>concurrency_conflict</c> when the grade moved meanwhile.</summary>
    public const string GradeChangeApprovePattern = GradeChangeRequestPattern + "/approve";

    /// <summary>POST (<c>DecideGradeChangeRequest</c> with a note) — <c>Grades.ChangeApprove</c>; never the requester.</summary>
    public const string GradeChangeRejectPattern = GradeChangeRequestPattern + "/reject";

    // --- promotion (Promotion.Run: Org Admin and Manager inside their units, D78)

    public const string PromotionRuns = "/api/promotion/runs";
    public const string PromotionRunPattern = PromotionRuns + "/{id:guid}";

    // GET <PromotionRuns> → PagedResult<PromotionRunDto> (history, server-paged) and POST (CreatePromotionRunRequest → PromotionRunDto): Promotion.Run; UnitAccess on the source unit; 409 year_not_closed, promotion_in_progress.

    /// <summary>GET <c>PromotionRowsQuery</c> → <c>PagedResult&lt;PromotionRowDto&gt;</c> — <c>Promotion.Run</c>; the preview, server-paged and filtered (X-09).</summary>
    public const string PromotionRunRowsPattern = PromotionRunPattern + "/rows";

    /// <summary>PUT (<c>SavePromotionDecisionsRequest</c> → 204) — <c>Promotion.Run</c>, a Draft run only; at most <c>PromotionLimits.DecisionsPerCall</c> rows; 409 <c>promotion_conflict</c> when the run is no longer a draft.</summary>
    public const string PromotionRunDecisionsPattern = PromotionRunPattern + "/decisions";

    /// <summary>PUT (<c>SetPromotionOutcomeForFilterRequest</c> → <c>PromotionCountsDto</c>) — <c>Promotion.Run</c>: a bulk change by filter, never by ids.</summary>
    public const string PromotionRunDecisionsFilterPattern = PromotionRunDecisionsPattern + "/filter";

    /// <summary>POST (<c>ExecutePromotionRequest</c> → <c>PromotionRunDto</c>, 202) — <c>Promotion.Run</c>: runs as a job, per student and idempotent (D84); blocking issues are 409 <c>promotion_conflict</c>; audited.</summary>
    public const string PromotionRunExecutePattern = PromotionRunPattern + "/execute";

    /// <summary>POST (no body → <c>PromotionRunDto</c>, 202) — <c>Promotion.Run</c>: restores the students it moved; only inside the window and before any activity in the new year (409 <c>promotion_undo_window_over</c> or <c>promotion_undo_activity_exists</c>, D12).</summary>
    public const string PromotionRunUndoPattern = PromotionRunPattern + "/undo";

    /// <summary>GET → <c>PagedResult&lt;PromotionFailureDto&gt;</c> — <c>Promotion.Run</c>: the students that failed, to fix and re-run (D84).</summary>
    public const string PromotionRunFailuresPattern = PromotionRunPattern + "/failures";

    // --- withdrawal, graduation, archive, re-admission (FR-STU-08, D81, D82)

    /// <summary>POST (<c>WithdrawStudentRequest</c> → 204) — <c>Students.Withdraw</c> (Org Admin, Manager, Counselor; UnitAccess); 409 <c>student_already_inactive</c>, 422 <c>effective_date_invalid</c>; audited; blocks sign-in from the effective date (FR-SEC-07).</summary>
    public const string StudentWithdrawPattern = StudentPattern + "/withdraw";

    /// <summary>POST (<c>GraduateStudentRequest</c> → 204) — <c>Students.Withdraw</c>; the same effect with the exit kind Graduated.</summary>
    public const string StudentGraduatePattern = StudentPattern + "/graduate";

    /// <summary>POST (<c>ReadmitStudentRequest</c> → 204) — <c>Students.Readmit</c> (Org Admin, Manager); 409 <c>readmit_class_full</c>, <c>readmit_year_closed</c>; the student code is kept (D25).</summary>
    public const string StudentReadmitPattern = StudentPattern + "/readmit";

    /// <summary>GET <c>ArchiveQuery</c> → <c>PagedResult&lt;ArchivedStudentDto&gt;</c> — <c>Students.ViewArchive</c>; unit scope, server-paged, read only.</summary>
    public const string StudentsArchive = Students + "/archive";

    /// <summary>GET (<c>Students.Withdraw</c>) → <c>IReadOnlyList&lt;WithdrawalReasonDto&gt;</c> and PUT (<c>SaveWithdrawalReasonsRequest</c>) — the client-root admin (D18, D82).</summary>
    public const string WithdrawalReasons = "/api/withdrawal-reasons";

    // --- helpers
    public static string AcademicYearClosePreview(Guid yearId) => $"{AcademicYear(yearId)}/close-preview";
    public static string AcademicYearClose(Guid yearId) => $"{AcademicYear(yearId)}/close";
    public static string AcademicYearClosingReport(Guid yearId) => $"{AcademicYear(yearId)}/closing-report";
    public static string AcademicYearOpenNextPreview(Guid yearId) => $"{AcademicYear(yearId)}/open-next/preview";
    public static string AcademicYearOpenNext(Guid yearId) => $"{AcademicYear(yearId)}/open-next";
    public static string AcademicYearOpenNextRun(Guid yearId, Guid runId) => $"{AcademicYear(yearId)}/open-next/{runId}";
    public static string ClassCourseGradeChangeRequests(Guid classCourseId) => $"{ClassCourses}/{classCourseId}/gradebook/change-requests";
    public static string GradeChangeRequest(Guid id) => $"{GradeChangeRequests}/{id}";
    public static string GradeChangeApprove(Guid id) => $"{GradeChangeRequest(id)}/approve";
    public static string GradeChangeReject(Guid id) => $"{GradeChangeRequest(id)}/reject";
    public static string PromotionRun(Guid id) => $"{PromotionRuns}/{id}";
    public static string PromotionRunRows(Guid id) => $"{PromotionRun(id)}/rows";
    public static string PromotionRunDecisions(Guid id) => $"{PromotionRun(id)}/decisions";
    public static string PromotionRunDecisionsFilter(Guid id) => $"{PromotionRun(id)}/decisions/filter";
    public static string PromotionRunExecute(Guid id) => $"{PromotionRun(id)}/execute";
    public static string PromotionRunUndo(Guid id) => $"{PromotionRun(id)}/undo";
    public static string PromotionRunFailures(Guid id) => $"{PromotionRun(id)}/failures";
    public static string StudentWithdraw(Guid id) => $"{Students}/{id}/withdraw";
    public static string StudentGraduate(Guid id) => $"{Students}/{id}/graduate";
    public static string StudentReadmit(Guid id) => $"{Students}/{id}/readmit";
}
