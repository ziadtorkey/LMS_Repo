using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.LiveSessions;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Retries live sessions whose provider call failed (<c>SyncFailed</c>), as a recurring system job (platform context) that works through
/// the clients one at a time: only the rows of that client are read and each is synced through <see cref="LiveSessionService"/>, which
/// keeps a failing row in <c>SyncFailed</c> for the next run. A named lock per client keeps overlapping runs (Hangfire overlap, the dev
/// trigger, two nodes) from syncing the same session twice. A row that already holds a pool host keeps it (no second allocation). One
/// client failing never stops the others; sessions that have already started are left alone.
/// </summary>
public sealed class ZoomSyncSweepJob(
    LmsDbContext db, LiveSessionService service, IMeetingProvider provider, IClock clock, IJobKeyLock keyLock, ILogger<ZoomSyncSweepJob> logger)
{
    public const string RecurringJobId = "zoom-sync-sweep";

    /// <summary>Every five minutes.</summary>
    public const string Cron = "*/5 * * * *";

    private const int BatchPerClient = 100;

    /// <summary>Returns the number of sessions that are in sync after the run.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // The platform context sees every client; the list of clients is platform-level data.
        var organizationIds = await db.Set<Organization>().Select(o => o.Id).ToListAsync(ct);
        var recovered = 0;
        foreach (var organizationId in organizationIds)
        {
            // One sweep per client at a time, across Hangfire overlap, the dev trigger and other nodes: two runs would each create a
            // Zoom meeting for the same session. A run that finds the client busy skips it (the other run is doing the work).
            IDisposable gate;
            try
            {
                gate = await Task.Run(() => keyLock.Acquire($"zoom-sync-sweep:{organizationId:N}", TimeSpan.Zero), ct);
            }
            catch (TimeoutException)
            {
                continue;
            }

            try
            {
                recovered += await SweepClientAsync(organizationId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "The live session sync sweep failed for client {OrganizationId}", organizationId);
            }
            finally
            {
                gate.Dispose();
            }
        }

        return recovered;
    }

    private async Task<int> SweepClientAsync(Guid organizationId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var sessions = await db.Set<LiveSession>()
            .Where(s => s.OrganizationId == organizationId && s.Provider == provider.Name && s.Status == LiveSessionStatuses.SyncFailed
                        && s.StartsAtUtc > now)
            .OrderBy(s => s.StartsAtUtc).Take(BatchPerClient).ToListAsync(ct);
        var recovered = 0;
        foreach (var session in sessions)
        {
            var classCourse = await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == session.ClassCourseId, ct);
            var module = session.ModuleId is { } moduleId
                ? await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == moduleId, ct)
                : null;
            if (classCourse is null || module is null)
            {
                continue; // deleted underneath the row: nothing to sync
            }

            var timeZone = session.SeriesId is { } seriesId
                ? await db.Set<LiveSessionSeries>().Where(s => s.Id == seriesId).Select(s => s.TimeZoneId).FirstOrDefaultAsync(ct) ?? "UTC"
                : "UTC";
            var metadata = new ModuleMetadata(
                module.TitleAr, module.TitleEn, module.Visibility, module.VisibleFromUtc, module.VisibleToUtc, module.Target, module.GroupId,
                module.CompletionRule, module.CompletionMinScore);
            await service.SyncNewAsync(classCourse, metadata, [session], timeZone, strict: false, ct);
            if (session.Status == LiveSessionStatuses.Scheduled)
            {
                recovered++;
            }
        }

        return recovered;
    }
}
