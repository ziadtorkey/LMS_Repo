using Lms.Domain.Files;
using Lms.Domain.Packages;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Packages;

/// <summary>Budgets of the validation job (the plan's Decision 9); bound from <c>Packages:Validation</c> so tests can shorten them.</summary>
public sealed class PackageValidationOptions
{
    public const string Section = "Packages:Validation";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a job that finds the gate busy waits before it runs again (it never holds a worker).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How long after the upload a package may keep losing the slot race; past it the package is rejected as <c>validation_timeout</c>.</summary>
    public TimeSpan MaxWaitAge { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Validations that run at once on this server.</summary>
    public int MaxConcurrent { get; set; } = 2;

    /// <summary>Where the temp areas live; empty = the system temp directory.</summary>
    public string? TempRoot { get; set; }
}

/// <summary>
/// The validation of one uploaded package (M4b Task 8b), a system job for one client: the organization id is in the payload and the upload is
/// re-read filtered by it, so a payload for another client's package does nothing. Budgets: a hard timeout (<c>validation_timeout</c>), two
/// validations per server and one per client (<see cref="PackageValidationGate"/>), a disk budget for the temp area, the client's package storage
/// quota (<c>quota_exceeded</c>, checked on the real inspected size before anything is extracted) and cleanup of the temp area in a
/// <c>finally</c> (and by <see cref="PackageTempSweepJob"/> if the process dies). Order: archive rules (<see cref="ZipHardening"/>), quota,
/// extraction, the format validator of the kind, the scanner, then the files go to the package store and the upload becomes <c>Ready</c>.
/// A reject writes nothing to the store. Only reasons and ids are logged, never names or content.
/// </summary>
public sealed class PackageValidationJob(
    LmsDbContext db, IFileStorage files, IPackageStore store, ISettingsResolver settings, IEnumerable<IPackageValidator> validators,
    IPackageScanner scanner, PackageValidationGate gate, IPackageJobs jobs, IOptions<PackageValidationOptions> options,
    ILogger<PackageValidationJob> logger)
{
    public async Task RunAsync(Guid organizationId, Guid packageId, CancellationToken cancellationToken)
    {
        var upload = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == packageId && p.OrganizationId == organizationId, cancellationToken);
        if (upload is null || upload.Status is not (PackageUpload.UploadedStatus or PackageUpload.ValidatingStatus))
        {
            return; // another client's id, deleted, or already decided: nothing to do (a replay changes nothing)
        }

        // Never wait here: a busy gate means "come back later", and the worker is free for other clients and other jobs.
        if (gate.TryEnter(organizationId) is not { } slot)
        {
            if (DateTime.UtcNow - upload.CreatedAt > options.Value.MaxWaitAge)
            {
                // It has lost the slot race for too long: give up with a reason the teacher can see, instead of waiting forever.
                await RejectAsync(upload, PackageRejectReasons.validation_timeout, cancellationToken);
                return;
            }

            // Jitter (up to +50 %) so two jobs that lost together do not collide again at the same moment.
            var delay = options.Value.RetryDelay * (1 + Random.Shared.NextDouble() / 2);
            jobs.ScheduleValidationLater(organizationId, packageId, delay);
            return;
        }

        await using var held = slot;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.Timeout);
        upload.MarkValidating();
        await db.SaveChangesAsync(cancellationToken);
        var parent = options.Value.TempRoot is { Length: > 0 } root ? root : Path.Combine(Path.GetTempPath(), PackageTempSweepJob.Folder);
        PackageTempArea? area = null;
        try
        {
            var limits = await LimitsAsync(upload, cancellationToken);
            area = new PackageTempArea(parent, limits.MaxUncompressedBytes);
            await RejectOrReadyAsync(upload, limits, area, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await RejectAsync(upload, PackageRejectReasons.validation_timeout, cancellationToken);
        }
        catch (ZipHardeningException ex)
        {
            await RejectAsync(upload, ex.Reason, cancellationToken);
        }
        finally
        {
            area?.Dispose();
        }
    }

    private async Task RejectOrReadyAsync(PackageUpload upload, ZipHardeningLimits limits, PackageTempArea area, CancellationToken ct)
    {
        var archivePath = area.NewScratchPath();
        await CopyArchiveAsync(upload.ArchiveFileId, archivePath, ct);
        await using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var inspection = await ZipHardening.InspectAsync(archive, limits, ct);
        if (!inspection.Accepted)
        {
            await RejectAsync(upload, inspection.RejectReason!, CancellationToken.None);
            return;
        }

        var quotaBytes = (await settings.ResolveAsync(SettingKeys.PackagesMaxClientStorageMb, upload.OrgUnitPath, ct)).Value * 1024L * 1024L;
        if (await PackageQuota.UsedBytesAsync(db, upload.OrganizationId, upload.Id, ct) + inspection.TotalBytes > quotaBytes)
        {
            await RejectAsync(upload, PackageRejectReasons.quota_exceeded, CancellationToken.None);
            return;
        }

        await ZipHardening.ExtractAsync(archive, inspection, (entry, content) => area.WriteAsync(entry.Name, content, ct), ct);
        var validator = validators.FirstOrDefault(v => v.Kind == upload.Kind);
        var result = validator is null
            ? PackageValidationResult.Reject(PackageRejectReasons.manifest_invalid)
            : await validator.ValidateAsync(area, ct);
        if (!result.Valid)
        {
            await RejectAsync(upload, result.RejectReason!, CancellationToken.None);
            return;
        }

        if (!await scanner.IsCleanAsync(area, ct))
        {
            await RejectAsync(upload, PackageRejectReasons.scan_failed, CancellationToken.None);
            return;
        }

        var prefix = IPackageStore.PrefixOf(upload.OrganizationId, upload.Id);
        await PutAllAsync(prefix, area, ct);
        // A delete may have come in while the files were being stored (its purge ran before they existed): then nothing may stay behind.
        if (!await db.Set<PackageUpload>().AnyAsync(p => p.Id == upload.Id, CancellationToken.None))
        {
            await store.DeletePrefixAsync(prefix, CancellationToken.None);
            logger.LogInformation("Package {PackageId} was deleted during validation; its files were removed", upload.Id);
            return;
        }

        upload.MarkReady(result.Title ?? upload.FileName, result.Version, result.MainLibrary, prefix, area.WrittenBytes);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task PutAllAsync(string prefix, PackageTempArea area, CancellationToken ct)
    {
        try
        {
            foreach (var path in area.Files)
            {
                await using var stream = area.OpenRead(path);
                await store.PutAsync(prefix, path, PackageContentTypes.For(path).Type, stream, stream.Length, ct);
            }
        }
        catch
        {
            // A half-filled prefix must not stay behind: the job fails (and is retried) with nothing stored.
            await store.DeletePrefixAsync(prefix, CancellationToken.None);
            throw;
        }
    }

    private async Task CopyArchiveAsync(Guid fileId, string path, CancellationToken ct)
    {
        await using var source = await files.OpenReadAsync(fileId, ct);
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, ct);
    }

    private async Task<ZipHardeningLimits> LimitsAsync(PackageUpload upload, CancellationToken ct) => new()
    {
        MaxEntries = (await settings.ResolveAsync(SettingKeys.PackagesMaxEntries, upload.OrgUnitPath, ct)).Value,
        MaxUncompressedBytes = (await settings.ResolveAsync(SettingKeys.PackagesMaxUncompressedMb, upload.OrgUnitPath, ct)).Value * 1024L * 1024L,
    };

    private async Task RejectAsync(PackageUpload upload, string reason, CancellationToken ct)
    {
        upload.Reject(reason);
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Package {PackageId} of organization {OrganizationId} was rejected: {Reason}", upload.Id, upload.OrganizationId, reason);
    }
}
