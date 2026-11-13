using System.Collections.Concurrent;
using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// The concurrency budget of validation (M4b Decision 9): at most <see cref="PackageValidationOptions.MaxConcurrent"/> validations on this server
/// and one per client, so a bomb from one client cannot starve the job server or another client. Per server by design: the process is the unit
/// that owns the CPU, memory and disk the budget protects.
/// </summary>
public sealed class PackageValidationGate(IOptions<PackageValidationOptions> options)
{
    private readonly SemaphoreSlim _global = new(Math.Max(1, options.Value.MaxConcurrent));
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _perClient = new();

    /// <summary>Waits for a slot of the client and a slot of the server; dispose the result to give both back.</summary>
    public async Task<Slot> EnterAsync(Guid organizationId, CancellationToken ct)
    {
        var client = _perClient.GetOrAdd(organizationId, _ => new SemaphoreSlim(1));
        await client.WaitAsync(ct);
        try
        {
            await _global.WaitAsync(ct);
        }
        catch
        {
            client.Release();
            throw;
        }

        return new Slot(_global, client);
    }

    public sealed class Slot(SemaphoreSlim global, SemaphoreSlim client) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                global.Release();
                client.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>The client's package storage in use: the real extracted bytes of its live uploads (a deleted upload frees its space).</summary>
public static class PackageQuota
{
    public static async Task<long> UsedBytesAsync(LmsDbContext db, Guid organizationId, Guid exceptPackageId, CancellationToken ct) =>
        await db.Set<PackageUpload>()
            .Where(p => p.OrganizationId == organizationId && p.Id != exceptPackageId)
            .SumAsync(p => (long?)p.ExtractedBytes, ct) ?? 0;
}

/// <summary>Decision 15: no malware scan in v1; the hook exists so one can be plugged in without touching the job.</summary>
public sealed class NoOpPackageScanner : IPackageScanner
{
    public Task<bool> IsCleanAsync(IPackageContent content, CancellationToken ct) => Task.FromResult(true);
}

/// <summary>
/// Removes the extracted files of a deleted package (M4b Task 8b). The prefix is computed from the two ids of the payload, so no row is read:
/// nothing here can reach another package. A store failure throws and the job runs again (Hangfire retries), so a purge is never lost.
/// </summary>
public sealed class PackagePurgeJob(IPackageStore store, ILogger<PackagePurgeJob> logger)
{
    public async Task RunAsync(Guid organizationId, Guid packageId, CancellationToken cancellationToken)
    {
        var removed = await store.DeletePrefixAsync(IPackageStore.PrefixOf(organizationId, packageId), cancellationToken);
        logger.LogInformation("Purged {Removed} files of package {PackageId} of organization {OrganizationId}", removed, packageId, organizationId);
    }
}

/// <summary>Recurring clean-up of temp areas that a crashed job left behind (older than <see cref="MaxAge"/>).</summary>
public sealed class PackageTempSweepJob(IOptions<PackageValidationOptions> options, ILogger<PackageTempSweepJob> logger)
{
    public const string Folder = "lms-package-validation";

    public const string JobId = "packages-temp-sweep";

    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(2);

    public Task RunAsync()
    {
        var parent = options.Value.TempRoot is { Length: > 0 } root ? root : Path.Combine(Path.GetTempPath(), Folder);
        if (!Directory.Exists(parent))
        {
            return Task.CompletedTask;
        }

        foreach (var directory in Directory.EnumerateDirectories(parent).Where(d => DateTime.UtcNow - Directory.GetLastWriteTimeUtc(d) > MaxAge))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "A package temp area could not be removed; it is retried at the next sweep");
            }
        }

        return Task.CompletedTask;
    }
}
