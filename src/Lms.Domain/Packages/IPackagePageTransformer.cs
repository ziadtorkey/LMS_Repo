namespace Lms.Domain.Packages;

/// <summary>
/// What a page transformer knows about the request: the ids of the verified token and <see cref="PathPrefix"/> (<c>/p/{token}/</c>), the
/// absolute prefix of every file of this launch, for URLs the transformer injects. Never log the prefix: it carries the token.
/// </summary>
public sealed record PackagePageContext(Guid OrganizationId, Guid PackageId, Guid? StudentId, Guid? AttemptId, bool Preview, string PathPrefix);

/// <summary>
/// A file that comes from the application, not from the package store, served under the reserved path <c>_lms/{Name}</c> of the content
/// host with the same sandbox headers (for example the SCORM prelude script). <see cref="Name"/> includes the content hash.
/// </summary>
public interface IPackageHostAsset
{
    string Name { get; }
    byte[] Content { get; }
    string ContentType { get; }
    string Hash { get; }
}

/// <summary>
/// A seam of the package content host: HTML files of a package of <see cref="Kind"/> pass through it before they are sent (for example the
/// SCORM prelude injection). Only HTML of at most <see cref="MaxBytes"/> goes through it; everything else is served untouched.
/// </summary>
public interface IPackagePageTransformer
{
    /// <summary>The package kind this transformer serves (a <c>PackageKinds</c> value).</summary>
    string Kind { get; }

    Task<byte[]> TransformAsync(PackagePageContext context, string relativePath, byte[] html, CancellationToken ct);

    /// <summary>The largest HTML file, in bytes, the host reads into memory for a transformer.</summary>
    const long MaxBytes = 8 * 1024 * 1024;
}
