using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.Imports;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Imports;

public enum CommitStart { Started, NotFound, InProgress, AlreadyCommitted, NotValid }

/// <summary>Upload, commit start and discard of import batches. The heavy work runs in the jobs.</summary>
public sealed class ImportService(
    LmsDbContext db, IFileStorage files, IBackgroundJobs jobs, ICurrentContext context)
{
    /// <summary>Stores the file at the target unit, creates the batch (Uploaded) and enqueues the validation job.</summary>
    public async Task<ImportBatch> UploadAsync(
        IImportDefinition definition, OrgUnit target, string fileName, Stream content, long sizeBytes, string culture,
        CancellationToken cancellationToken)
    {
        var stored = await files.UploadAsync(new FileUploadRequest(fileName, string.Empty, sizeBytes, target.Id), content, cancellationToken);
        var batch = new ImportBatch(target, definition.Kind, fileName, stored.Id, context.UserId ?? Guid.Empty, culture);
        db.Add(batch);
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<ValidateImportJob>(j => j.RunAsync(batch.Id));
        return batch;
    }

    /// <summary>
    /// Valid to Committing in one atomic statement, so two clicks (or two callers) start exactly one commit job.
    /// </summary>
    public async Task<CommitStart> StartCommitAsync(string kind, Guid batchId, CancellationToken cancellationToken)
    {
        var changed = await db.Set<ImportBatch>()
            .Where(b => b.Id == batchId && b.Kind == kind && b.Status == ImportStatus.Valid)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, ImportStatus.Committing), cancellationToken);
        if (changed == 1)
        {
            jobs.Enqueue<CommitImportJob>(j => j.RunAsync(batchId));
            return CommitStart.Started;
        }

        var status = await db.Set<ImportBatch>().Where(b => b.Id == batchId && b.Kind == kind)
            .Select(b => (ImportStatus?)b.Status).FirstOrDefaultAsync(cancellationToken);
        return status switch
        {
            null => CommitStart.NotFound,
            ImportStatus.Committing => CommitStart.InProgress,
            ImportStatus.Committed => CommitStart.AlreadyCommitted,
            _ => CommitStart.NotValid,
        };
    }

    /// <summary>Discards an uncommitted batch (rows, files, soft-deleted batch). False when it is committing or committed.</summary>
    public async Task<bool> DiscardAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Status is ImportStatus.Committing or ImportStatus.Committed)
        {
            return false;
        }

        await RemoveAsync(batch, cancellationToken);
        return true;
    }

    /// <summary>Deletes the rows and the stored files and soft-deletes the batch.</summary>
    public async Task RemoveAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        await db.Set<ImportRow>().Where(r => r.BatchId == batch.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (var fileId in new[] { (Guid?)batch.SourceFileId, batch.ErrorReportFileId }.OfType<Guid>())
        {
            try
            {
                await files.DeleteAsync(fileId, cancellationToken);
            }
            catch (StoredFileNotFoundException)
            {
                // already gone
            }
        }

        db.Remove(batch);
        await db.SaveChangesAsync(cancellationToken);
    }
}
