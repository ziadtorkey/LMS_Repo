using Lms.Domain.Messaging;

namespace Lms.Infrastructure.Messaging;

/// <summary>Allows everything until Task 11 registers the D69 policy (this default is registered with <c>TryAdd</c>, so the real one replaces it).</summary>
public sealed class AllowAllMessagingPolicy : IMessagingPolicy
{
    public Task<MessagingDecision> CanStartAsync(Guid initiatorUserId, Guid otherUserId, CancellationToken cancellationToken) => Task.FromResult(MessagingDecision.Allow);

    public Task<MessagingDecision> CanAddToGroupAsync(Guid ownerUserId, Guid memberUserId, CancellationToken cancellationToken) => Task.FromResult(MessagingDecision.Allow);

    public Task<MessagingDecision> CanPostAsync(Guid senderUserId, Conversation conversation, CancellationToken cancellationToken) => Task.FromResult(MessagingDecision.Allow);
}
