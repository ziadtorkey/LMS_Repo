using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Messaging;

/// <summary>
/// Sends the realtime events to people who are working in this client (L13): <b>ids only, never text</b>, to each participant own connections only, after the change is committed.
/// The transport lives in the API (SignalR); the default does nothing.
/// </summary>
public interface IMessagingRealtime
{
    Task MessageReceivedAsync(Guid organizationId, IReadOnlyCollection<Guid> userIds, Guid conversationId, Guid messageId, CancellationToken cancellationToken);

    Task ConversationReadAsync(Guid organizationId, Guid userId, Guid conversationId, Guid lastReadMessageId, CancellationToken cancellationToken);

    Task ConversationChangedAsync(Guid organizationId, IReadOnlyCollection<Guid> userIds, Guid conversationId, CancellationToken cancellationToken);
}

public sealed class NoOpMessagingRealtime : IMessagingRealtime
{
    public Task MessageReceivedAsync(Guid organizationId, IReadOnlyCollection<Guid> userIds, Guid conversationId, Guid messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ConversationReadAsync(Guid organizationId, Guid userId, Guid conversationId, Guid lastReadMessageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ConversationChangedAsync(Guid organizationId, IReadOnlyCollection<Guid> userIds, Guid conversationId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// What happens after a messaging change is saved: the realtime events and the throttled notification. Nothing is sent when the <c>Messaging</c> flag is off for the caller scope,
/// a member who left gets nothing, and a failing transport never fails the request (the change is already stored; the client catches up on its next fetch).
/// </summary>
public sealed class MessagingEvents(
    LmsDbContext db, ICurrentContext context, IClock clock, ISettingsResolver settings, IMessagingRealtime realtime, INotificationPublisher notifications, ILogger<MessagingEvents> log)
{
    public const string NewMessageLinkPrefix = "/messages/";

    public async Task MessageSentAsync(Conversation conversation, Message message, CancellationToken ct)
    {
        if (!await EnabledAsync(ct))
        {
            return;
        }

        var recipients = await ActiveParticipantsAsync(conversation.Id, ct);
        var others = recipients.Where(p => p.UserId != message.SenderUserId).ToList();
        await TryAsync(() => realtime.MessageReceivedAsync(conversation.OrganizationId, others.Select(p => p.UserId).ToList(), conversation.Id, message.Id, ct));
        await TryAsync(() => NotifyAsync(conversation, message, others, ct));
    }

    /// <summary>The caller read the conversation: their other tabs hear it, and the unread notification of the conversation is read too.</summary>
    public async Task ReadAsync(Conversation conversation, Guid userId, Guid lastReadMessageId, CancellationToken ct)
    {
        var link = NewMessageLinkPrefix + conversation.Id;
        var unread = await db.Set<Notification>().Where(n => n.UserId == userId && n.EventKey == NotificationEvents.MessagesNewMessage && n.DeepLink == link && n.ReadAt == null).ToListAsync(ct);
        if (unread.Count > 0)
        {
            var now = clock.UtcNow;
            unread.ForEach(n => n.MarkRead(now));
            await db.SaveChangesAsync(ct);
        }

        if (await EnabledAsync(ct))
        {
            await TryAsync(() => realtime.ConversationReadAsync(conversation.OrganizationId, userId, conversation.Id, lastReadMessageId, ct));
        }
    }

    /// <summary>Members, the lock or the read-only state changed. <paramref name="alsoUserIds"/> are people who just left or were removed and still need to refetch.</summary>
    public async Task ChangedAsync(Conversation conversation, IReadOnlyCollection<Guid>? alsoUserIds, CancellationToken ct)
    {
        if (!await EnabledAsync(ct))
        {
            return;
        }

        var users = (await ActiveParticipantsAsync(conversation.Id, ct)).Select(p => p.UserId).Concat(alsoUserIds ?? []).Distinct().ToList();
        await TryAsync(() => realtime.ConversationChangedAsync(conversation.OrganizationId, users, conversation.Id, ct));
    }

    private async Task NotifyAsync(Conversation conversation, Message message, IReadOnlyList<ConversationParticipant> recipients, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var link = NewMessageLinkPrefix + conversation.Id;
        var candidates = recipients.Where(p => !(p.Muted && (p.MutedUntilUtc is null || p.MutedUntilUtc > now))).Select(p => p.UserId).ToList(); // a muted conversation is silent
        if (candidates.Count == 0)
        {
            return;
        }

        // One unread notification per conversation until it is read (a busy chat must not flood the bell).
        var alreadyNotified = await db.Set<Notification>()
            .Where(n => candidates.Contains(n.UserId) && n.EventKey == NotificationEvents.MessagesNewMessage && n.DeepLink == link && n.ReadAt == null)
            .Select(n => n.UserId).Distinct().ToListAsync(ct);
        var targets = candidates.Except(alreadyNotified).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var sender = await db.Users.Where(u => u.Id == message.SenderUserId).Select(u => new { u.DisplayNameAr, u.DisplayNameEn }).SingleOrDefaultAsync(ct);
        var parameters = new Dictionary<string, string>
        {
            ["SenderNameAr"] = sender?.DisplayNameAr ?? MessageService.FormerUserAr, // the sender name only: never the text (the push and the bell show a name)
            ["SenderNameEn"] = sender?.DisplayNameEn ?? MessageService.FormerUserEn,
        };
        await notifications.PublishAsync(new NotificationRequest(NotificationEvents.MessagesNewMessage, conversation.OrganizationId, targets, parameters, link), ct);
    }

    private async Task<bool> EnabledAsync(CancellationToken ct) =>
        context.ScopePath is { } path && (await settings.ResolveAsync(FeatureFlags.Messaging, path, ct)).Value;

    private Task<List<ConversationParticipant>> ActiveParticipantsAsync(Guid conversationId, CancellationToken ct) =>
        db.Set<ConversationParticipant>().Where(p => p.ConversationId == conversationId && p.LeftAtUtc == null).ToListAsync(ct);

    private async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "A messaging realtime event or notification failed after the change was saved.");
        }
    }
}
