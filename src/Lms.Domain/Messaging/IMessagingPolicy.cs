namespace Lms.Domain.Messaging;

/// <summary>The reason codes a policy refusal carries (the <c>reason</c> of 403 <c>messaging_not_allowed</c> and of <c>conversation_read_only</c>, L18, L25).</summary>
public static class MessagingReasons
{
    /// <summary>Neutral: a block and a rule look the same to the person who is refused, so a block is never revealed.</summary>
    public const string BlockedOrRule = "blocked_or_rule";

    /// <summary>The creating rule no longer holds (an Org Admin tightening, a relationship that ended).</summary>
    public const string Rule = "rule";

    public const string Restricted = "restricted";
}

/// <summary>The answer of a policy question: allowed, or refused with a <see cref="MessagingReasons"/> value.</summary>
public sealed record MessagingDecision(bool Allowed, string? Reason)
{
    public static MessagingDecision Allow { get; } = new(true, null);

    public static MessagingDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Who may write to whom (M6 Task 11, D69, L4, L25). It answers "may A message B" <b>only when a conversation is created</b> (<see cref="CanStartAsync"/>) and when a
/// member is added to a group (<see cref="CanAddToGroupAsync"/>, owner to member, never pairwise); afterwards a post checks <see cref="CreatingRuleHoldsAsync"/> for the
/// initiator (or the group owner) towards the sender, plus blocks and restrictions, so a guardian can reply to a manager who wrote first although they could not start the
/// conversation. Every rule is <c>min(stored, default)</c>, never looser than the D69 defaults. The user with several roles is judged pair by pair, the best pair wins, never
/// a mix of two pairs. Everything stays inside one client.
/// </summary>
public interface IMessagingPolicy
{
    /// <summary>A direct conversation: the initiator may write to the other (rules, relationship, no block either way). A refusal is always <see cref="MessagingReasons.BlockedOrRule"/>.</summary>
    Task<MessagingDecision> CanStartAsync(Guid organizationId, Guid initiatorUserId, Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>A group: the owner may add the member (owner to member rule, no block between them).</summary>
    Task<MessagingDecision> CanAddToGroupAsync(Guid organizationId, Guid ownerUserId, Guid memberUserId, CancellationToken cancellationToken = default);

    /// <summary>The creating rule still holds from <paramref name="fromUserId"/> to <paramref name="toUserId"/> (no block check: see <see cref="IsBlockedBetweenAsync"/>): the rule check of every post.</summary>
    Task<MessagingDecision> CreatingRuleHoldsAsync(Guid organizationId, Guid fromUserId, Guid toUserId, CancellationToken cancellationToken = default);

    /// <summary>True when either user blocked the other (direct conversations only; groups are not affected).</summary>
    Task<bool> IsBlockedBetweenAsync(Guid organizationId, Guid userA, Guid userB, CancellationToken cancellationToken = default);

    /// <summary>True when the user is under an active restriction (cannot send).</summary>
    Task<bool> IsRestrictedAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
}
