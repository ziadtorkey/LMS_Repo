using Lms.Infrastructure.Jobs;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Reads the booked host slots around a time window. ISOLATION-BYPASS NOTE for the implementation: the pool is platform-wide, so
/// the implementation reads slot columns (host, organization, start, end) across clients, selected through a projection so no
/// other column or entity of another client is ever materialised.
/// </summary>
public interface IHostSlotStore
{
    Task<IReadOnlyList<HostSlot>> SlotsAroundAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

/// <summary>
/// Picks a pool host for a new session without ever double-booking one. Read, decision and the caller's reservation (the insert of
/// the session row) happen under one named lock, so two callers cannot both see the same host as free (D53). The lock is a database
/// named lock, which holds across API nodes.
/// </summary>
public sealed class ZoomHostAllocator(
    IOptions<ZoomOptions> options, IJobKeyLock keyLock, IHostSlotStore slots, Func<int> sharePercent, TimeSpan? lockTimeout = null)
{
    private const string LockResource = "zoom:host-pool";

    /// <summary>
    /// How long a caller waits for the pool lock. While it waits it holds one database connection (a named lock lives on its own
    /// connection), so the wait is short: ten callers queueing for ten seconds use ten pooled connections, not a hundred.
    /// </summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Chooses a host for <paramref name="startUtc"/>..<paramref name="endUtc"/> and calls <paramref name="reserve"/> with it while
    /// the lock is held; <paramref name="reserve"/> must commit the slot (the session row) before it returns. Nothing is reserved
    /// when the pool or the client's share is exhausted.
    /// </summary>
    public async Task<HostChoice> AllocateAsync(
        Guid organizationId, DateTime startUtc, DateTime endUtc, Func<string, Task> reserve, CancellationToken ct = default)
    {
        var zoom = options.Value;
        var buffer = TimeSpan.FromMinutes(Math.Max(0, zoom.HostBufferMinutes));
        IDisposable gate;
        try
        {
            gate = await Task.Run(() => keyLock.Acquire(LockResource, lockTimeout ?? DefaultLockTimeout), ct);
        }
        catch (TimeoutException)
        {
            // Another caller kept the pool busy for the whole wait: the same answer as an unavailable provider (409 provider_unavailable).
            throw new ZoomProviderException(ZoomFailure.Unavailable, "The Zoom host pool is busy.", TimeSpan.FromSeconds(5));
        }

        using var held = gate;

        var around = await slots.SlotsAroundAsync(startUtc - buffer, endUtc + buffer, ct);
        var choice = ZoomHostPlanner.Pick(zoom.EffectiveHostPool, around, organizationId, startUtc, endUtc, buffer, sharePercent());
        if (choice.HostId is { } host)
        {
            await reserve(host);
        }

        return choice;
    }
}
