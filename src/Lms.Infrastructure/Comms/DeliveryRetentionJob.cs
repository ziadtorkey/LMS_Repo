using Lms.Domain.Abstractions;
using Lms.Domain.Comms;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Comms;

/// <summary>
/// Daily clean-up (system job): delivery rows that are finished (sent, failed, skipped, expired) and older than the client's
/// <c>Comms.DeliveryRetentionDays</c> (90 by default) are deleted. A row still waiting is never deleted. Rows hold no message text, so retention is
/// about volume. Suppressions are kept until an admin lifts them.
/// </summary>
public sealed class DeliveryRetentionJob(LmsDbContext db, CommsClientSettings settings, IClock clock, ILogger<DeliveryRetentionJob> logger)
{
    public const string RecurringJobId = "comms-retention";

    /// <summary>Every day at 03:20 (read in <c>Jobs:TimeZone</c>).</summary>
    public const string Cron = "20 3 * * *";

    public Task RunAsync(CancellationToken ct) => RunAtAsync(clock.UtcNow, null, ct);

    public async Task RunAtAsync(DateTime now, Guid? only, CancellationToken ct)
    {
        var organizationIds = await db.Set<Organization>().Where(o => only == null || o.Id == only).Select(o => o.Id).ToListAsync(ct);
        foreach (var organizationId in organizationIds)
        {
            try
            {
                var days = (await settings.GetAsync(organizationId, ct)).RetentionDays;
                var cutoff = now.AddDays(-Math.Max(1, days));
                await db.Set<DeliveryOutbox>()
                    .Where(r => r.OrganizationId == organizationId && r.CreatedAtUtc < cutoff
                                && (r.Status == DeliveryStates.Sent || r.Status == DeliveryStates.Failed
                                    || r.Status == DeliveryStates.Skipped || r.Status == DeliveryStates.Expired))
                    .ExecuteDeleteAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Delivery retention failed for client {OrganizationId}", organizationId);
            }
        }
    }
}
