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
    public const string Harassment = "Harassment";
    public const string Inappropriate = "Inappropriate";
    public const string Spam = "Spam";
    public const string Safety = "Safety";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } = [Harassment, Inappropriate, Spam, Safety, Other];
}

/// <summary>One list of report states.</summary>
public static class ReportStatuses
{
    public const string Open = "Open";
    public const string Dismissed = "Dismissed";
    public const string Actioned = "Actioned";

    public static IReadOnlyList<string> All { get; } = [Open, Dismissed, Actioned];
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

    /// <summary>The actions that need <c>RestrictUntilUtc</c>.</summary>
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

/// <summary>Where an entry of a course page's announcement list came from: the course board (M4a) or a school-level announcement that reaches the course (M6).</summary>
public static class AnnouncementSources
{
    public const string CourseBoard = "CourseBoard";
    public const string School = "School";

    public static IReadOnlyList<string> All { get; } = [CourseBoard, School];
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

/// <summary>The state of one outbound delivery (email, push) in the outbox (M6 wave 2).</summary>
public static class DeliveryStatuses
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";

    /// <summary>The address is on the suppression list (hard bounce, complaint or an opt-out): never sent, never retried.</summary>
    public const string Suppressed = "Suppressed";

    /// <summary>Held by quiet hours or a cap until <c>HeldUntilUtc</c>; the time to live of a held push starts when the hold ends.</summary>
    public const string Held = "Held";

    /// <summary>Not sent by rule; <see cref="DeliverySkipReasons"/> says which.</summary>
    public const string Skipped = "Skipped";

    public static IReadOnlyList<string> All { get; } = [Pending, Sending, Sent, Failed, Suppressed, Held, Skipped];
}

/// <summary>Why a delivery was skipped (stored as the row code, never a body or an address).</summary>
public static class DeliverySkipReasons
{
    /// <summary>A held row outlived its time to live while it waited.</summary>
    public const string ExpiredWhileHeld = "expired_while_held";

    /// <summary>The per-recipient daily cap held it past its time to live.</summary>
    public const string RecipientCap = "recipient_cap";

    /// <summary>The user has no contact point for the channel.</summary>
    public const string NoContact = "no_contact";

    /// <summary>The channel is off for the client or the user.</summary>
    public const string ChannelOff = "channel_off";

    public static IReadOnlyList<string> All { get; } = [ExpiredWhileHeld, RecipientCap, NoContact, ChannelOff];
}

/// <summary>Why an address or endpoint is on the suppression list.</summary>
public static class SuppressionReasons
{
    public const string HardBounce = "HardBounce";
    public const string Complaint = "Complaint";
    public const string UserOptOut = "UserOptOut";

    public static IReadOnlyList<string> All { get; } = [HardBounce, Complaint, UserOptOut];
}

/// <summary>Limits of the delivery channels that are fixed by the contract (the daily caps are settings).</summary>
public static class CommsLimits
{
    /// <summary>"Send me a test email" a user may press a day (429 <c>rate_limited</c> after it).</summary>
    public const int TestSendsPerDay = 3;

    /// <summary>The longest consent text version accepted.</summary>
    public const int ConsentTextVersionMaxLength = 40;
}

/// <summary>The state of one channel of a user as <c>GET /api/me/channels</c> shows it.</summary>
public static class ChannelStates
{
    public const string Active = "Active";
    public const string NeedsSetup = "NeedsSetup";
    public const string Blocked = "Blocked";
    public const string Bounced = "Bounced";

    /// <summary>The client switched the channel off, or the user withdrew consent.</summary>
    public const string Off = "Off";

    public static IReadOnlyList<string> All { get; } = [Active, NeedsSetup, Blocked, Bounced, Off];
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

    /// <summary>A report carries the reported message and this many messages around it.</summary>
    public const int ReportContextMessages = 10;

    public const int MaxAnnouncementAttachments = 5;
    public const int MaxAnnouncementTitleLength = 200;
    public const int MaxAnnouncementBodyLength = 20_000;
    public const int AudiencePreviewSampleMax = 10;
    public const int PushEndpointMaxLength = 1000;
    public const int EmailTestsPerDay = 3;
}
