namespace Lms.Domain.Messaging;

/// <summary>The reason codes a policy refusal carries (the <c>reason</c> of 403 <c>messaging_not_allowed</c> and of <c>conversation_read_only</c>, L18, L25).</summary>
public static class MessagingReasons
{
    /// <summary>Neutral: a block and a rule look the same to the person who is refused, so a block is never revealed.</summary>
    public const string BlockedOrRule = "blocked_or_rule";

    /// <summary>The creating rule no longer holds (an Org Admin tightening, a relationship that ended).</summary>
    public const string Rule = "rule";

    public const string Restricted = "restricted";

    /// <summary>D74: students never post in a group, including a class group; they reply privately to the teacher.</summary>
    public const string GroupStudentsReadOnly = "group_students_read_only";

    /// <summary>D77: a guardian posts only in a teacher-run group; in any other group they read.</summary>
    public const string GroupGuardiansReadOnly = "group_guardians_read_only";
}

/// <summary>The answer of the policy: allowed, or refused with a reason the API turns into a problem (the reason never names who blocked whom).</summary>
public sealed record MessagingDecision(bool Allowed, string? Reason = null)
{
    public static MessagingDecision Allow { get; } = new(true);

    public static MessagingDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// The safeguarding rules of messaging (M6 Task 11, D69, L4, L25). It answers "may A message B" <b>only when a conversation is created</b> (<see cref="CanStartAsync"/>) and
/// when a member is added to a group (<see cref="CanAddToGroupAsync"/>, owner to member, never pairwise); afterwards every post asks <see cref="CanPostAsync"/>: the sender is
/// not restricted, no block between the pair (direct), the group allows this kind of sender (D74, D77) and the <b>creating rule still holds</b> (from the initiator, or from the
/// group owner to the sender), so a guardian can reply to a manager who wrote first although they could not start the conversation. Every rule is <c>min(stored, default)</c>,
/// never looser than the D69 defaults; a user with several roles is judged pair by pair and the best pair wins. Everything stays inside the caller's client.
/// </summary>
public interface IMessagingPolicy
{
    /// <summary>A direct conversation: the initiator may write to the other (rules, relationship, no block either way). A refusal is always <see cref="MessagingReasons.BlockedOrRule"/>.</summary>
    Task<MessagingDecision> CanStartAsync(Guid initiatorUserId, Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>A group: the owner may add the member (owner to member rule; a block does not matter in groups).</summary>
    Task<MessagingDecision> CanAddToGroupAsync(Guid ownerUserId, Guid memberUserId, CancellationToken cancellationToken = default);

    /// <summary>May this person post in the conversation now (L25). The reason of a refusal is one of <see cref="MessagingReasons"/>.</summary>
    Task<MessagingDecision> CanPostAsync(Guid senderUserId, Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>The creating rule holds from <paramref name="fromUserId"/> to <paramref name="toUserId"/> (no block check): the rule part of every post.</summary>
    Task<MessagingDecision> CreatingRuleHoldsAsync(Guid fromUserId, Guid toUserId, CancellationToken cancellationToken = default);

    /// <summary>True when either user blocked the other (direct conversations only; groups are not affected).</summary>
    Task<bool> IsBlockedBetweenAsync(Guid userA, Guid userB, CancellationToken cancellationToken = default);

    /// <summary>True when the user is under an active restriction (cannot send).</summary>
    Task<bool> IsRestrictedAsync(Guid userId, CancellationToken cancellationToken = default);
}
