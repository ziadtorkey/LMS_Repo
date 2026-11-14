using Lms.Domain.LiveSessions;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// <see cref="IMeetingProvider"/> over the platform Zoom app (D49). Creating a meeting first allocates a pool host under the
/// allocator lock and lets the caller commit the slot (<see cref="MeetingRequest.ReserveHostAsync"/>) before the lock is released;
/// only then does the Zoom call happen. A Zoom failure after that leaves the reservation with the session row, so the sweep
/// retries under the same host. Failures never carry bodies or credentials.
/// </summary>
public sealed class ZoomMeetingProvider(
    ZoomApiClient client, ZoomHostAllocator allocator, ZoomProviderStatus status, IOptions<ZoomOptions> options) : IMeetingProvider
{
    private static readonly TimeSpan PoolRetryHint = TimeSpan.FromSeconds(60);

    public string Name => "Zoom";

    public async Task<MeetingResult> CreateMeetingAsync(MeetingRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();
        return await GuardAsync(async () =>
        {
            var host = request.HostUserKey;
            if (host is null)
            {
                var end = request.StartsAtUtc.AddMinutes(request.DurationMinutes);
                var choice = await allocator.AllocateAsync(
                    request.OrganizationId, request.StartsAtUtc, end,
                    chosen => request.ReserveHostAsync?.Invoke(chosen) ?? Task.CompletedTask, ct);
                if (choice.HostId is null)
                {
                    throw new MeetingProviderException(
                        MeetingFailure.CapacityExhausted,
                        choice.Failure == ZoomHostFailure.ClientShareReached ? "The client holds its share of the Zoom hosts." : "No Zoom host is free.",
                        (int)PoolRetryHint.TotalSeconds);
                }

                host = choice.HostId;
            }

            var meeting = await client.CreateMeetingAsync(ToZoom(request, host), ct);
            return new MeetingResult(meeting.MeetingId, meeting.JoinUrl, meeting.StartUrl, host);
        });
    }

    public async Task UpdateMeetingAsync(string externalMeetingId, MeetingRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();
        await GuardAsync(async () =>
        {
            await client.UpdateMeetingAsync(externalMeetingId, ToZoom(request, request.HostUserKey ?? ""), ct);
            return 0;
        });
    }

    public async Task DeleteMeetingAsync(string externalMeetingId, CancellationToken ct = default)
    {
        EnsureConfigured();
        await GuardAsync(async () =>
        {
            await client.DeleteMeetingAsync(externalMeetingId, ct);
            return 0;
        });
    }

    public async Task<IReadOnlyList<MeetingParticipant>> GetParticipantsAsync(string externalMeetingId, CancellationToken ct = default)
    {
        EnsureConfigured();
        var rows = await GuardAsync(() => client.GetParticipantsAsync(externalMeetingId, ct));

        // An email counts only for a participant signed in to Zoom (Decision 7): a guest can type anything into the name or email.
        return [.. rows.Select(p => new MeetingParticipant(
            p.Name, p.ZoomUserId is not null ? p.Email : null, p.JoinedAtUtc, p.LeftAtUtc, p.DurationSeconds))];
    }

    public async Task<IReadOnlyList<MeetingRecording>> GetRecordingsAsync(string externalMeetingId, CancellationToken ct = default)
    {
        EnsureConfigured();
        var files = await GuardAsync(() => client.GetRecordingsAsync(externalMeetingId, ct));
        return [.. files.Select(f => new MeetingRecording(f.PlayUrl, f.Kind))];
    }

    public async Task<ProviderHealth> GetHealthAsync(CancellationToken ct = default)
    {
        var s = await status.GetAsync(ct);
        return new ProviderHealth(s.Configured, s.Healthy);
    }

    private static ZoomMeetingRequest ToZoom(MeetingRequest request, string host) => new(
        host, request.Topic, request.StartsAtUtc, request.DurationMinutes, request.TimeZoneId, request.CloudRecording,
        new Dictionary<string, string>
        {
            ["client"] = request.OrganizationId.ToString(),
            ["classCourse"] = request.ClassCourseId.ToString(),
        });

    private void EnsureConfigured()
    {
        if (!options.Value.IsConfigured)
        {
            throw new MeetingProviderException(MeetingFailure.NotConfigured, "Zoom is not configured.");
        }
    }

    /// <summary>Maps the Zoom client failures to the provider contract: slow-down and busy-pool answers are capacity, the rest is a failed call.</summary>
    private static async Task<T> GuardAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ZoomProviderException ex)
        {
            var retry = ex.RetryAfter is { } after ? (int?)Math.Max(1, (int)Math.Ceiling(after.TotalSeconds)) : null;
            var failure = ex.Failure == ZoomFailure.RateLimited || retry is not null ? MeetingFailure.CapacityExhausted : MeetingFailure.Failed;
            throw new MeetingProviderException(failure, ex.Message, retry, ex);
        }
    }
}
