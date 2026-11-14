using Lms.Domain.Announcements;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Announcements;

/// <summary>
/// The keyed jobs of an announcement (site 5 of the isolation list). They run with the context of whoever scheduled them (the author), so the row is found
/// only inside that author's client and scope: a payload that names an announcement of another client finds nothing and does nothing.
/// </summary>
public sealed class AnnouncementJobs(LmsDbContext db, AnnouncementService service)
{
    /// <summary>Publishes a scheduled announcement when its time comes. A withdrawn, edited-to-draft or missing one is left alone.</summary>
    public async Task PublishAsync(Guid announcementId, CancellationToken ct)
    {
        var row = await db.Set<Announcement>().FirstOrDefaultAsync(a => a.Id == announcementId, ct);
        if (row is { Status: AnnouncementStates.Scheduled })
        {
            await service.PublishNowAsync(row, ct);
        }
    }

    /// <summary>Marks a published announcement expired (readers already stop seeing it at its expiry time; this settles the status).</summary>
    public async Task ExpireAsync(Guid announcementId, DateTime? asOfUtc, CancellationToken ct)
    {
        var row = await db.Set<Announcement>().FirstOrDefaultAsync(a => a.Id == announcementId, ct);
        if (row is { Status: AnnouncementStates.Published } && row.ExpiresAtUtc <= (asOfUtc ?? DateTime.UtcNow).AddSeconds(5))
        {
            row.Expire();
            await db.SaveChangesAsync(ct);
        }
    }
}
