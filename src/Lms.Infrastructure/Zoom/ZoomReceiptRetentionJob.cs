using Lms.Domain.Abstractions;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Removes webhook receipts older than 30 days (D54). Receipts only deduplicate re-deliveries, and Zoom retries within hours, so a month is
/// ample; the table holds an opaque event key and a time, nothing of any client. Daily, in the platform context.
/// </summary>
public sealed class ZoomReceiptRetentionJob(LmsDbContext db, IClock clock)
{
    public const string RecurringJobId = "zoom-receipt-retention";

    /// <summary>Every day at 03:30 (server time zone of the job server).</summary>
    public const string Cron = "30 3 * * *";

    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>Returns the number of receipts deleted.</summary>
    public Task<int> RunAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - Retention;
        return db.Set<ZoomWebhookReceipt>().Where(r => r.ReceivedAtUtc < cutoff).ExecuteDeleteAsync(ct);
    }
}
