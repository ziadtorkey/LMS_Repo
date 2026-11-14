using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Attendance.Live;

/// <summary>
/// The attendance side of the live-session seam (M4b #544, M5 Task 18, L11 item 3): it only enqueues the proposal job and returns, so a slow or failing attendance
/// step can never delay the live lifecycle. It reads the client from the live session row in the caller context (the same filters the lifecycle used), never from a
/// payload field the caller could forge; the job re-checks the client again in its own context.
/// </summary>
public sealed class LiveAttendanceSink(LmsDbContext db, IBackgroundJobs jobs) : ILiveSessionAttendanceSink
{
    public async Task SessionEndedAsync(
        Guid sessionId, Guid classCourseId, DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyList<AttendanceParticipant> participants, CancellationToken ct = default)
    {
        var organizationId = await db.Set<LiveSession>().Where(s => s.Id == sessionId).Select(s => (Guid?)s.OrganizationId).FirstOrDefaultAsync(ct);
        if (organizationId is { } organization)
        {
            jobs.EnqueueAsSystem<LiveAttendanceJob>(job => job.ProposeAsync(organization, sessionId, CancellationToken.None));
        }
    }
}

/// <summary>The jobs of the live proposal: one session after it ended, and the daily sweep for the ones that were missed. Both are idempotent.</summary>
public sealed class LiveAttendanceJob(LiveAttendanceProposer proposer, ILogger<LiveAttendanceJob> logger)
{
    public const string ReconcileJobId = "attendance-live-reconcile";

    /// <summary>Every day at 01:15 in <c>Jobs:TimeZone</c>.</summary>
    public const string ReconcileCron = "15 1 * * *";

    public async Task ProposeAsync(Guid organizationId, Guid liveSessionId, CancellationToken ct)
    {
        var created = await proposer.ProposeAsync(organizationId, liveSessionId, ct);
        logger.LogInformation("Live attendance proposal for session {SessionId}: created {Created}", liveSessionId, created);
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var created = await proposer.ReconcileAllAsync(ct);
        logger.LogInformation("Live attendance reconcile created {Count} proposals", created);
    }
}

/// <summary>Registers the daily reconcile sweep at startup; one id is one schedule.</summary>
public sealed class LiveAttendanceRegistration(IServiceScopeFactory scopes, ILogger<LiveAttendanceRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<LiveAttendanceJob>(
                LiveAttendanceJob.ReconcileJobId, job => job.ReconcileAsync(CancellationToken.None), LiveAttendanceJob.ReconcileCron);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The live attendance reconcile job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
