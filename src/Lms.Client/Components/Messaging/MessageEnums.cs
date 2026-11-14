namespace Lms.Client.Components.Messaging;

/// <summary>The send state shown in the meta line of the user's own message (spec #615 §2.6, §2.8). Read receipts are off by default (D66).</summary>
public enum MessageStatus
{
    None,
    Sending,
    Sent,
    Seen,
    Failed,
}

/// <summary>Who removed a message: the two removals have different words (spec #615 §2.6, §2.7).</summary>
public enum MessageRemoval
{
    None,
    ByModerator,
    BySender,
}
