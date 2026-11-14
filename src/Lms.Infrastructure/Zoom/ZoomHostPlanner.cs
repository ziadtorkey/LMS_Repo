namespace Lms.Infrastructure.Zoom;

/// <summary>One booked stretch of one pool host. Carries only slot columns: no titles, users or other client data.</summary>
public sealed record HostSlot(string HostId, Guid OrganizationId, DateTime StartUtc, DateTime EndUtc);

public enum ZoomHostFailure
{
    /// <summary>Every pool host is busy around the requested time (or the pool is empty).</summary>
    PoolExhausted,

    /// <summary>Free hosts exist, but this client already holds its share of the pool in an overlapping slot.</summary>
    ClientShareReached,
}

public sealed record HostChoice(string? HostId, ZoomHostFailure? Failure)
{
    public static HostChoice Of(string hostId) => new(hostId, null);

    public static HostChoice Refused(ZoomHostFailure failure) => new(null, failure);
}

/// <summary>
/// The host-allocation rule as a pure function (D53): a host is free when no booked slot comes within the buffer before the start
/// or after the end of the requested session, and one client may hold at most <c>sharePercent</c> of the pool (at least one host)
/// among the hosts busy around that time. The caller supplies the booked slots and holds the database lock.
/// </summary>
public static class ZoomHostPlanner
{
    public static HostChoice Pick(
        IReadOnlyList<string> pool, IReadOnlyList<HostSlot> slots, Guid organizationId,
        DateTime startUtc, DateTime endUtc, TimeSpan buffer, int sharePercent)
    {
        var from = startUtc - buffer;
        var to = endUtc + buffer;
        var around = slots.Where(s => s.StartUtc < to && s.EndUtc > from).ToList();

        var cap = Math.Max(1, (int)Math.Floor(pool.Count * Math.Clamp(sharePercent, 0, 100) / 100.0));
        var heldByClient = around.Where(s => s.OrganizationId == organizationId).Select(s => s.HostId).Distinct().Count();
        var busy = around.Select(s => s.HostId).ToHashSet();
        var free = pool.FirstOrDefault(h => !busy.Contains(h));

        if (free is null)
        {
            return HostChoice.Refused(ZoomHostFailure.PoolExhausted);
        }

        return heldByClient >= cap ? HostChoice.Refused(ZoomHostFailure.ClientShareReached) : HostChoice.Of(free);
    }
}
