using Lms.Domain.Messaging;
using Lms.Shared;

namespace Lms.Infrastructure.Messaging;

/// <summary>
/// The default policy until Task 11 registers the D69 matrix: it fails <b>closed</b>. Nobody may start a conversation, add a member or post, so enabling the
/// <c>Messaging</c> flag before the real policy exists cannot open a student-to-student or admin-to-student channel. It is registered with <c>TryAdd</c>, so the real one replaces it.
/// </summary>
public sealed class DenyAllMessagingPolicy : IMessagingPolicy
{
    private static readonly Task<MessagingDecision> Denied = Task.FromResult(MessagingDecision.Deny(ProblemCodes.messaging_not_allowed));

    public Task<MessagingDecision> CanStartAsync(Guid initiatorUserId, Guid otherUserId, CancellationToken cancellationToken) => Denied;

    public Task<MessagingDecision> CanAddToGroupAsync(Guid ownerUserId, Guid memberUserId, CancellationToken cancellationToken) => Denied;

    public Task<MessagingDecision> CanPostAsync(Guid senderUserId, Conversation conversation, CancellationToken cancellationToken) => Denied;
}
