using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Rate limits for the anonymous Zoom webhook route (D54). Only <b>failed verifications</b> are counted per source address, so a valid
/// burst or a Zoom retry is never refused for its address; verified traffic meets only a high global per-minute ceiling sized well
/// above Zoom's delivery rate. State is per node and in memory (a flood is cut at each node); the failure table is bounded.
/// </summary>
public sealed class ZoomWebhookRateLimiter(IClock clock, int failuresPerMinute = 20, int globalPerMinute = 6000)
{
    public const int MaxTrackedAddresses = 10_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTime WindowStart, int Failures)> _failures = [];
    private DateTime _globalWindow;
    private int _globalCount;

    public int TrackedAddresses
    {
        get
        {
            lock (_gate)
            {
                return _failures.Count;
            }
        }
    }

    /// <summary>True once the address has reached the failure threshold within the current minute (answer 429, touch no database).</summary>
    public bool IsBlocked(string address)
    {
        lock (_gate)
        {
            return _failures.TryGetValue(address, out var entry)
                   && clock.UtcNow - entry.WindowStart < TimeSpan.FromMinutes(1)
                   && entry.Failures >= failuresPerMinute;
        }
    }

    public void RegisterFailure(string address)
    {
        lock (_gate)
        {
            var now = clock.UtcNow;
            if (!_failures.TryGetValue(address, out var entry) || now - entry.WindowStart >= TimeSpan.FromMinutes(1))
            {
                if (_failures.Count >= MaxTrackedAddresses)
                {
                    Evict(now);
                }

                entry = (now, 0);
            }

            _failures[address] = (entry.WindowStart, entry.Failures + 1);
        }
    }

    /// <summary>Counts one verified request against the global ceiling; false means over the ceiling (answer 429).</summary>
    public bool TryAdmit()
    {
        lock (_gate)
        {
            var now = clock.UtcNow;
            if (now - _globalWindow >= TimeSpan.FromMinutes(1))
            {
                _globalWindow = now;
                _globalCount = 0;
            }

            return ++_globalCount <= globalPerMinute;
        }
    }

    /// <summary>Drops expired windows; if the table is still full, the oldest quarter goes (an attacker cannot pin the table).</summary>
    private void Evict(DateTime now)
    {
        foreach (var expired in _failures.Where(kv => now - kv.Value.WindowStart >= TimeSpan.FromMinutes(1)).Select(kv => kv.Key).ToList())
        {
            _failures.Remove(expired);
        }

        if (_failures.Count >= MaxTrackedAddresses)
        {
            foreach (var oldest in _failures.OrderBy(kv => kv.Value.WindowStart).Take(MaxTrackedAddresses / 4).Select(kv => kv.Key).ToList())
            {
                _failures.Remove(oldest);
            }
        }
    }
}
