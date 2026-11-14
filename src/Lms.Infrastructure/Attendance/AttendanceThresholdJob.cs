using Lms.Domain.Notifications;
using Lms.Infrastructure.Jobs;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Attendance;

/// <summary>The daily absence threshold job (M5 Task 20): runs the check for every client and tells the Counselors of each student's unit, once per crossing.</summary>
public sealed class AttendanceThresholdJob(AttendanceThresholdRunner runner, INotificationPublisher publisher, ILogger<AttendanceThresholdJob> logger)
{
    public const string RecurringJobId = "attendance-threshold";

    /// <summary>Every day at 01:00 in <c>Jobs:TimeZone</c>, after the generation job.</summary>
    public const string Cron = "0 1 * * *";

    public async Task RunAsync(CancellationToken ct)
    {
        var alerts = await runner.RunAllAsync(ct);
        await NotifyAsync(alerts, ct);
        logger.LogInformation("The attendance threshold check opened {Count} alerts", alerts.Count);
    }

    public async Task NotifyAsync(IReadOnlyList<NewAttendanceAlert> alerts, CancellationToken ct)
    {
        foreach (var alert in alerts)
        {
            try
            {
                // Default recipients: the Counselors at or below the student's unit (the event's default role). Names and the count only: no marks, no notes.
                await publisher.PublishAsync(new NotificationRequest(
                    NotificationEvents.AttendanceThresholdCrossed, alert.OrganizationId, [],
                    new Dictionary<string, string>
                    {
                        ["StudentNameAr"] = alert.NameAr, ["StudentNameEn"] = alert.NameEn,
                        ["Absences"] = alert.Absences.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["WindowDays"] = alert.WindowDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    OrgUnitPath: alert.OrgUnitPath, DeepLink: "/attendance/alerts"), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification {Event} could not be published", NotificationEvents.AttendanceThresholdCrossed);
            }
        }
    }
}

/// <summary>Registers the daily recurring job at startup; one id is one schedule.</summary>
public sealed class AttendanceThresholdRegistration(IServiceScopeFactory scopes, ILogger<AttendanceThresholdRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<AttendanceThresholdJob>(
                AttendanceThresholdJob.RecurringJobId, job => job.RunAsync(CancellationToken.None), AttendanceThresholdJob.Cron);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The attendance threshold recurring job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
