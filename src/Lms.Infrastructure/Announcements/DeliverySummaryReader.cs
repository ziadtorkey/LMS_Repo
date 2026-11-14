using Lms.Domain.Announcements;
using Lms.Domain.Comms;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Announcements;

/// <summary>
/// What happened to the notices of a published announcement, for its authors (M6 Task 13/14): how many deliveries per channel are in each status. In-app notices
/// count as <c>Sent</c> (they exist the moment they are written); email and push come from the outbox rows of those notices. Counts only, never a recipient.
/// Null before publishing and when the author did not ask to notify.
/// </summary>
public sealed class DeliverySummaryReader(LmsDbContext db)
{
    public async Task<AnnouncementDeliverySummaryDto?> ForAsync(Announcement announcement, CancellationToken ct)
    {
        if (announcement.PublishedAtUtc is null || !announcement.Notify)
        {
            return null;
        }

        var notices = db.Set<Notification>().Where(n => n.SourceId == announcement.Id).Select(n => n.Id);
        var inApp = await db.Set<Notification>().CountAsync(n => n.SourceId == announcement.Id, ct);
        var outbox = await db.Set<DeliveryOutbox>().Where(r => r.NotificationId != null && notices.Contains(r.NotificationId.Value))
            .GroupBy(r => new { r.Channel, r.Status }).Select(g => new { g.Key.Channel, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        List<AnnouncementDeliveryCountDto> counts = [];
        if (inApp > 0)
        {
            counts.Add(new AnnouncementDeliveryCountDto(NotificationChannels.InApp, DeliveryStatuses.Sent, inApp));
        }

        counts.AddRange(outbox.OrderBy(o => o.Channel).ThenBy(o => o.Status).Select(o => new AnnouncementDeliveryCountDto(o.Channel, o.Status, o.Count)));
        return new AnnouncementDeliverySummaryDto(counts);
    }
}
