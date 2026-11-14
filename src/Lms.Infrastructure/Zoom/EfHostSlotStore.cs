using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Reads the booked pool-host slots (D53). The Zoom host pool is platform-wide, so a host booked by another client is busy for this one
/// too: this is the one query that reads live-session rows across clients, and it reads <b>slot columns only</b> (host key, client id,
/// start, duration) through a projection, so no row, title, link or other column of another client is ever materialised.
/// </summary>
public sealed class EfHostSlotStore(LmsDbContext db) : IHostSlotStore
{
    public async Task<IReadOnlyList<HostSlot>> SlotsAroundAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // A session that started up to the longest allowed duration before the window can still be running inside it.
        var earliestStart = fromUtc.AddMinutes(-LiveSessionLimits.DurationMaxMinutes);

        // ISOLATION-BYPASS: the host pool is shared by every client, so a host another client booked must count as busy; slot columns only
        // (host, client id, start, duration), projected; the soft-delete filter is re-applied by hand and cancelled rows are skipped.
        var rows = await db.Set<LiveSession>().IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.HostUserKey != null && s.Status != LiveSessionStatuses.Cancelled
                        && s.StartsAtUtc < toUtc && s.StartsAtUtc > earliestStart)
            .Select(s => new { s.HostUserKey, s.OrganizationId, s.StartsAtUtc, s.DurationMinutes })
            .ToListAsync(ct);

        return [.. rows
            .Select(r => new HostSlot(r.HostUserKey!, r.OrganizationId, r.StartsAtUtc, r.StartsAtUtc.AddMinutes(r.DurationMinutes)))
            .Where(slot => slot.EndUtc > fromUtc)];
    }
}
