using Lms.Infrastructure.Messaging;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Lms.Api.Features.Notifications;

/// <summary>
/// The messaging events over SignalR (L13). The hub has no client-callable join: a connection is in its own user-and-client group only, and each event goes to the groups of the
/// participants the server names, so nobody receives a conversation they are not in. Ids only, never text.
/// </summary>
public sealed class SignalRMessagingRealtime(IHubContext<NotificationsHub> hub) : IMessagingRealtime
{
    public Task MessageReceivedAsync(Guid organizationId, IReadOnlyCollection<Guid> userIds, Guid conversationId, Guid messageId, CancellationToken cancellationToken) =>
        SendAsync(organizationId, userIds, NotificationHub.MessageReceivedMethod, new MessageReceivedEvent(conversationId, messageId), cancellationToken);

    public Task ConversationReadAsync(Guid organizationId, Guid userId, Guid conversationId, Guid lastReadMessageId, CancellationToken cancellationToken) =>
        SendAsync(organizationId, [userId], NotificationHub.ConversationReadMethod, new ConversationReadEvent(conversationId, lastReadMessageId), cancellationToken);

    public Task ConversationChangedAsync(Guid organizationId, IReadOnlyCollection<Guid> userIds, Guid conversationId, CancellationToken cancellationToken) =>
        SendAsync(organizationId, userIds, NotificationHub.ConversationChangedMethod, new ConversationChangedEvent(conversationId), cancellationToken);

    private async Task SendAsync(Guid organizationId, IReadOnlyCollection<Guid> userIds, string method, object payload, CancellationToken ct)
    {
        foreach (var userId in userIds)
        {
            await hub.Clients.Group(NotificationGroups.For(userId, organizationId)).SendAsync(method, payload, ct);
        }
    }
}
