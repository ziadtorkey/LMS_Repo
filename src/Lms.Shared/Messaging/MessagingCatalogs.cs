namespace Lms.Shared.Messaging;

// M6 contract, wave 1 (M6 backend proposal #613, revision 4): messaging, announcements, moderation, the rules matrix (D69) and delivery status.

public static class ConversationKinds
{
    public const string Direct = "Direct";
    public const string Group = "Group";

    public static IReadOnlyList<string> All { get; } = [Direct, Group];
}

/// <summary>What a message looks like to a reader. A deleted or removed message keeps its place (a tombstone keeps a report's context).</summary>
public static class MessageStates
{
    public const string Normal = "Normal";

    /// <summary>The sender deleted it for everyone within <see cref="MessagingLimits.DeleteWindowMinutes"/>; shown as "Message deleted".</summary>
    public const string Deleted = "Deleted";

    /// <summary>A moderator removed it; the original text stays visible to moderators only.</summary>
    public const string Removed = "Removed";

    public static IReadOnlyList<string> All { get; } = [Normal, Deleted, Removed];
}

/// <summary>How one cell of the rules matrix lets a role write to another.</summary>
public static class MessagingRuleModes
{
    /// <summary>Always allowed ("Anywhere" in the proposal).</summary>
    public const string Allow = "Allow";

    /// <summary>Allowed when the two share a relationship (a ClassCourse; a guardian of a student the sender teaches or counsels).</summary>
    public const string Relationship = "Relationship";

    /// <summary>Never allowed.</summary>
    public const string Deny = "Deny";

    /// <summary>Allowed to anyone in the same unit (looser than a relationship; no D69 default uses it, so a client can never choose it).</summary>
    public const string SameUnit = "SameUnit";

    public static IReadOnlyList<string> All { get; } = [Deny, Relationship, SameUnit, Allow];

    /// <summary>The order of #613 6.2: <c>Deny &lt; Relationship &lt; SameUnit &lt; Allow</c> (NotAllowed, Relationship, SameUnit, Anywhere); a higher rank is a looser rule.</summary>
    public static int Rank(string mode) => mode switch { Deny => 0, Relationship => 1, SameUnit => 2, Allow => 3, _ => -1 };
}

public static class ReportReasons
{
    /// <summary>Sorts first in the queue; its notice to the moderators is mandatory and urgent; the review target is <c>Moderation.SafetyReviewHours</c> (24).</summary>
    public const string SafetyConcern = "SafetyConcern";

    public const string Harassment = "Harassment";
    public const string Spam = "Spam";
    public const string Other = "Other";

    /// <summary>The fixed reasons (L16; #612's "Inappropriate" is dropped). The order is the sort order of the queue.</summary>
    public static IReadOnlyList<string> All { get; } = [SafetyConcern, Harassment, Spam, Other];
}

/// <summary>One list of report states.</summary>
public static class ReportStatuses
{
    public const string Open = "Open";

    /// <summary>A moderator opened it (the first read of the detail moves an Open report here; every read is audited).</summary>
    public const string InReview = "InReview";

    public const string Dismissed = "Dismissed";

    /// <summary>Remove, Warn, Restrict, RemoveAndRestrict or LockConversation was applied.</summary>
    public const string ActionTaken = "ActionTaken";

    public static IReadOnlyList<string> All { get; } = [Open, InReview, Dismissed, ActionTaken];

    /// <summary>The statuses still waiting for a decision.</summary>
    public static IReadOnlyList<string> Pending { get; } = [Open, InReview];
}

/// <summary>The restriction lengths a moderator can choose, in days (L16): 1, 7 or 30. A restriction is the moderator's tool; a block is the person's own and is never touched.</summary>
public static class RestrictionDurations
{
    public static IReadOnlyList<int> Days { get; } = [1, 7, 30];

    public static bool IsValid(int days) => Days.Contains(days);
}

/// <summary>What the person who reported a message can learn about it, on <c>MessageDto.MyReportStatus</c>: pending or reviewed, never the action (L16).</summary>
public static class MyReportStatuses
{
    public const string Pending = "Pending";
    public const string Reviewed = "Reviewed";

    public static IReadOnlyList<string> All { get; } = [Pending, Reviewed];
}

/// <summary>The outcome code of the <c>Messaging.ReportResolved</c> notice to the reporter: <c>Reviewed</c> only, no action and no note.</summary>
public static class ReportOutcomes
{
    public const string Reviewed = "Reviewed";

    public static IReadOnlyList<string> All { get; } = [Reviewed];
}

/// <summary>Limits of the moderation tools.</summary>
public static class ModerationLimits
{
    /// <summary>The most reports one bulk dismissal takes (the queue page size).</summary>
    public const int BulkDismissMax = 50;

    /// <summary>The longest note a moderator can add to a warning or a restriction notice to the sender.</summary>
    public const int NoteToPeopleMaxLength = 300;
}

/// <summary>The one moderation action set (#613 section 16). Moderators act on messages and on a user's messaging right, never on a user's blocks.</summary>
public static class ModerationActions
{
    public const string Dismiss = "Dismiss";
    public const string Remove = "Remove";
    public const string Warn = "Warn";
    public const string Restrict = "Restrict";
    public const string RemoveAndRestrict = "RemoveAndRestrict";
    public const string LockConversation = "LockConversation";

    public static IReadOnlyList<string> All { get; } = [Dismiss, Remove, Warn, Restrict, RemoveAndRestrict, LockConversation];

    /// <summary>The actions that need <c>DurationDays</c> (one of <see cref="RestrictionDurations"/>).</summary>
    public static IReadOnlyList<string> Restricting { get; } = [Restrict, RemoveAndRestrict];
}

public static class AnnouncementTargetKinds
{
    public const string Organization = "Organization";
    public const string Unit = "Unit";
    public const string Level = "Level";
    public const string Class = "Class";
    public const string ClassCourse = "ClassCourse";
    public const string Group = "Group";

    public static IReadOnlyList<string> All { get; } = [Organization, Unit, Level, Class, ClassCourse, Group];
}

public static class AnnouncementStatuses
{
    public const string Draft = "Draft";
    public const string Scheduled = "Scheduled";
    public const string Published = "Published";
    public const string Withdrawn = "Withdrawn";
    public const string Expired = "Expired";

    public static IReadOnlyList<string> All { get; } = [Draft, Scheduled, Published, Withdrawn, Expired];
}

/// <summary>The role chips of an announcement audience; none chosen = everyone the targets reach.</summary>
public static class AnnouncementRoleFilters
{
    public const string Staff = "Staff";
    public const string Students = "Students";
    public const string Guardians = "Guardians";

    public static IReadOnlyList<string> All { get; } = [Staff, Students, Guardians];
}

/// <summary>The state of one outbound delivery (email, push) in the outbox.</summary>
public static class DeliveryStatuses
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";

    /// <summary>Not sent by rule: opt-out, suppression, per-recipient cap, no contact.</summary>
    public const string Skipped = "Skipped";

    /// <summary>Held past its time to live (a held push starts its clock when the hold ends).</summary>
    public const string Expired = "Expired";

    public static IReadOnlyList<string> All { get; } = [Pending, Sending, Sent, Failed, Skipped, Expired];
}

/// <summary>The state of one channel of a user as <c>GET /api/me/channels</c> shows it.</summary>
public static class ChannelStates
{
    public const string Active = "Active";
    public const string NeedsSetup = "NeedsSetup";
    public const string Blocked = "Blocked";
    public const string Bounced = "Bounced";


    public static IReadOnlyList<string> All { get; } = [Active, NeedsSetup, Blocked, Bounced];
}

/// <summary>Where an effective notification preference comes from.</summary>
public static class PreferenceSources
{
    public const string Default = "Default";
    public const string User = "User";
    public const string Mandatory = "Mandatory";


    public static IReadOnlyList<string> All { get; } = [Default, User, Mandatory];
}

/// <summary>Limits of messaging and announcements (D66 defaults; the client settings narrow or raise within the settings catalog).</summary>
public static class MessagingLimits
{
    public const int MaxBodyLength = 4000;
    public const int MaxAttachmentsPerMessage = 5;
    public const int MaxAttachmentMb = 10;
    public const int MaxConversationTitleLength = 200;
    public const int DefaultMaxGroupSize = 50;
    public const int DeleteWindowMinutes = 15;
    public const int SearchMinLength = 2;
    public const int SearchMaxResults = 50;

    /// <summary>A search shows at most this many hits per query, however it is paged (it cannot enumerate message volume).</summary>
    public const int SearchWindow = 200;

    public const int MessagePageSize = 50;
    public const int MessagePageSizeMax = 100;
    public const int ReportNoteMaxLength = 500;

    /// <summary>A report carries the reported message and this many messages around it: <see cref="ReportContextEachSide"/> before and as many after.</summary>
    public const int ReportContextMessages = 10;

    public const int ReportContextEachSide = 5;

    public const int MaxAnnouncementAttachments = 5;
    public const int MaxAnnouncementTitleLength = 200;
    public const int MaxAnnouncementBodyLength = 20_000;
    public const int AudiencePreviewSampleMax = 10;
    public const int PushEndpointMaxLength = 1000;
    public const int EmailTestsPerDay = 3;
}
