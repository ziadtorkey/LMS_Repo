using Lms.Domain.Abstractions;
using Lms.Domain.LiveSessions;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// The fallback for missed webhooks (D54): sessions whose scheduled end passed without an <c>Ended</c> event are ended through
/// <see cref="LiveSessionLifecycle"/> (participants imported, the attendance seam called once), and ended sessions of units with cloud
/// recording on that have no recording yet are asked for one. A recurring system job (platform context) that works client by client
/// under a named lock, so overlapping runs do not repeat each other; one client failing never stops the others.
/// </summary>
public sealed class ZoomPollingJob(
    LmsDbContext db, LiveSessionLifecycle lifecycle, IMeetingProvider provider, ISettingsResolver settings, IClock clock, IJobKeyLock keyLock,
    ILogger<ZoomPollingJob> logger)
{
    public const string RecurringJobId = "zoom-polling";

    /// <summary>Every fifteen minutes.</summary>
    public const string Cron = "*/15 * * * *";

    /// <summary>Zoom needs a moment after the end before its reports exist.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan Lookback = TimeSpan.FromHours(48);
    private const int BatchPerClient = 100;

    /// <summary>Returns the number of sessions ended or given a recording by this run.</summary>
    public Task<int> RunAsync(CancellationToken ct = default) => RunAsync(null, ct);

    public Task<int> RunForClientAsync(Guid organizationId, CancellationToken ct = default) => RunAsync(organizationId, ct);

    private async Task<int> RunAsync(Guid? only, CancellationToken ct)
    {
        var organizationIds = await db.Set<Organization>().Where(o => only == null || o.Id == only).Select(o => o.Id).ToListAsync(ct);
        var changed = 0;
        foreach (var organizationId in organizationIds)
        {
            IDisposable gate;
            try
            {
                gate = await Task.Run(() => keyLock.Acquire($"zoom-polling:{organizationId:N}", TimeSpan.Zero), ct);
            }
            catch (TimeoutException)
            {
                continue; // another run is working on this client
            }

            try
            {
                changed += await PollClientAsync(organizationId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "The Zoom polling failed for client {OrganizationId}", organizationId);
            }
            finally
            {
                gate.Dispose();
            }
        }

        return changed;
    }

    private async Task<int> PollClientAsync(Guid organizationId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var changed = 0;

        var overdue = await db.Set<LiveSession>()
            .Where(s => s.OrganizationId == organizationId && s.Provider == provider.Name && s.ExternalMeetingId != null
                        && (s.Status == LiveSessionRules.Scheduled || s.Status == LiveSessionRules.Live)
                        && s.StartsAtUtc < now && s.StartsAtUtc > now - Lookback)
            .OrderBy(s => s.StartsAtUtc).Take(BatchPerClient).ToListAsync(ct);
        foreach (var session in overdue.Where(s => s.EndsAtUtc + Grace < now))
        {
            if (await lifecycle.EndedAsync(session.Id, ct))
            {
                changed++;
            }
        }

        var ended = await db.Set<LiveSession>()
            .Where(s => s.OrganizationId == organizationId && s.Provider == provider.Name && s.ExternalMeetingId != null
                        && s.Status == LiveSessionRules.Ended && s.RecordingUrl == null && s.StartsAtUtc > now - Lookback)
            .OrderBy(s => s.StartsAtUtc).Take(BatchPerClient).ToListAsync(ct);
        foreach (var session in ended)
        {
            if (!(await settings.ResolveAsync(SettingKeys.LiveCloudRecordingEnabled, session.OrgUnitPath, ct)).Value)
            {
                continue;
            }

            try
            {
                var recordings = await provider.GetRecordingsAsync(session.ExternalMeetingId!, ct);
                if (recordings.FirstOrDefault() is { } first)
                {
                    session.SetRecording(first.Url, LiveSessionRules.RecordingProvider);
                    await db.SaveChangesAsync(ct);
                    changed++;
                }
            }
            catch (MeetingProviderException ex)
            {
                logger.LogWarning(ex, "Recordings of live session {SessionId} could not be read", session.Id); // the next run asks again
            }
        }

        return changed;
    }
}
