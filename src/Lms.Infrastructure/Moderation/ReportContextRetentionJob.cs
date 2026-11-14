using Lms.Domain.Abstractions;
using Lms.Domain.Moderation;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Messaging;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Moderation;

/// <summary>
/// The frozen context of a report (names, text, attachment names) is kept <c>Moderation.ReportContextRetentionDays</c> days after the report was resolved (D66, D71, L23), then
/// cleared; the report row and its action stay. <b>An open or in-review report holds its context past the window.</b> Per client in the system context.
/// </summary>
public sealed class ReportContextRetentionJob(
    LmsDbContext db, RetentionSupport support, IClock clock, JobSettings jobSettings, ILogger<ReportContextRetentionJob> logger)
{
    public const string RecurringJobId = "moderation-report-context-retention";
    public const string Cron = "50 2 * * *";

    public async Task RunAsync(DateOnly? asOf, CancellationToken ct)
    {
        var day = asOf ?? jobSettings.Today(clock);
        foreach (var client in await support.ClientsAsync(ct))
        {
            try
            {
                var cutoff = RetentionSupport.StartOfDayUtc(day.AddDays(-await support.ReportContextDaysAsync(client, ct)));
                var reports = await db.Set<MessageReport>()
                    .Where(r => r.OrganizationId == client.OrganizationId && (r.Status == MessageReport.DismissedStatus || r.Status == MessageReport.ActionTakenStatus)
                                && r.ResolvedAtUtc < cutoff && r.ContextJson != MessageReport.PurgedContext)
                    .ToListAsync(ct);
                reports.ForEach(r => r.PurgeContext());
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Report context retention failed for client {OrganizationId}", client.OrganizationId);
            }
        }
    }
}
