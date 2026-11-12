using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Files;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Content;

namespace Lms.Infrastructure.Content;

/// <summary>
/// Assets on top of the file store (M0): the allow-list is <see cref="AssetTypes.All"/> (narrower than the generic file
/// endpoint: no archives, no SVG or HTML), the size limit is the unit's <c>Files.MaxUploadMb</c>, and bytes are streamed
/// through the API (no presigned direct upload in v1).
/// </summary>
public sealed class AssetService(
    LmsDbContext db, IFileStorage storage, ISettingsResolver settings, IClock clock) : IAssetService
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(10);

    // Content types browsers really send for an allowed extension, besides the canonical one in AssetTypes.
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = ["audio/mp3"],
        [".m4a"] = ["audio/x-m4a", "audio/m4a", "audio/aac"],
        [".wav"] = ["audio/x-wav", "audio/wave", "audio/vnd.wave"],
        [".jpg"] = ["image/jpg", "image/pjpeg"],
        [".jpeg"] = ["image/jpg", "image/pjpeg"],
        [".ogg"] = ["application/ogg"],
    };

    public async Task<long> GetMaxBytesAsync(ClassCourse classCourse, CancellationToken cancellationToken = default) =>
        (await settings.ResolveAsync(SettingKeys.FilesMaxUploadMb, classCourse.OrgUnitPath, cancellationToken)).Value
        * 1024L * 1024L;

    public async Task<Asset> UploadAsync(
        ClassCourse classCourse, string fileName, string declaredContentType, long sizeBytes, Stream content,
        CancellationToken cancellationToken = default)
    {
        var type = AssetTypes.ForFileName(fileName);
        if (type is null || !ContentTypeMatches(type, declaredContentType))
        {
            throw new AssetTypeNotAllowedException(fileName);
        }

        var maxBytes = await GetMaxBytesAsync(classCourse, cancellationToken);
        if (sizeBytes > maxBytes)
        {
            throw new AssetTooLargeException(sizeBytes, maxBytes);
        }

        // The bytes must look like the type the name claims (a .png holding HTML is refused). Needs a seekable stream.
        if (!content.CanSeek)
        {
            throw new ArgumentException("The upload stream must be seekable.", nameof(content));
        }

        var header = new byte[AssetSignatures.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        content.Position = 0;
        if (!AssetSignatures.Matches(type.Extension, header.AsSpan(0, read)))
        {
            throw new AssetTypeNotAllowedException(fileName);
        }

        StoredFile stored;
        try
        {
            stored = await storage.UploadAsync(
                new FileUploadRequest(fileName, declaredContentType, sizeBytes, classCourse.OrgUnitId), content, cancellationToken);
        }
        catch (FileTooLargeException ex)
        {
            throw new AssetTooLargeException(ex.SizeBytes, ex.MaxBytes);
        }
        catch (FileTypeNotAllowedException)
        {
            throw new AssetTypeNotAllowedException(fileName);
        }

        var asset = new Asset(classCourse, stored.Id, type.Kind, fileName, stored.ContentType, sizeBytes);
        try
        {
            db.Add(asset);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Do not leave a stored file without an asset behind.
            await storage.DeleteAsync(stored.Id, CancellationToken.None);
            throw;
        }

        return asset;
    }

    public async Task<AssetUrl> GetUrlAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        var previewable = AssetTypes.ForFileName(asset.FileName)?.Previewable == true;
        var url = previewable
            ? await storage.GetViewUrlAsync(asset.FileId, UrlLifetime, cancellationToken)
            : await storage.GetDownloadUrlAsync(asset.FileId, UrlLifetime, cancellationToken);
        return new AssetUrl(url, clock.UtcNow.Add(UrlLifetime));
    }

    private static bool ContentTypeMatches(AssetTypeDto type, string declared)
    {
        var mediaType = declared.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, type.ContentType, StringComparison.OrdinalIgnoreCase)
            || (Aliases.TryGetValue(type.Extension, out var aliases)
                && aliases.Contains(mediaType, StringComparer.OrdinalIgnoreCase));
    }
}
