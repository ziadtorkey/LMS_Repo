using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>Unread counts and read marks (D66): the caller's own state only; no route shows staff read state to students.</summary>
public sealed class ReadStateService(LmsDbContext db, ICurrentContext context)
{
    /// <summary>Marks everything up to the newest message the caller may read as read.</summary>
    public async Task MarkReadAsync(ConversationAccessResult access, CancellationToken ct)
    {
        var query = db.Set<Message>().Where(m => m.ConversationId == access.Conversation.Id);
        if (access.Me.LeftAtUtc is { } left)
        {
            query = query.Where(m => m.SentAtUtc <= left);
        }

        var newest = await query.OrderByDescending(m => m.Id).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        if (newest is { } id)
        {
            access.Me.MarkRead(id);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The unread message count per conversation of the caller (messages of other people after the last read one).</summary>
    public async Task<Dictionary<Guid, int>> UnreadByConversationAsync(IReadOnlyCollection<ConversationParticipant> mine, CancellationToken ct)
    {
        var me = context.UserId!.Value;
        var result = new Dictionary<Guid, int>();
        foreach (var participant in mine)
        {
            var query = db.Set<Message>().Where(m => m.ConversationId == participant.ConversationId && m.SenderUserId != me && m.State == Message.NormalState);
            if (participant.LastReadMessageId is { } last)
            {
                query = query.Where(m => m.Id > last);
            }

            if (participant.LeftAtUtc is { } left)
            {
                query = query.Where(m => m.SentAtUtc <= left);
            }

            result[participant.ConversationId] = await query.CountAsync(ct);
        }

        return result;
    }
}
