using Lms.Domain.Messaging;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>Test double: allows every pair and every post, so the messaging kernel tests do not depend on the D69 matrix (Task 11). The product default fails closed.</summary>
public sealed class AllowAllTestMessagingPolicy : IMessagingPolicy
{
    public Task<MessagingDecision> CanStartAsync(Guid initiatorUserId, Guid otherUserId, CancellationToken cancellationToken) => Task.FromResult(MessagingDecision.Allow);

    public Task<MessagingDecision> CanAddToGroupAsync(Guid ownerUserId, Guid memberUserId, CancellationToken cancellationToken) => Task.FromResult(MessagingDecision.Allow);

    public Task<MessagingDecision> CanPostAsync(Guid senderUserId, Conversation conversation, CancellationToken cancellationToken) => Task.FromResult(MessagingDecision.Allow);
}
