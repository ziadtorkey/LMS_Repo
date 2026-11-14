using Lms.Domain.Timetable;
using Lms.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Attendance;

/// <summary>The jobs of session generation: the daily run for every client and the run after a timetable publish (M5 Task 16). Both are idempotent.</summary>
public sealed class AttendanceGenerationJob(AttendanceSessionGenerator generator, ILogger<AttendanceGenerationJob> logger)
{
    public const string RecurringJobId = "attendance-session-generation";

    /// <summary>Every day at 00:30 in <c>Jobs:TimeZone</c>.</summary>
    public const string Cron = "30 0 * * *";

    public async Task RunAsync(CancellationToken ct)
    {
        var created = await generator.GenerateAllAsync(ct);
        logger.LogInformation("Attendance generation created {Count} sessions", created);
    }

    public async Task RunForClassAsync(Guid organizationId, Guid classId, Guid termId, CancellationToken ct)
    {
        var created = await generator.GenerateForOrganizationAsync(organizationId, classId, termId, ct);
        logger.LogInformation("Attendance generation after a publish created {Count} sessions", created);
    }
}

/// <summary>Queues the run after a timetable is published (replaces the no-op default of Task 14).</summary>
public sealed class AttendanceGenerationQueue(IBackgroundJobs jobs) : IAttendanceGenerationQueue
{
    public Task<bool> QueueAsync(Guid organizationId, Guid classId, Guid termId, CancellationToken cancellationToken)
    {
        jobs.EnqueueAsSystem<AttendanceGenerationJob>(job => job.RunForClassAsync(organizationId, classId, termId, CancellationToken.None));
        return Task.FromResult(true);
    }
}

/// <summary>Registers the daily recurring job at startup; one id is one schedule.</summary>
public sealed class AttendanceGenerationRegistration(IServiceScopeFactory scopes, ILogger<AttendanceGenerationRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<AttendanceGenerationJob>(
                AttendanceGenerationJob.RecurringJobId, job => job.RunAsync(CancellationToken.None), AttendanceGenerationJob.Cron);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The attendance generation recurring job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
