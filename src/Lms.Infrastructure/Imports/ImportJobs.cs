using System.Text.Json;
using Hangfire;
using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.Imports;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Imports;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Imports;

/// <summary>Everything the two import jobs share: finding the batch, the definition, the stored rows and the report.</summary>
public abstract class ImportJobBase(
    LmsDbContext db, IFileStorage files, ImportDefinitions definitions, IImportMessages messages,
    INotificationPublisher notifications, ImportValidator validator, ILogger logger)
{
    protected LmsDbContext Db => db;
    protected ImportValidator Validator => validator;
    protected ILogger Logger => logger;

    protected async Task<(ImportBatch Batch, IImportDefinition Definition)?> LoadAsync(Guid batchId, CancellationToken ct)
    {
        // Filtered set: a job whose requester lost the assignment finds nothing and does nothing.
        var batch = await db.Set<ImportBatch>().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            logger.LogWarning("Import batch {BatchId} is not visible to the job context; skipped", batchId);
            return null;
        }

        var definition = definitions.Find(batch.Kind);
        if (definition is null)
        {
            logger.LogError("Import batch {BatchId} has unknown kind {Kind}", batchId, batch.Kind);
            return null;
        }

        return (batch, definition);
    }

    /// <summary>Replaces the stored rows of the batch, creates the error report when needed and sets the status.</summary>
    protected async Task StoreResultAsync(
        ImportBatch batch, IImportDefinition definition, ImportValidation result, CancellationToken ct)
    {
        await db.Set<ImportRow>().Where(r => r.BatchId == batch.Id).ExecuteDeleteAsync(ct);
        var errorsByRow = result.Errors.Where(e => e.RowNumber > 0).ToLookup(e => e.RowNumber);
        foreach (var row in result.Rows)
        {
            var errors = errorsByRow[row.RowNumber].Select(e => new StoredError(e.ColumnKey, e.Code, e.Args)).ToList();
            db.Add(new ImportRow(
                batch, row.RowNumber, JsonSerializer.Serialize(row.Values),
                errors.Count == 0 ? null : JsonSerializer.Serialize(errors)));
        }

        Guid? reportId = null;
        if (result.Errors.Count > 0)
        {
            await using var buffer = new MemoryStream();
            ErrorReportWriter.Write(definition, result.Rows, result.Errors, batch.Culture, messages, buffer);
            buffer.Position = 0;
            var name = $"{batch.Kind}-errors-{batch.Id:N}.xlsx";
            reportId = (await files.UploadAsync(new FileUploadRequest(name, string.Empty, buffer.Length, batch.OrgUnitId), buffer, ct)).Id;
        }

        batch.RecordValidation(result.Rows.Count, result.Errors.Count, reportId);
        await db.SaveChangesAsync(ct);
    }

    protected async Task NotifyAsync(ImportBatch batch, CancellationToken ct)
    {
        try
        {
            await notifications.PublishAsync(new NotificationRequest(
                NotificationEvents.ImportsCompleted, batch.OrganizationId, [batch.RequestedBy],
                new Dictionary<string, string>
                {
                    ["Kind"] = batch.Kind,
                    ["Status"] = batch.Status.ToString(),
                    ["RowCount"] = batch.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                NotifyActor: true), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Imports.Completed notification of batch {BatchId} could not be sent", batch.Id);
        }
    }

    /// <summary>Row error as stored in <see cref="ImportRow.ErrorsJson"/>.</summary>
    public sealed record StoredError(string? Column, string Code, IReadOnlyDictionary<string, string>? Args);
}

/// <summary>Reads and validates an uploaded file; the batch ends Valid or Invalid (with an error report).</summary>
public sealed class ValidateImportJob(
    LmsDbContext db, IFileStorage files, ImportDefinitions definitions, IImportMessages messages,
    INotificationPublisher notifications, ImportValidator validator, ILogger<ValidateImportJob> logger)
    : ImportJobBase(db, files, definitions, messages, notifications, validator, logger)
{
    [Queue(JobQueues.Imports)]
    public async Task RunAsync(Guid batchId)
    {
        var loaded = await LoadAsync(batchId, CancellationToken.None);
        if (loaded is null)
        {
            return;
        }

        var (batch, definition) = loaded.Value;

        try
        {
            batch.SetStatus(ImportStatus.Validating);
            await Db.SaveChangesAsync();
            var result = await Validator.ValidateAsync(batch, definition, CancellationToken.None);
            if (result is null)
            {
                batch.MarkFailed("unexpected");
                await Db.SaveChangesAsync();
                return;
            }

            await StoreResultAsync(batch, definition, result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Validation of import batch {BatchId} failed", batchId);
            Db.ChangeTracker.Clear();
            var failed = await Db.Set<ImportBatch>().FirstAsync(b => b.Id == batchId);
            failed.MarkFailed("unexpected");
            await Db.SaveChangesAsync();
            batch = failed;
        }

        await NotifyAsync(batch, CancellationToken.None);
    }
}

/// <summary>
/// Commits a Valid batch in one transaction: validates again (the data may have changed since the preview), then
/// creates everything or nothing. Emails and notifications go out only after the commit.
/// </summary>
public sealed class CommitImportJob(
    LmsDbContext db, IFileStorage files, ImportDefinitions definitions, IImportMessages messages,
    INotificationPublisher notifications, ImportValidator validator, IClock clock, ILogger<CommitImportJob> logger)
    : ImportJobBase(db, files, definitions, messages, notifications, validator, logger)
{
    [Queue(JobQueues.Imports)]
    public async Task RunAsync(Guid batchId)
    {
        var loaded = await LoadAsync(batchId, CancellationToken.None);
        if (loaded is null)
        {
            return;
        }

        var (batch, definition) = loaded.Value;

        // A replay (Hangfire retry, duplicate delivery) must not touch a batch that is not waiting for its commit.
        if (batch.Status != ImportStatus.Committing)
        {
            Logger.LogWarning("Import batch {BatchId} is {Status}, not Committing; commit job skipped", batchId, batch.Status);
            return;
        }

        var afterCommit = new List<Func<CancellationToken, Task>>();
        try
        {
            await using var transaction = await Db.Database.BeginTransactionAsync();
            var result = await Validator.ValidateAsync(batch, definition, CancellationToken.None);
            if (result is null || result.Errors.Count > 0)
            {
                await transaction.RollbackAsync();
                Db.ChangeTracker.Clear();
                await RecordInvalidAsync(batchId, definition, result);
                await NotifyAfterAsync(batchId);
                return;
            }

            var created = await definition.CommitAsync(
                new ImportCommitContext(batch, result.Target, result.UnitsByCode, afterCommit), result.Rows, CancellationToken.None);
            batch.MarkCommitted(created, clock.UtcNow);
            await Db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            // Nothing was committed: the transaction is disposed without Commit, which rolls it back.
            Logger.LogError(ex, "Commit of import batch {BatchId} failed and was rolled back", batchId);
            Db.ChangeTracker.Clear();
            var failed = await Db.Set<ImportBatch>().FirstAsync(b => b.Id == batchId);
            failed.MarkFailed("unexpected");
            await Db.SaveChangesAsync();
            await NotifyAsync(failed, CancellationToken.None);
            return;
        }

        foreach (var action in afterCommit)
        {
            try
            {
                await action(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "A post-commit action of import batch {BatchId} failed", batchId);
            }
        }

        await NotifyAsync(batch, CancellationToken.None);
    }

    private async Task RecordInvalidAsync(Guid batchId, IImportDefinition definition, ImportValidation? result)
    {
        var batch = await Db.Set<ImportBatch>().FirstAsync(b => b.Id == batchId);
        if (result is null)
        {
            batch.MarkFailed("unexpected");
            await Db.SaveChangesAsync();
            return;
        }

        await StoreResultAsync(batch, definition, result, CancellationToken.None);
    }

    private async Task NotifyAfterAsync(Guid batchId) =>
        await NotifyAsync(await Db.Set<ImportBatch>().FirstAsync(b => b.Id == batchId), CancellationToken.None);
}

/// <summary>Recurring clean-up (<c>imports-cleanup</c>): batches that were never committed are dropped after seven days.</summary>
public sealed class ImportCleanupJob(LmsDbContext db, ImportService imports, IClock clock, ILogger<ImportCleanupJob> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    /// <summary>A batch Committing for longer than this lost its worker; the transaction left nothing behind, so it is failed.</summary>
    public static readonly TimeSpan CommitTimeout = TimeSpan.FromHours(2);

    public async Task RunAsync()
    {
        var cutoff = clock.UtcNow - MaxAge;
        await FailStuckCommitsAsync();
        var stale = await db.Set<ImportBatch>()
            .Where(b => b.Status != ImportStatus.Committed && b.Status != ImportStatus.Committing && b.CreatedAt < cutoff)
            .ToListAsync();
        foreach (var batch in stale)
        {
            await imports.RemoveAsync(batch, CancellationToken.None);
        }

        if (stale.Count > 0)
        {
            logger.LogInformation("Removed {Count} stale import batches", stale.Count);
        }
    }

    private async Task FailStuckCommitsAsync()
    {
        var stuckBefore = clock.UtcNow - CommitTimeout;
        var stuck = await db.Set<ImportBatch>()
            .Where(b => b.Status == ImportStatus.Committing && (b.UpdatedAt ?? b.CreatedAt) < stuckBefore)
            .ToListAsync();
        foreach (var batch in stuck)
        {
            batch.MarkFailed("interrupted");
        }

        if (stuck.Count > 0)
        {
            await db.SaveChangesAsync();
            logger.LogWarning("Failed {Count} import batches stuck in Committing", stuck.Count);
        }
    }
}
