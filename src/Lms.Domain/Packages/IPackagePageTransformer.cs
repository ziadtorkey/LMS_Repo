namespace Lms.Domain.Packages;

/// <summary>What a page transformer knows about the request: the ids of the verified token (never the token itself).</summary>
public sealed record PackagePageContext(Guid OrganizationId, Guid PackageId, Guid? StudentId, Guid? AttemptId, bool Preview);

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
