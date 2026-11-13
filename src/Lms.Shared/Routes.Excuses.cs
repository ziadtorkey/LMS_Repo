namespace Lms.Shared;

// M5 wave 3: absence excuses (M5 plan Task 3, FR-EXC-01..04). A relationship miss is 404, a missing permission 403. Submit: a linked guardian, or the student under
// <c>Excuses.Submitters = GuardiansAndStudents</c> (a student under <c>GuardiansOnly</c> is 403 <c>excuse_submitter_not_allowed</c>); review: Counselor and Manager by unit subtree; the
// reviewer is never the submitter (409 <c>excuse_reviewer_is_submitter</c>).
public static partial class Routes
{
    /// <summary>GET → <c>ExcusePolicyDto</c> for the student (Excuses.Submit; guardian link or self; another guardian 404).</summary>
    public const string StudentExcusePolicyPattern = StudentPattern + "/excuse-policy";

    /// <summary>GET <c>?from=&amp;to=</c> (<c>ExcusableSessionsQuery</c> → <c>ExcusableSessionDto[]</c>) — Excuses.Submit; guardian link or self + the submitters setting; another guardian 404.</summary>
    public const string StudentExcusableSessionsPattern = StudentPattern + "/excusable-sessions";

    /// <summary>
    /// POST (<c>SubmitExcuseRequest</c> → <c>ExcuseDto</c>) — Excuses.Submit; guardian link or self; a student under GuardiansOnly 403 <c>excuse_submitter_not_allowed</c>, another guardian 404;
    /// outside the policy 409 <c>excuse_out_of_policy</c> with the violation as <c>reason</c>; PlacementGuard.
    /// </summary>
    public const string ExcusesPattern = "/api/excuses";

    /// <summary>POST (multipart, one file → <c>ExcuseAttachmentDto</c>) — Excuses.Submit; allow-list jpeg, png, webp, pdf, <c>Excuses.MaxAttachmentMb</c>, content sniffed, stored privately; 400 <c>excuse_attachment_invalid</c>.</summary>
    public const string ExcuseAttachmentsPattern = ExcusesPattern + "/attachments";

    /// <summary>GET (<c>MyExcusesQuery</c> → <c>PagedResult&lt;ExcuseDto&gt;</c>) — Excuses.Submit; the caller's own submissions (a student also the ones for them); a child that is not the caller's is 404.</summary>
    public const string MeExcuses = "/api/me/excuses";

    /// <summary>POST (no body → <c>ExcuseDto</c>) — Excuses.Submit; the submitter only (others 404), while <c>Submitted</c> (otherwise 409 <c>excuse_not_pending</c>).</summary>
    public const string ExcuseWithdrawPattern = ExcusesPattern + "/{id:guid}/withdraw";

    /// <summary>GET → <c>ExcuseAttachmentUrlDto</c> — Excuses.Submit or Excuses.Review; the submitter, the student's linked guardians, or staff in the student's unit subtree; a signed URL valid 5 minutes; others 404.</summary>
    public const string ExcuseAttachmentUrlPattern = ExcusesPattern + "/{id:guid}/attachment-url";

    /// <summary>GET (<c>ExcuseReviewQuery</c> → <c>PagedResult&lt;ExcuseReviewItemDto&gt;</c>) — Excuses.Review; unit subtree; teacher 403, another school's counselor sees nothing of it (404 for a foreign unit filter).</summary>
    public const string ExcuseReviewPattern = ExcusesPattern + "/review";

    /// <summary>POST (<c>ApproveExcuseRequest</c> → <c>ExcuseDto</c>) — Excuses.Review; subtree; one transaction: each covered record becomes <c>Excused</c> with an <c>AttendanceRecordChange</c> and the submitter is notified; 409 <c>excuse_not_pending</c>, 409 <c>excuse_reviewer_is_submitter</c>.</summary>
    public const string ExcuseApprovePattern = ExcusesPattern + "/{id:guid}/approve";

    /// <summary>POST (<c>RejectExcuseRequest</c> → <c>ExcuseDto</c>) — Excuses.Review; subtree; the records stay Absent; 400 <c>excuse_comment_required</c>, 409 <c>excuse_not_pending</c>, 409 <c>excuse_reviewer_is_submitter</c>.</summary>
    public const string ExcuseRejectPattern = ExcusesPattern + "/{id:guid}/reject";

    /// <summary>POST (<c>EscalateExcuseRequest</c> → <c>ExcuseDto</c>) — Excuses.Escalate; subtree; the target must hold Excuses.Review or Excuses.Escalate in scope; 409 <c>excuse_not_pending</c>.</summary>
    public const string ExcuseEscalatePattern = ExcusesPattern + "/{id:guid}/escalate";

    /// <summary>GET → <c>EscalationTargetDto[]</c> — Excuses.Escalate or Excuses.Review; subtree; the submitter is excluded.</summary>
    public const string ExcuseEscalationTargetsPattern = ExcusesPattern + "/{id:guid}/escalation-targets";

    public static string StudentExcusePolicy(Guid studentId) => $"{Student(studentId)}/excuse-policy";
    public static string StudentExcusableSessions(Guid studentId, DateOnly from, DateOnly to) => $"{Student(studentId)}/excusable-sessions?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
    public static string ExcuseWithdraw(Guid excuseId) => $"{ExcusesPattern}/{excuseId}/withdraw";
    public static string ExcuseAttachmentUrl(Guid excuseId) => $"{ExcusesPattern}/{excuseId}/attachment-url";
    public static string ExcuseApprove(Guid excuseId) => $"{ExcusesPattern}/{excuseId}/approve";
    public static string ExcuseReject(Guid excuseId) => $"{ExcusesPattern}/{excuseId}/reject";
    public static string ExcuseEscalate(Guid excuseId) => $"{ExcusesPattern}/{excuseId}/escalate";
    public static string ExcuseEscalationTargets(Guid excuseId) => $"{ExcusesPattern}/{excuseId}/escalation-targets";
}
