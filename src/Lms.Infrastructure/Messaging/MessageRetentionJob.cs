using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Files;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Messaging;

/// <summary>
/// Messaging retention, per client in the system context (D66, D71, L23; background job site 8). Messages are kept the school year plus
/// <c>Messaging.RetentionYearsAfterClose</c> years: a message sent on or before the end of a <b>closed</b> year whose end date is at least that many years ago is purged with its
/// attachments and stored files. A message with an open or in-review report is held until the report is resolved. The same run purges pending uploads nobody attached for 24 hours.
/// The cut-off day is today in <c>Jobs:TimeZone</c> (the client calendar of M5 replaces it when it lands).
/// </summary>
public sealed class MessageRetentionJob(
    LmsDbContext db, RetentionSupport support, IFileStorage storage, IClock clock, JobSettings jobSettings, ILogger<MessageRetentionJob> logger)
{
    public const string RecurringJobId = "messaging-retention";
    public const string Cron = "30 2 * * *";
    private const int BatchSize = 500;

    public async Task RunAsync(DateOnly? asOf, CancellationToken ct)
    {
        var day = asOf ?? jobSettings.Today(clock);
        foreach (var client in await support.ClientsAsync(ct))
        {
            try
            {
                await PurgeOldMessagesAsync(client, day, ct);
                await PurgeOrphanUploadsAsync(client, day, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear(); // one client failing must not stop the others; the job is idempotent and tries again tomorrow
                logger.LogError(ex, "Messaging retention failed for client {OrganizationId}", client.OrganizationId);
            }
        }
    }

    private async Task PurgeOldMessagesAsync(RetentionClient client, DateOnly day, CancellationToken ct)
    {
        var years = await support.YearsAfterCloseAsync(client, ct);
        var latestEnd = await db.Set<AcademicYear>()
            .Where(y => y.OrganizationId == client.OrganizationId && y.Status == AcademicYearStatus.Closed)
            .Select(y => y.EndDate).ToListAsync(ct);
        var qualifying = latestEnd.Where(end => end.AddYears(years) <= day).ToList();
        if (qualifying.Count == 0)
        {
            return;
        }

        var cutoff = RetentionSupport.StartOfDayUtc(qualifying.Max().AddDays(1)); // everything sent on or before the last day of that year
        while (true)
        {
            var batch = await db.Set<Message>()
                .Where(m => m.OrganizationId == client.OrganizationId && m.SentAtUtc < cutoff
                            && !db.Set<MessageReport>().Any(r => r.MessageId == m.Id && (r.Status == MessageReport.OpenStatus || r.Status == MessageReport.InReviewStatus)))
                .OrderBy(m => m.Id).Take(BatchSize).Select(m => m.Id).ToListAsync(ct);
            if (batch.Count == 0)
            {
                return;
            }

            await DeleteAttachmentsAsync(db.Set<MessageAttachment>().Where(a => a.MessageId != null && batch.Contains(a.MessageId.Value)), ct);
            await db.Set<Message>().Where(m => batch.Contains(m.Id)).ExecuteDeleteAsync(ct);
        }
    }

    private async Task PurgeOrphanUploadsAsync(RetentionClient client, DateOnly day, CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddHours(-MessageAttachmentGuard.PendingLifetime.TotalHours);
        _ = day;
        await DeleteAttachmentsAsync(db.Set<MessageAttachment>().Where(a => a.OrganizationId == client.OrganizationId && a.MessageId == null && a.CreatedAtUtc < cutoff), ct);
    }

    /// <summary>The stored files first (a failure keeps the row, so the next run finds it again), then the rows.</summary>
    private async Task DeleteAttachmentsAsync(IQueryable<MessageAttachment> attachments, CancellationToken ct)
    {
        var rows = await attachments.Select(a => new { a.Id, a.FileId }).ToListAsync(ct);
        var done = new List<Guid>();
        foreach (var row in rows)
        {
            try
            {
                await storage.DeleteAsync(row.FileId, ct);
            }
            catch (StoredFileNotFoundException)
            {
                // already gone: nothing left to remove
            }

            done.Add(row.Id);
        }

        if (done.Count > 0)
        {
            await db.Set<MessageAttachment>().Where(a => done.Contains(a.Id)).ExecuteDeleteAsync(ct);
        }
    }
}
