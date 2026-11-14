using System.Collections.Concurrent;
using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>
/// Message search (FR-MSG-01, L21): joined to the caller own participant rows, so a conversation the caller is not in can never match; a left member finds only what was sent before
/// leaving; deleted and removed messages are not searchable. A search shows at most <see cref="MessagingLimits.SearchWindow"/> hits however it is paged.
/// </summary>
public sealed class MessageSearch(LmsDbContext db, ICurrentContext context)
{
    private const int SnippetRadius = 60;

    public async Task<IReadOnlyList<MessageSearchResultDto>> SearchAsync(string query, Guid? conversationId, int page, CancellationToken ct)
    {
        var me = context.UserId!.Value;
        var phrase = "\"" + query.Replace("\"", string.Empty).Trim() + "\""; // a phrase: the ngram tokens must be adjacent, so "hello" does not match every "he"
        var hits = db.Set<Message>().FromSqlInterpolated($"SELECT * FROM `Messages` WHERE MATCH(`Body`) AGAINST ({phrase} IN BOOLEAN MODE)");
        var scoped = from m in hits
                     join p in db.Set<ConversationParticipant>() on m.ConversationId equals p.ConversationId
                     where p.UserId == me && m.State == Message.NormalState && (p.LeftAtUtc == null || m.SentAtUtc <= p.LeftAtUtc)
                     select m;
        if (conversationId is { } only)
        {
            scoped = scoped.Where(m => m.ConversationId == only);
        }

        var size = MessagingLimits.SearchMaxResults;
        var rows = await scoped.OrderByDescending(m => m.Id).Take(MessagingLimits.SearchWindow).Skip((Math.Max(page, 1) - 1) * size).Take(size).ToListAsync(ct);
        var senderIds = rows.Select(m => m.SenderUserId).Distinct().ToList();
        var users = await db.Users.Where(u => senderIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToDictionaryAsync(u => u.Id, ct);
        return rows.Select(m =>
        {
            users.TryGetValue(m.SenderUserId, out var user);
            return new MessageSearchResultDto(m.Id, m.ConversationId, Snippet(m.Body, query), user?.DisplayNameAr ?? MessageService.FormerUserAr, user?.DisplayNameEn ?? MessageService.FormerUserEn, m.SentAtUtc);
        }).ToList();
    }

    private static string Snippet(string body, string query)
    {
        var at = body.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, (at < 0 ? 0 : at) - SnippetRadius);
        var length = Math.Min(body.Length - start, SnippetRadius * 2 + query.Length);
        return (start > 0 ? "…" : string.Empty) + body.Substring(start, length) + (start + length < body.Length ? "…" : string.Empty);
    }
}

/// <summary>The search rate limit: at most <see cref="PerMinute"/> searches per person per minute (a search is a full-text query; this keeps it from being a scraping tool).</summary>
public sealed class MessageSearchThrottle(IClock clock)
{
    public const int PerMinute = 20;
    private readonly ConcurrentDictionary<Guid, Queue<DateTime>> _recent = new();

    /// <summary>Null when the search may run (and is counted), else the seconds to wait.</summary>
    public int? TryTake(Guid userId)
    {
        var now = clock.UtcNow;
        var queue = _recent.GetOrAdd(userId, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() >= TimeSpan.FromMinutes(1))
            {
                queue.Dequeue();
            }

            if (queue.Count >= PerMinute)
            {
                return Math.Max(1, (int)Math.Ceiling((queue.Peek().AddMinutes(1) - now).TotalSeconds));
            }

            queue.Enqueue(now);
            return null;
        }
    }
}
