using System.Security.Cryptography;
using Lms.Domain.Classes;
using Lms.Domain.Files;
using Lms.Domain.Packages;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Packages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Packages;

/// <summary>The upload of a package archive: the file store, the <see cref="PackageUpload"/> row and the hand-over to the validation job.</summary>
public sealed class PackageService(LmsDbContext db, IFileStorage files, IPackageJobs jobs)
{
    /// <summary>Package extensions by kind: SCORM is a zip, H5P is an .h5p (also a zip).</summary>
    public static bool ExtensionMatches(string kind, string fileName) => kind switch
    {
        PackageKinds.Scorm => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
        PackageKinds.H5p => fileName.EndsWith(".h5p", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>
    /// Stores the archive (a seekable stream of <paramref name="sizeBytes"/> bytes) and creates the upload in <c>Uploaded</c>; the validation
    /// job is scheduled after the row is saved. Throws <see cref="FileTooLargeException"/> over <c>Files.MaxUploadMb</c>.
    /// </summary>
    public async Task<PackageUpload> CreateAsync(
        ClassCourse classCourse, Guid lessonKey, string kind, string fileName, long sizeBytes, Stream content, CancellationToken ct)
    {
        content.Position = 0;
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
        content.Position = 0;
        // Both kinds are zip files; the generic store allow-lists extensions, so the archive is kept as package.zip (the teacher's name stays on the row).
        var stored = await files.UploadAsync(new FileUploadRequest("package.zip", "application/zip", sizeBytes, classCourse.OrgUnitId), content, ct);
        var upload = new PackageUpload(classCourse, lessonKey, kind, fileName, stored.Id, sizeBytes, sha256);
        try
        {
            db.Add(upload);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await files.DeleteAsync(stored.Id, CancellationToken.None);
            throw;
        }

        jobs.ScheduleValidation(upload.OrganizationId, upload.Id);
        return upload;
    }

    /// <summary>
    /// Soft-deletes the upload and its archive. The extracted files stay in the package store until the purge job of Task 8b removes the
    /// prefix of every deleted upload.
    /// </summary>
    public async Task DeleteAsync(PackageUpload upload, CancellationToken ct)
    {
        var (organizationId, packageId, archiveFileId) = (upload.OrganizationId, upload.Id, upload.ArchiveFileId);
        db.Remove(upload);
        await db.SaveChangesAsync(ct);
        // Soft-deleting the file while the tracked upload still points at it would look like a severed required relationship.
        db.Entry(upload).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        jobs.SchedulePurge(organizationId, packageId);
        try
        {
            await files.DeleteAsync(archiveFileId, ct);
        }
        catch (StoredFileNotFoundException)
        {
            // The archive is already gone; nothing left to remove.
        }
    }
}

/// <summary>Enqueues the package jobs as system jobs of one client (the organization id is the payload).</summary>
public sealed class HangfirePackageJobs(IBackgroundJobs jobs) : IPackageJobs
{
    public void ScheduleValidation(Guid organizationId, Guid packageId) =>
        jobs.EnqueueAsSystem<PackageValidationJob>(j => j.RunAsync(organizationId, packageId, CancellationToken.None));

    public void ScheduleValidationLater(Guid organizationId, Guid packageId, TimeSpan delay) =>
        jobs.ScheduleAsSystem<PackageValidationJob>(
            $"package-validation:{packageId:N}", DateTimeOffset.UtcNow + delay, j => j.RunAsync(organizationId, packageId, CancellationToken.None));

    public void SchedulePurge(Guid organizationId, Guid packageId) =>
        jobs.EnqueueAsSystem<PackagePurgeJob>(j => j.RunAsync(organizationId, packageId, CancellationToken.None));
}

/// <summary>Registers the recurring sweep of abandoned temp areas.</summary>
internal sealed class PackageTempSweepRegistrar(IServiceScopeFactory scopes, ILogger<PackageTempSweepRegistrar> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>()
                .AddOrUpdateRecurring<PackageTempSweepJob>(PackageTempSweepJob.JobId, j => j.RunAsync(), "*/30 * * * *");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The package temp sweep could not be registered; it is retried at the next start");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class PackagesServiceCollectionExtensions
{
    public static IServiceCollection AddPackages(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PackageHostOptions>(o =>
        {
            o.Origin = configuration[$"{PackageHostOptions.Section}:Origin"];
            o.AppOrigin = configuration[$"{PackageHostOptions.Section}:AppOrigin"] ?? configuration["App:PublicBaseUrl"];
            o.RevocationCacheSeconds = configuration.GetValue($"{PackageHostOptions.Section}:RevocationCacheSeconds", 30);
        });
        services.AddScoped<IPackageRevocations, PackageRevocationWriter>();
        services.AddScoped<IPackageStore, MinioPackageStore>();
        services.Configure<PackageValidationOptions>(o =>
        {
            var section = configuration.GetSection(PackageValidationOptions.Section);
            if (TimeSpan.TryParse(section["Timeout"], System.Globalization.CultureInfo.InvariantCulture, out var timeout))
            {
                o.Timeout = timeout;
            }

            if (int.TryParse(section["MaxConcurrent"], out var concurrent) && concurrent > 0)
            {
                o.MaxConcurrent = concurrent;
            }

            o.TempRoot = section["TempRoot"];
        });
        services.AddSingleton<PackageValidationGate>();
        services.AddScoped<IPackageScanner, NoOpPackageScanner>();
        services.AddScoped<IPackageValidator, Scorm.ScormManifestValidator>();
        services.AddScoped<IPackageValidator, H5p.H5pValidator>();
        services.AddSingleton<Scorm.ScormPreludeAsset>();
        services.AddSingleton<IPackageHostAsset>(sp => sp.GetRequiredService<Scorm.ScormPreludeAsset>());
        services.AddScoped<IPackagePageTransformer, Scorm.ScormPageTransformer>();
        services.AddScoped<IPackageJobs, HangfirePackageJobs>();
        services.AddScoped<PackageValidationJob>();
        services.AddScoped<PackagePurgeJob>();
        services.AddScoped<PackageTempSweepJob>();
        services.AddHostedService<PackageTempSweepRegistrar>();
        services.AddScoped<PackageService>();
        services.AddScoped<Attempts.PackageAttemptService>();
        services.AddSingleton<IPackageTracker, Attempts.PlaceholderPackageTracker>();
        services.AddSingleton<IPackageTracker, Scorm.ScormTracker>(); // the last registration of a kind wins
        return services;
    }
}
