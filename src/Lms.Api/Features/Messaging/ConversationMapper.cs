using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Messaging;

/// <summary>Builds the conversation DTOs: participants with names ("Former user" for a deleted account), the caller mute and read-only state.</summary>
public sealed class ConversationMapper(LmsDbContext db, ReadStateService readState, IClock clock)
{
    public async Task<ConversationDto> ToDtoAsync(ConversationAccessResult access, CancellationToken ct)
    {
        var participants = await ParticipantsAsync([access.Conversation.Id], ct);
        var c = access.Conversation;
        var reason = c.IsLocked ? ConversationReadOnlyReasons.Locked
            : c.IsReadOnly ? ConversationReadOnlyReasons.GroupStudentsReadOnly
            : access.Me.HasLeft ? ConversationReadOnlyReasons.BlockedOrRule
            : null; // the D69 policy (Task 11) refines the reason; until then these are the causes this kernel knows
        return new ConversationDto(c.Id, c.Kind, c.Title, participants[c.Id], c.IsLocked, IsMuted(access.Me), c.CreatedAtUtc,
            IsReadOnly: c.IsReadOnly || access.Me.HasLeft, OwnerUserId: c.OwnerUserId, ClassCourseId: c.ClassCourseId, CanPost: reason is null, ReadOnlyReason: reason);
    }

    public async Task<IReadOnlyList<ConversationListItemDto>> ToListAsync(IReadOnlyList<ConversationParticipant> mine, CancellationToken ct)
    {
        var ids = mine.Select(p => p.ConversationId).ToList();
        var conversations = await db.Set<Conversation>().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var participants = await ParticipantsAsync(ids, ct);
        var unread = await readState.UnreadByConversationAsync(mine, ct);
        var items = new List<ConversationListItemDto>();
        foreach (var p in mine)
        {
            var c = conversations[p.ConversationId];
            var query = db.Set<Message>().Where(m => m.ConversationId == c.Id);
            if (p.LeftAtUtc is { } left)
            {
                query = query.Where(m => m.SentAtUtc <= left);
            }

            var last = await query.OrderByDescending(m => m.Id).FirstOrDefaultAsync(ct);
            var preview = last is { State: Message.NormalState } ? Cut(last.Body) : null; // a tombstone never shows its original text
            items.Add(new ConversationListItemDto(c.Id, c.Kind, c.Title, participants[c.Id], unread.GetValueOrDefault(c.Id), preview, last?.SentAtUtc, IsMuted(p), c.IsLocked,
                c.IsReadOnly || p.HasLeft));
        }

        return items; // the caller passes the rows already ordered (newest activity first)
    }

    private async Task<Dictionary<Guid, IReadOnlyList<ParticipantDto>>> ParticipantsAsync(IReadOnlyCollection<Guid> conversationIds, CancellationToken ct)
    {
        var rows = await db.Set<ConversationParticipant>().Where(p => conversationIds.Contains(p.ConversationId)).ToListAsync(ct);
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToDictionaryAsync(u => u.Id, ct);
        return rows.GroupBy(r => r.ConversationId).ToDictionary(g => g.Key, g => (IReadOnlyList<ParticipantDto>)g.Select(p =>
        {
            users.TryGetValue(p.UserId, out var user);
            return new ParticipantDto(p.UserId, user?.DisplayNameAr ?? MessageService.FormerUserAr, user?.DisplayNameEn ?? MessageService.FormerUserEn, null, p.HasLeft);
        }).ToList());
    }

    private bool IsMuted(ConversationParticipant p) => p.Muted && (p.MutedUntilUtc is null || p.MutedUntilUtc > clock.UtcNow);

    private static string Cut(string body) => body.Length <= 120 ? body : body[..120];
}
