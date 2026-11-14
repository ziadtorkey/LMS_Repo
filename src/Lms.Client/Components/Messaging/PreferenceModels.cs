namespace Lms.Client.Components.Messaging;

/// <summary>The four delivery channels of the preferences page (spec #615 §6), in column order.</summary>
public enum NotificationChannel
{
    InApp,
    Email,
    Sms,
    Push,
}

/// <summary>A channel column: its caption is the address in use (masked) or the push state of this device; an unavailable channel disables its
/// switches and says why (no mobile number, or a browser without push).</summary>
public sealed record ChannelColumn(NotificationChannel Channel, string Caption, bool Available = true, string? UnavailableText = null, string? UnavailableHref = null);

/// <summary>One event row: its label and, per channel, whether it is on and whether the school locked it (a mandatory event).</summary>
public sealed class PreferenceRow
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public Dictionary<NotificationChannel, bool> On { get; init; } = [];
    public HashSet<NotificationChannel> Locked { get; init; } = [];
}

/// <summary>This browser's push state (spec #615 §6.3).</summary>
public enum PushState
{
    On,
    Off,
    Denied,
    Unsupported,
}

/// <summary>#613 rev 3 <c>ModerationActions</c>, in the order the decision lists them: the ones that act on the sender or the conversation come last.</summary>
public enum ModerationAction
{
    Dismiss,
    Remove,
    Warn,
    Restrict,
    RemoveAndRestrict,
    LockConversation,
}

/// <summary>A report's status in the moderation queue (spec #615 §7.3).</summary>
public enum ReportStatus
{
    New,
    InReview,
    Removed,
    Kept,
}
