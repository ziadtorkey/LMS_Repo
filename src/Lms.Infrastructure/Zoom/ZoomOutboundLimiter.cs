using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Zoom;

/// <summary>Zoom groups its endpoints into rate-limit classes; each has its own per-second budget.</summary>
public enum ZoomRateClass
{
    Light,
    Medium,
    Heavy,
}

/// <summary>
/// Outbound limiter for the one platform Zoom app: a token bucket per rate-limit class (one second of burst), a penalty window when
/// Zoom answers 429 with <c>Retry-After</c>, and a bounded queue so a flood of callers fails fast instead of piling up.
/// </summary>
public sealed class ZoomOutboundLimiter(IClock clock, Func<TimeSpan, CancellationToken, Task>? delay = null, int maxQueue = 200)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly object _gate = new();
    private readonly Dictionary<ZoomRateClass, Bucket> _buckets = [];
    private int _waiting;

    /// <summary>Calls per second Zoom allows for a class (per account, documented defaults).</summary>
    public static int PerSecond(ZoomRateClass rateClass) => rateClass switch
    {
        ZoomRateClass.Light => 30,
        ZoomRateClass.Medium => 20,
        _ => 10,
    };

    /// <summary>Report endpoints are heavy, recording endpoints medium, everything else (users, meetings) light.</summary>
    public static ZoomRateClass Classify(HttpMethod method, string path)
    {
        var p = path.TrimStart('/');
        if (p.StartsWith("report/", StringComparison.OrdinalIgnoreCase))
        {
            return ZoomRateClass.Heavy;
        }

        return p.Contains("/recordings", StringComparison.OrdinalIgnoreCase) ? ZoomRateClass.Medium : ZoomRateClass.Light;
    }

    /// <summary>Waits until a call of this class may be sent. Throws <see cref="ZoomFailure.RateLimited"/> when too many callers already wait.</summary>
    public async Task AcquireAsync(ZoomRateClass rateClass, CancellationToken ct = default)
    {
        var counted = false;
        try
        {
            while (true)
            {
                TimeSpan wait;
                lock (_gate)
                {
                    var bucket = BucketFor(rateClass);
                    var now = clock.UtcNow;
                    bucket.Refill(now, PerSecond(rateClass));
                    wait = bucket.WaitFor(now, PerSecond(rateClass));
                    if (wait <= TimeSpan.Zero)
                    {
                        bucket.Tokens -= 1;
                        return;
                    }

                    if (!counted)
                    {
                        if (_waiting >= maxQueue)
                        {
                            throw new ZoomProviderException(ZoomFailure.RateLimited, "Too many Zoom calls are waiting.", wait);
                        }

                        _waiting++;
                        counted = true;
                    }
                }

                await _delay(wait, ct);
            }
        }
        finally
        {
            if (counted)
            {
                lock (_gate)
                {
                    _waiting--;
                }
            }
        }
    }

    /// <summary>Zoom answered 429: nothing of this class is sent until <paramref name="retryAfter"/> has passed.</summary>
    public void Penalize(ZoomRateClass rateClass, TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var bucket = BucketFor(rateClass);
            var until = clock.UtcNow + retryAfter;
            if (until > bucket.BlockedUntil)
            {
                bucket.BlockedUntil = until;
            }
        }
    }

    private Bucket BucketFor(ZoomRateClass rateClass)
    {
        if (!_buckets.TryGetValue(rateClass, out var bucket))
        {
            bucket = new Bucket { Tokens = PerSecond(rateClass), LastRefill = clock.UtcNow };
            _buckets[rateClass] = bucket;
        }

        return bucket;
    }

    private sealed class Bucket
    {
        public double Tokens { get; set; }
        public DateTime LastRefill { get; set; }
        public DateTime BlockedUntil { get; set; }

        public void Refill(DateTime now, int perSecond)
        {
            Tokens = Math.Min(perSecond, Tokens + (now - LastRefill).TotalSeconds * perSecond);
            LastRefill = now;
        }

        public TimeSpan WaitFor(DateTime now, int perSecond)
        {
            if (BlockedUntil > now)
            {
                return BlockedUntil - now;
            }

            return Tokens >= 1 ? TimeSpan.Zero : TimeSpan.FromSeconds((1 - Tokens) / perSecond);
        }
    }
}
