using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio.DataModel.Args;

namespace Lms.Infrastructure.Files;

public sealed class MinioFileStorage(
    LmsDbContext db,
    ISettingsResolver settings,
    MinioClients clients,
    IOptions<StorageOptions> options,
    IClock clock) : IFileStorage
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public async Task<StoredFile> UploadAsync(
        FileUploadRequest request, Stream content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(request.FileName);
        if (string.IsNullOrEmpty(extension) || !options.Value.AllowedExtensionSet().Contains(extension))
        {
            throw new FileTypeNotAllowedException(extension);
        }

        // Through the filtered set: the caller can only upload into units inside its own scope.
        var unit = await db.OrgUnits.SingleOrDefaultAsync(x => x.Id == request.OrgUnitId, cancellationToken)
            ?? throw new UploadTargetNotFoundException(request.OrgUnitId);

        var maxMb = (await settings.ResolveAsync(SettingKeys.FilesMaxUploadMb, unit.Path, cancellationToken)).Value;
        var maxBytes = maxMb * 1024L * 1024L;
        if (request.SizeBytes > maxBytes)
        {
            throw new FileTooLargeException(request.SizeBytes, maxBytes);
        }

        // The client-declared content type is ignored: the type comes from the allow-listed extension, and downloads are
        // forced to attachments, so an uploaded file can never be rendered as a page from the storage origin.
        var contentType = FileContentTypes.ForExtension(extension);
        var file = new StoredFile(
            unit.OrganizationId, unit.Id, unit.Path, request.FileName, contentType, request.SizeBytes, clock.UtcNow);
        var bucket = BucketFor(unit.OrganizationId);

        await clients.EnsureBucketAsync(bucket, cancellationToken);
        await clients.Client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(bucket)
                .WithObject(file.ObjectKey)
                .WithStreamData(content)
                .WithObjectSize(request.SizeBytes)
                .WithContentType(contentType),
            cancellationToken);

        try
        {
            db.Add(file);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Do not leave an object without a record behind.
            await clients.Client.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(bucket).WithObject(file.ObjectKey), CancellationToken.None);
            throw;
        }

        return file;
    }

    public Task<Uri> GetDownloadUrlAsync(Guid fileId, TimeSpan? ttl = null, CancellationToken cancellationToken = default) =>
        PresignAsync(fileId, ttl, FileContentTypes.AttachmentDisposition, cancellationToken);

    public Task<Uri> GetViewUrlAsync(Guid fileId, TimeSpan? ttl = null, CancellationToken cancellationToken = default) =>
        PresignAsync(fileId, ttl, FileContentTypes.InlineDisposition, cancellationToken);

    private async Task<Uri> PresignAsync(
        Guid fileId, TimeSpan? ttl, Func<string, string, string> disposition, CancellationToken cancellationToken)
    {
        // Filtered set: other clients, sibling units and deleted files all look like "not found".
        var file = await db.Set<StoredFile>().SingleOrDefaultAsync(x => x.Id == fileId, cancellationToken)
            ?? throw new StoredFileNotFoundException(fileId);

        var seconds = (int)Math.Max(1, (ttl ?? DefaultTtl).TotalSeconds);
        var url = await clients.Presigner.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(BucketFor(file.OrganizationId))
                .WithObject(file.ObjectKey)
                .WithExpiry(seconds)
                // Signed into the url (storage rejects Content-Disposition as an upload header): the response is
                // typed by the allow-listed extension, never by what the client claimed.
                .WithHeaders(new Dictionary<string, string>
                {
                    ["response-content-disposition"] = disposition(file.FileName, Path.GetExtension(file.FileName)),
                    ["response-content-type"] = file.ContentType,
                }));
        return new Uri(url);
    }

    public async Task<Stream> OpenReadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await db.Set<StoredFile>().SingleOrDefaultAsync(x => x.Id == fileId, cancellationToken)
            ?? throw new StoredFileNotFoundException(fileId);

        // Small objects stay in memory; large ones are streamed to a temp file (never a whole archive on the heap).
        Stream target = file.SizeBytes <= options.Value.InMemoryReadLimitBytes ? new MemoryStream() : CreateSpoolFile();
        try
        {
            await clients.Client.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(BucketFor(file.OrganizationId))
                    .WithObject(file.ObjectKey)
                    .WithCallbackStream((stream, ct) => stream.CopyToAsync(target, ct)),
                cancellationToken);
            target.Position = 0;
            return target;
        }
        catch
        {
            await target.DisposeAsync(); // a spool file goes with it (DeleteOnClose)
            throw;
        }
    }

    /// <summary>A temp file that is removed when the stream is closed, read back sequentially after the download.</summary>
    private FileStream CreateSpoolFile()
    {
        var directory = options.Value.SpoolDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);
        return new FileStream(
            Path.Combine(directory, $"lms-read-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await db.Set<StoredFile>().SingleOrDefaultAsync(x => x.Id == fileId, cancellationToken)
            ?? throw new StoredFileNotFoundException(fileId);

        db.Remove(file); // soft delete; the object is removed later by a retention job
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string BucketFor(Guid organizationId) => $"lms-{organizationId:N}";
}
