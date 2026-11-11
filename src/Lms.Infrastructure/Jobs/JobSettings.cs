using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Jobs;

/// <summary>
/// Job options. <c>Jobs:TimeZone</c> (default UTC) is the zone the cron expressions of recurring jobs are read in and
/// the zone that decides "today" for jobs that run without an explicit day.
/// </summary>
public sealed record JobSettings(TimeZoneInfo TimeZone)
{
    public const string TimeZoneKey = "Jobs:TimeZone";

    public static JobSettings Default { get; } = new(TimeZoneInfo.Utc);

    /// <summary>Parses a time zone id (IANA or Windows); null or blank = UTC. An unknown id fails fast at startup.</summary>
    public static JobSettings Parse(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return Default;
        }

        try
        {
            return new JobSettings(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim()));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"{TimeZoneKey} '{timeZoneId}' is not a known time zone.", ex);
        }
    }

    /// <summary>The current day in the job time zone.</summary>
    public DateOnly Today(IClock clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, TimeZone));
}

/// <summary>A job the Development-only manual trigger can run on demand (E2E), under the platform context.</summary>
public sealed record DevJobDefinition(string Id, Func<IServiceProvider, DateOnly?, CancellationToken, Task> Run);
