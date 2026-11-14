using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Messaging;

/// <summary>
/// The original text of a message a moderator removed is kept <c>Messaging.RemovedOriginalRetentionDays</c> days after the removal (D66, D71, L23), then cleared; the tombstone stays.
/// The removal day is the day the report was resolved with a removal. Per client in the system context.
/// </summary>
public sealed class RemovedOriginalRetentionJob(
    LmsDbContext db, RetentionSupport support, IClock clock, JobSettings jobSettings, ILogger<RemovedOriginalRetentionJob> logger)
{
    public const string RecurringJobId = "messaging-removed-original-retention";
    public const string Cron = "40 2 * * *";

    public async Task RunAsync(DateOnly? asOf, CancellationToken ct)
    {
        var day = asOf ?? jobSettings.Today(clock);
        foreach (var client in await support.ClientsAsync(ct))
        {
            try
            {
                var cutoff = RetentionSupport.StartOfDayUtc(day.AddDays(-await support.RemovedOriginalDaysAsync(client, ct)));
                var messageIds = await db.Set<MessageReport>()
                    .Where(r => r.OrganizationId == client.OrganizationId && r.Status == MessageReport.ActionTakenStatus && r.ResolvedAtUtc < cutoff
                                && (r.Action == ModerationActions.Remove || r.Action == ModerationActions.RemoveAndRestrict))
                    .Select(r => r.MessageId).Distinct().ToListAsync(ct);
                foreach (var chunk in messageIds.Chunk(500))
                {
                    var messages = await db.Set<Message>().Where(m => chunk.Contains(m.Id) && m.State == Message.RemovedState && m.Body != string.Empty).ToListAsync(ct);
                    messages.ForEach(m => m.PurgeOriginal());
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Removed-original retention failed for client {OrganizationId}", client.OrganizationId);
            }
        }
    }
}
