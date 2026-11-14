using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>The outcome of sending: the message, or the problem code and HTTP status that refuses it.</summary>
public sealed record SendResult(Message? Message, string? ProblemCode, int Status, int? RetryAfterSeconds = null, string? Reason = null)
{
    public static SendResult Sent(Message message) => new(message, null, 201);

    public static SendResult Refused(string code, int status) => new(null, code, status);
}

/// <summary>Sending, paging and mapping messages (FR-MSG-01, L6, L20): keyset pages newest first, a neutral name for a former user, a left member reads up to leaving.</summary>
public sealed class MessageService(LmsDbContext db, ICurrentContext context, IClock clock, IMessagingPolicy policy, MessageAttachmentGuard attachments)
{
    public const string FormerUserAr = "مستخدم سابق";
    public const string FormerUserEn = "Former user";

    public async Task<SendResult> SendAsync(ConversationAccessResult access, string? body, CancellationToken ct) => await SendAsync(access, body, null, ct);

    public async Task<SendResult> SendAsync(ConversationAccessResult access, string? body, IReadOnlyList<Guid>? attachmentFileIds, CancellationToken ct)
    {
        var text = body?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MessagingLimits.MaxBodyLength)
        {
            return SendResult.Refused(ProblemCodes.message_too_long, 400);
        }

        var (conversation, me) = (access.Conversation, access.Me);
        if (me.HasLeft)
        {
            return SendResult.Refused(ProblemCodes.messaging_not_allowed, 403);
        }

        if (conversation.IsLocked)
        {
            return SendResult.Refused(ProblemCodes.conversation_closed, 409);
        }

        if (conversation.IsReadOnly)
        {
            return new SendResult(null, ProblemCodes.conversation_read_only, 403, null, ConversationReadOnlyReasons.GroupStudentsReadOnly);
        }

        if (!(await policy.CanPostAsync(me.UserId, conversation, ct)).Allowed)
        {
            return SendResult.Refused(ProblemCodes.messaging_not_allowed, 403);
        }

        // Attach time (L12): the files must be the caller own pending uploads of this conversation, and the caller may post right now (checked above).
        var (pending, refused) = await attachments.ResolvePendingAsync(access, attachmentFileIds, ct);
        if (pending is null)
        {
            return SendResult.Refused(refused!.ProblemCode == "not_found" ? "not_found" : refused.ProblemCode!, refused.Status);
        }

        var now = clock.UtcNow;
        var message = new Message(conversation, me.UserId, text, now);
        db.Add(message);
        foreach (var attachment in pending)
        {
            attachment.AttachTo(message);
        }

        conversation.NoteMessage(now);
        me.MarkRead(message.Id); // the sender has read their own message
        await db.SaveChangesAsync(ct);
        return SendResult.Sent(message);
    }

    /// <summary>Deletes for everyone inside <c>Messaging.DeleteWindowMinutes</c> by the server clock (L20); the row stays as a tombstone, the text and the files are no longer served.</summary>
    public async Task<SendResult> DeleteAsync(ConversationAccessResult access, Guid messageId, CancellationToken ct)
    {
        var message = await db.Set<Message>().SingleOrDefaultAsync(m => m.Id == messageId && m.ConversationId == access.Conversation.Id, ct);
        if (message is null || message.SenderUserId != access.Me.UserId || message.State != Message.NormalState)
        {
            return SendResult.Refused("not_found", 404); // another person message is not yours to delete, and is not revealed either
        }

        if (!message.InDeleteWindow(clock.UtcNow, MessagingLimits.DeleteWindowMinutes))
        {
            return SendResult.Refused(ProblemCodes.message_delete_window_closed, 409);
        }

        message.DeleteForEveryone(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return SendResult.Sent(message);
    }

    /// <summary>One page, newest first. A left member sees only what was sent before they left.</summary>
    public async Task<MessagePageDto> PageAsync(ConversationAccessResult access, Guid? beforeId, int pageSize, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize, 1, MessagingLimits.MessagePageSizeMax);
        var query = db.Set<Message>().Where(m => m.ConversationId == access.Conversation.Id);
        if (access.Me.LeftAtUtc is { } left)
        {
            query = query.Where(m => m.SentAtUtc <= left);
        }

        if (beforeId is { } before)
        {
            query = query.Where(m => m.Id < before);
        }

        var rows = await query.OrderByDescending(m => m.Id).Take(size + 1).ToListAsync(ct);
        var more = rows.Count > size;
        var page = rows.Take(size).ToList();
        return new MessagePageDto(await ToDtosAsync(page, ct), more ? page[^1].Id : null);
    }

    public async Task<IReadOnlyList<MessageDto>> ToDtosAsync(IReadOnlyList<Message> messages, CancellationToken ct)
    {
        var me = context.UserId;
        var senderIds = messages.Select(m => m.SenderUserId).Distinct().ToList();
        var users = await db.Users.Where(u => senderIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToDictionaryAsync(u => u.Id, ct);
        var now = clock.UtcNow;
        var messageIds = messages.Where(m => m.State == Message.NormalState).Select(m => m.Id).ToList();
        var files = (await db.Set<MessageAttachment>().Where(a => a.MessageId != null && messageIds.Contains(a.MessageId.Value)).ToListAsync(ct))
            .GroupBy(a => a.MessageId!.Value).ToDictionary(g => g.Key, g => (IReadOnlyList<MessageAttachmentDto>)g.Select(a => new MessageAttachmentDto(a.FileId, a.FileName, a.ContentType, a.SizeBytes)).ToList());
        return messages.Select(m =>
        {
            users.TryGetValue(m.SenderUserId, out var user);
            var known = user is not null;
            var body = m.State == Message.NormalState ? m.Body : null;
            var canDelete = m.SenderUserId == me && m.State == Message.NormalState && now <= m.SentAtUtc.AddMinutes(MessagingLimits.DeleteWindowMinutes);
            return new MessageDto(m.Id, m.ConversationId, known ? m.SenderUserId : null, known ? user!.DisplayNameAr : FormerUserAr, known ? user!.DisplayNameEn : FormerUserEn,
                body, m.State, files.GetValueOrDefault(m.Id) ?? [], m.SentAtUtc, m.DeletedAtUtc, canDelete);
        }).ToList();
    }
}
