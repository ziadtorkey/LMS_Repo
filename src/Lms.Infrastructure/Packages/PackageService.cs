using System.Security.Cryptography;
using Lms.Domain.Classes;
using Lms.Domain.Files;
using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Packages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Packages;

/// <summary>The upload of a package archive: the file store, the <see cref="PackageUpload"/> row and the hand-over to the validation job.</summary>
public sealed class PackageService(LmsDbContext db, IFileStorage files, IPackageValidationScheduler scheduler)
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

        await scheduler.ScheduleAsync(upload.OrganizationId, upload.Id, ct);
        return upload;
    }

    /// <summary>
    /// Soft-deletes the upload and its archive. The extracted files stay in the package store until the purge job of Task 8b removes the
    /// prefix of every deleted upload.
    /// </summary>
    public async Task DeleteAsync(PackageUpload upload, CancellationToken ct)
    {
        var archiveFileId = upload.ArchiveFileId;
        db.Remove(upload);
        await db.SaveChangesAsync(ct);
        // Soft-deleting the file while the tracked upload still points at it would look like a severed required relationship.
        db.Entry(upload).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
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

/// <summary>Until the validation job (Task 8b) registers its own scheduler, an upload simply stays <c>Uploaded</c>.</summary>
internal sealed class NoopPackageValidationScheduler : IPackageValidationScheduler
{
    public Task ScheduleAsync(Guid organizationId, Guid packageId, CancellationToken ct = default) => Task.CompletedTask;
}

public static class PackagesServiceCollectionExtensions
{
    public static IServiceCollection AddPackages(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PackageHostOptions>(o => o.Origin = configuration[$"{PackageHostOptions.Section}:Origin"]);
        services.AddScoped<IPackageStore, MinioPackageStore>();
        services.AddScoped<IPackageValidationScheduler, NoopPackageValidationScheduler>();
        services.AddScoped<PackageService>();
        return services;
    }
}
