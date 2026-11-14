using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>The outcome of sending: the message, or the problem code and HTTP status that refuses it.</summary>
public sealed record SendResult(Message? Message, string? ProblemCode, int Status)
{
    public static SendResult Sent(Message message) => new(message, null, 201);

    public static SendResult Refused(string code, int status) => new(null, code, status);
}

/// <summary>Sending, paging and mapping messages (FR-MSG-01, L6, L20): keyset pages newest first, a neutral name for a former user, a left member reads up to leaving.</summary>
public sealed class MessageService(LmsDbContext db, ICurrentContext context, IClock clock, IMessagingPolicy policy)
{
    public const string FormerUserAr = "مستخدم سابق";
    public const string FormerUserEn = "Former user";

    public async Task<SendResult> SendAsync(ConversationAccessResult access, string? body, CancellationToken ct)
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
            return SendResult.Refused(ProblemCodes.group_read_only, 403);
        }

        if (!(await policy.CanPostAsync(me.UserId, conversation, ct)).Allowed)
        {
            return SendResult.Refused(ProblemCodes.messaging_not_allowed, 403);
        }

        var now = clock.UtcNow;
        var message = new Message(conversation, me.UserId, text, now);
        db.Add(message);
        conversation.NoteMessage(now);
        me.MarkRead(message.Id); // the sender has read their own message
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
        return messages.Select(m =>
        {
            users.TryGetValue(m.SenderUserId, out var user);
            var known = user is not null;
            var body = m.State == Message.NormalState ? m.Body : null;
            var canDelete = m.SenderUserId == me && m.State == Message.NormalState && now <= m.SentAtUtc.AddMinutes(MessagingLimits.DeleteWindowMinutes);
            return new MessageDto(m.Id, m.ConversationId, known ? m.SenderUserId : null, known ? user!.DisplayNameAr : FormerUserAr, known ? user!.DisplayNameEn : FormerUserEn,
                body, m.State, [], m.SentAtUtc, m.DeletedAtUtc, canDelete);
        }).ToList();
    }
}
