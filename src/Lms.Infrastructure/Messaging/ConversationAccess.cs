using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>A conversation the caller may read and their own participant row (the left date limits what they read).</summary>
public sealed record ConversationAccessResult(Conversation Conversation, ConversationParticipant Me);

/// <summary>
/// The only participant gate (L6, D66, D69): every conversation, message, attachment, search and realtime path goes through it. The caller must be a participant
/// (a member who left keeps reading what was sent before leaving); nobody else reads a conversation, an Org Admin, a moderator, a platform admin or a guardian
/// included. A miss is null, which the API answers as 404.
/// </summary>
public sealed class ConversationAccess(LmsDbContext db, ICurrentContext context)
{
    public async Task<ConversationAccessResult?> OpenAsync(Guid conversationId, CancellationToken ct)
    {
        if (context.UserId is not { } me || context.OrganizationId is null)
        {
            return null;
        }

        var participant = await db.Set<ConversationParticipant>().SingleOrDefaultAsync(p => p.ConversationId == conversationId && p.UserId == me, ct);
        if (participant is null)
        {
            return null;
        }

        var conversation = await db.Set<Conversation>().SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        return conversation is null ? null : new ConversationAccessResult(conversation, participant);
    }
}
