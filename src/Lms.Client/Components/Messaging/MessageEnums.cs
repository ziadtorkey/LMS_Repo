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

/// <summary>A messaging-rules cell (#613 <c>MessagingRule.Via</c>); GroupsOnly is the D69 floor for managers and org admins to students.</summary>
public enum MessagingVia
{
    Anywhere,
    SameUnit,
    Relationship,
    NotAllowed,
    GroupsOnly,
}
