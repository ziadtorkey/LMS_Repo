namespace Lms.Domain.Messaging;

/// <summary>The answer of the policy: allowed, or refused with a reason the API turns into a problem (the reason never names who blocked whom).</summary>
public sealed record MessagingDecision(bool Allowed, string? Reason = null)
{
    public static MessagingDecision Allow { get; } = new(true);

    public static MessagingDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// The safeguarding rules of messaging (D69, L25), implemented by Task 11. It answers "may A message B" when a conversation is created and when a member is added,
/// and "may this person post here" on every message.
/// </summary>
public interface IMessagingPolicy
{
    Task<MessagingDecision> CanStartAsync(Guid initiatorUserId, Guid otherUserId, CancellationToken cancellationToken);

    Task<MessagingDecision> CanAddToGroupAsync(Guid ownerUserId, Guid memberUserId, CancellationToken cancellationToken);

    Task<MessagingDecision> CanPostAsync(Guid senderUserId, Conversation conversation, CancellationToken cancellationToken);
}
