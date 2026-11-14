namespace Lms.Domain.Comms;

/// <summary>The pure rules of the delivery outbox (M6 Task 5, proposal #613 section 4): back-off, limits per channel, quiet hours, the client's day.</summary>
public static class DeliveryPolicy
{
    public static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan BackoffCap = TimeSpan.FromHours(1);
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>How long a row waits when its provider is not configured (the row is not counted as an attempt).</summary>
    public static readonly TimeSpan NotConfiguredWait = TimeSpan.FromMinutes(15);

    /// <summary>
    /// <c>base * 2^(attempt-1)</c> with jitter (<paramref name="jitter"/> in -1..1 maps to +-20 %), capped at one hour; a provider
    /// <c>Retry-After</c> wins when it is longer.
    /// </summary>
    public static TimeSpan Backoff(int attempt, double jitter, TimeSpan? retryAfter)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var seconds = BackoffBase.TotalSeconds * Math.Pow(2, exponent) * (1 + 0.2 * Math.Clamp(jitter, -1, 1));
        var delay = TimeSpan.FromSeconds(Math.Min(seconds, BackoffCap.TotalSeconds));
        return retryAfter is { } wait && wait > delay ? wait : delay;
    }

    public static int MaxAttempts(string channel) => channel == "Push" ? 5 : 8;

    /// <summary>The window a row may stay undelivered; it starts when a hold ends, never while held.</summary>
    public static TimeSpan TimeToLive(string channel) => channel == "Push" ? TimeSpan.FromHours(4) : TimeSpan.FromHours(24);

    /// <summary>True when <paramref name="utcNow"/> lies inside the daily quiet window (it may wrap past midnight); <paramref name="endsAtUtc"/> is when it ends.</summary>
    public static bool IsQuiet(DateTime utcNow, TimeZoneInfo zone, TimeOnly start, TimeOnly end, out DateTime endsAtUtc)
    {
        endsAtUtc = default;
        if (start == end)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var time = TimeOnly.FromDateTime(local);
        var inside = start < end ? time >= start && time < end : time >= start || time < end;
        if (!inside)
        {
            return false;
        }

        var endDay = start < end || time < end ? local.Date : local.Date.AddDays(1);
        var endLocal = endDay + end.ToTimeSpan();
        endsAtUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), zone);
        return true;
    }

    /// <summary>The client's current day as UTC bounds: from its local midnight to the next one.</summary>
    public static (DateTime StartUtc, DateTime NextStartUtc) ClientDay(DateTime utcNow, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var startLocal = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, zone),
            TimeZoneInfo.ConvertTimeToUtc(startLocal.AddDays(1), zone));
    }
}
