namespace Lms.Shared.Excuses;

// M5 contract, wave 3 (M5 plan Task 3): absence excuses. D61, L10, L13, L14, L21.

/// <summary>
/// The state machine of an excuse: <c>Submitted</c> → <c>Approved</c> | <c>Rejected</c> | <c>Escalated</c> (→ <c>Approved</c> | <c>Rejected</c>), or <c>Withdrawn</c> by the
/// submitter while <c>Submitted</c>. Any other transition is 409 <c>excuse_not_pending</c>.
/// </summary>
public static class ExcuseStatuses
{
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Escalated = "Escalated";
    public const string Withdrawn = "Withdrawn";

    public static IReadOnlyList<string> All { get; } = [Submitted, Approved, Rejected, Escalated, Withdrawn];

    /// <summary>The statuses a reviewer can still decide.</summary>
    public static IReadOnlyList<string> Pending { get; } = [Submitted, Escalated];
}

/// <summary>The fixed reason catalog (L13). <c>Excuses.AllowedReasons</c> picks a subset per client.</summary>
public static class ExcuseReasons
{
    public const string Illness = "Illness";
    public const string Family = "Family";
    public const string Official = "Official";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } = [Illness, Family, Official, Other];
}

/// <summary>Who may submit an excuse for a student (setting <c>Excuses.Submitters</c>, D61). The read answers <c>CanSubmit</c> so the client never reads the setting (L21).</summary>
public static class ExcuseSubmitters
{
    /// <summary>The default: a linked guardian only.</summary>
    public const string GuardiansOnly = "GuardiansOnly";

    public const string GuardiansAndStudents = "GuardiansAndStudents";

    public static IReadOnlyList<string> All { get; } = [GuardiansOnly, GuardiansAndStudents];
}

/// <summary>Why an excuse is outside the policy; the <c>reason</c> extension of the 409 <c>excuse_out_of_policy</c> problem.</summary>
public static class ExcusePolicyViolations
{
    /// <summary>The absence is older than <c>Excuses.MaxDaysAfterAbsence</c> school days (counted with <c>IClientCalendar</c>).</summary>
    public const string TooLate = "TooLate";

    /// <summary>The reason is not in <c>Excuses.AllowedReasons</c>.</summary>
    public const string ReasonNotAllowed = "ReasonNotAllowed";

    /// <summary>The reason is in <c>Excuses.AttachmentRequiredReasons</c> and there is no attachment.</summary>
    public const string AttachmentRequired = "AttachmentRequired";

    /// <summary>None of the chosen sessions or dates is an <c>Absent</c> or <c>Late</c> record of the student.</summary>
    public const string NothingToExcuse = "NothingToExcuse";

    /// <summary>The dates are in the future or the range is reversed.</summary>
    public const string DatesInvalid = "DatesInvalid";

    public static IReadOnlyList<string> All { get; } = [TooLate, ReasonNotAllowed, AttachmentRequired, NothingToExcuse, DatesInvalid];
}

/// <summary>Limits of the excuse contract (L10). Attachments: allow-list, content sniffed server side, stored privately.</summary>
public static class ExcuseLimits
{
    public const int ReasonTextMaxLength = 1000;

    /// <summary>Length of a decision or escalation comment (a rejection needs one: 400 <c>excuse_comment_required</c>).</summary>
    public const int CommentMinLength = 1;

    public const int CommentMaxLength = 500;

    public const int MaxDaysAfterAbsenceMin = 1;
    public const int MaxDaysAfterAbsenceMax = 60;
    public const int MaxDaysAfterAbsenceDefault = 5;

    public const int MaxAttachmentMbMin = 1;
    public const int MaxAttachmentMbMax = 25;
    public const int MaxAttachmentMbDefault = 5;

    public const int AttachmentRetentionDaysMin = 30;
    public const int AttachmentRetentionDaysMax = 3650;
    public const int AttachmentRetentionDaysDefault = 365;

    /// <summary>The signed attachment URL is valid this long; it is served with <c>Content-Disposition: attachment</c> and <c>nosniff</c> (L10).</summary>
    public const int AttachmentUrlMinutes = 5;

    /// <summary>An uploaded attachment that is not attached to an excuse within this many hours is purged by a job (L10).</summary>
    public const int UnattachedUploadPurgeHours = 24;

    /// <summary>Per-user rate limit of <c>POST /api/excuses/attachments</c>.</summary>
    public const int UploadsPerUserPerHour = 20;

    /// <summary>The attachment allow-list. Only the raster types preview inline in the review drawer; a PDF downloads only.</summary>
    public static IReadOnlyList<string> AttachmentContentTypes { get; } = ["image/jpeg", "image/png", "image/webp", "application/pdf"];

    public static IReadOnlyList<string> InlinePreviewContentTypes { get; } = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>The context rows a review item shows: the student's most recent absences.</summary>
    public const int RecentAbsencesShown = 10;
}
