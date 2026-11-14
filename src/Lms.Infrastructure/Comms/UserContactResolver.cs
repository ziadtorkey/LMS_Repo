using Lms.Domain.Comms;
using Lms.Domain.Identity;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Comms;

/// <summary>
/// Email = the user's own address while the account is active. Push resolves a marker for a user with at least one push subscription, and nothing otherwise (no push row is written). The address is read at send time and never copied into the outbox.
/// </summary>
public sealed class UserContactResolver(LmsDbContext db) : IContactResolver
{
    public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> userIds, string channel, CancellationToken ct)
    {
        if (userIds.Count == 0 || channel is not (NotificationChannels.Email or NotificationChannels.Push))
        {
            return new Dictionary<Guid, string>();
        }

        if (channel == NotificationChannels.Push)
        {
            // The "address" of push is only a marker: the keys and endpoints stay in the subscription rows and are read by the channel at send time.
            var subscribed = await db.Set<PushSubscription>().AsNoTracking()
                .Where(s => userIds.Contains(s.UserId)).Select(s => s.UserId).Distinct().ToListAsync(ct);
            return subscribed.ToDictionary(id => id, _ => "push-subscription");
        }

        var ids = userIds.Distinct().ToList();
        var result = new Dictionary<Guid, string>();
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = await db.Users.AsNoTracking()
                .Where(u => chunk.Contains(u.Id) && u.Status == UserStatus.Active && u.Email != null && u.Email != string.Empty)
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                result[row.Id] = row.Email!;
            }
        }

        return result;
    }
}
