using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Moderation;

/// <summary>
/// A report of one message (M6 Task 15, FR-MSG-04, D66, D69, D70, L16). The moderator reads only this: the reported message with a <b>frozen</b> context taken at report time
/// (five messages before and after: text and attachment names), so it survives the sender deleting the message and never reaches into the rest of the conversation. One report per
/// reporter and message (<see cref="ReportKey"/>). The reporter is never shown to the moderator or the sender. Which moderators see it is decided by its
/// <see cref="MessageReportUnit"/> rows (L16); the report itself belongs to the client.
/// </summary>
[Audited]
public sealed class MessageReport : AuditableEntity, ITenantOwned, ILiveKeyed
{
    public const string OpenStatus = "Open";
    public const string InReviewStatus = "InReview";
    public const string DismissedStatus = "Dismissed";
    public const string ActionTakenStatus = "ActionTaken";
    public const string SafetyConcernReason = "SafetyConcern";

    private MessageReport()
    {
        Reason = Status = ContextJson = string.Empty;
    }

    public MessageReport(
        Guid organizationId, Guid messageId, Guid conversationId, Guid reporterUserId, Guid reportedUserId, string reason, string? note, string contextJson,
        DateTime? reviewDueUtc)
    {
        OrganizationId = organizationId;
        MessageId = messageId;
        ConversationId = conversationId;
        ReporterUserId = reporterUserId;
        ReportedUserId = reportedUserId;
        Reason = reason;
        Note = note;
        ContextJson = contextJson;
        ReviewDueUtc = reviewDueUtc;
        Status = OpenStatus;
        ReportKey = KeyOf(reporterUserId, messageId);
    }

    public Guid OrganizationId { get; private set; }

    public Guid MessageId { get; private set; }

    public Guid ConversationId { get; private set; }

    /// <summary>Who reported it: never shown to a moderator or the sender, and kept out of the audit log.</summary>
    [AuditIgnore]
    public Guid ReporterUserId { get; private set; }

    /// <summary>The sender of the reported message.</summary>
    public Guid ReportedUserId { get; private set; }

    public string Reason { get; private set; }

    /// <summary>The reporter's own words (at most 500 characters): never in the audit log.</summary>
    [AuditIgnore]
    public string? Note { get; private set; }

    public string Status { get; private set; }

    /// <summary>The frozen context as JSON (names, text, attachment names): never in the audit log.</summary>
    [AuditIgnore]
    public string ContextJson { get; private set; }

    /// <summary>The review target of a safety concern (<c>Moderation.SafetyReviewHours</c>); null for the other reasons.</summary>
    public DateTime? ReviewDueUtc { get; private set; }

    /// <summary>The action taken (a <c>ModerationActions</c> value); null while pending.</summary>
    public string? Action { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>"reporter:message" while live: one report per reporter and message, whatever the number of requests at once.</summary>
    public string? ReportKey { get; private set; }

    public bool IsPending => Status is OpenStatus or InReviewStatus;

    public static string KeyOf(Guid reporter, Guid message) => $"{reporter:N}:{message:N}";

    /// <summary>A moderator opened it: an Open report becomes InReview.</summary>
    public void MarkInReview()
    {
        if (Status == OpenStatus)
        {
            Status = InReviewStatus;
        }
    }

    public void Dismiss(Guid moderatorUserId, DateTime nowUtc)
    {
        Close(DismissedStatus, "Dismiss", moderatorUserId, nowUtc);
    }

    public void TakeAction(string action, Guid moderatorUserId, DateTime nowUtc)
    {
        Close(ActionTakenStatus, action, moderatorUserId, nowUtc);
    }

    public void ClearLiveKeys() => ReportKey = null;

    private void Close(string status, string action, Guid moderatorUserId, DateTime nowUtc)
    {
        if (!IsPending)
        {
            throw new InvalidOperationException("The report is already resolved.");
        }

        Status = status;
        Action = action;
        ResolvedByUserId = moderatorUserId;
        ResolvedAtUtc = nowUtc;
    }
}

/// <summary>
/// One unit at which a moderator may see a report (L16): the conversation's context unit (a class or course group), else every active unit of the sender, the client root as the
/// fallback so a report is never invisible. A report is in the queue of an Org Admin assigned at or above <b>any</b> of its units, through the ordinary unit-subtree filter.
/// </summary>
public sealed class MessageReportUnit : UnitOwnedEntity
{
    private MessageReportUnit()
    {
    }

    public MessageReportUnit(Guid reportId, Guid organizationId, Guid orgUnitId, string orgUnitPath)
    {
        ReportId = reportId;
        PlaceIn(organizationId, orgUnitId, orgUnitPath);
    }

    public Guid ReportId { get; private set; }
}
