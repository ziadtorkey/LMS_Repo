using Lms.Domain.Classes;
using Lms.Domain.Organizations;

namespace Lms.Domain.Content;

/// <summary>The upload and read side of assets (FR-CNT-03). Authorisation is the caller's job; this enforces the file rules.</summary>
public interface IAssetService
{
    /// <summary>The effective <c>Files.MaxUploadMb</c> at the ClassCourse's unit, in bytes.</summary>
    Task<long> GetMaxBytesAsync(ClassCourse classCourse, CancellationToken cancellationToken = default);

    /// <summary>The effective <c>Files.MaxUploadMb</c> at the unit (library assets), in bytes.</summary>
    Task<long> GetMaxBytesAsync(OrgUnit unit, CancellationToken cancellationToken = default);

    /// <summary>Stores the file and creates the asset.</summary>
    /// <exception cref="AssetTypeNotAllowedException">The extension or the declared content type is not on the allow-list.</exception>
    /// <exception cref="AssetTooLargeException">The file is over the effective limit.</exception>
    Task<Asset> UploadAsync(
        ClassCourse classCourse, string fileName, string declaredContentType, long sizeBytes, Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Stores the file as a library asset owned by the unit (same rules as <see cref="UploadAsync(ClassCourse,string,string,long,Stream,CancellationToken)"/>).</summary>
    Task<Asset> UploadAsync(
        OrgUnit unit, string fileName, string declaredContentType, long sizeBytes, Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>A short-lived signed URL (inline for previewable types, a download otherwise).</summary>
    Task<AssetUrl> GetUrlAsync(Asset asset, CancellationToken cancellationToken = default);
}

/// <summary>A signed URL and the instant it stops working.</summary>
public sealed record AssetUrl(Uri Url, DateTime ExpiresAtUtc);

/// <summary>The extension or declared content type is not an allowed asset type.</summary>
public sealed class AssetTypeNotAllowedException(string fileName)
    : InvalidOperationException($"'{fileName}' is not an allowed asset type.")
{
    public string FileName { get; } = fileName;
}

/// <summary>The file is larger than the effective upload limit.</summary>
public sealed class AssetTooLargeException(long sizeBytes, long maxBytes)
    : InvalidOperationException($"The file is {sizeBytes} bytes; the maximum is {maxBytes} bytes.")
{
    public long SizeBytes { get; } = sizeBytes;
    public long MaxBytes { get; } = maxBytes;
}

/// <summary>
/// Another feature that lets a caller read an asset because of what references it: a module the caller can see, their own
/// submission, a forum post, a library resource. Registered once per feature; the asset endpoints allow a read when the
/// caller manages the ClassCourse or any reader says yes. A reader answers false for "no", it never throws.
/// </summary>
public interface IAssetReferenceReader
{
    Task<bool> CanReadAsync(Asset asset, CancellationToken cancellationToken = default);
}
