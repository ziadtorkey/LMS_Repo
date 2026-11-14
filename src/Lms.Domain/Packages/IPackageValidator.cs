namespace Lms.Domain.Packages;

/// <summary>A read-only view of the extracted files of one package, relative paths with forward slashes (the validation job's temp area).</summary>
public interface IPackageContent
{
    IReadOnlyList<string> Files { get; }

    bool Exists(string relativePath);

    /// <summary>Opens a file for reading; the caller disposes the stream.</summary>
    Stream OpenRead(string relativePath);
}

/// <summary>What a format validator found: a reject reason (a <c>PackageRejectReasons</c> value), or the facts read from a valid package.</summary>
public sealed record PackageValidationResult(
    string? RejectReason, string? Title = null, string? Version = null, string? MainLibrary = null, string? LaunchPath = null)
{
    public bool Valid => RejectReason is null;

    public static PackageValidationResult Reject(string reason) => new(reason);
}

/// <summary>
/// The format-specific part of validation (SCORM manifest, H5P JSON). Registered by Tasks 10 and 11; the validation job picks the one whose
/// <see cref="Kind"/> is the upload's. Until one is registered a package is rejected as <c>manifest_invalid</c>.
/// </summary>
public interface IPackageValidator
{
    /// <summary>A <c>PackageKinds</c> value.</summary>
    string Kind { get; }

    Task<PackageValidationResult> ValidateAsync(IPackageContent content, CancellationToken ct);
}

/// <summary>
/// The malware scan before a package becomes <c>Ready</c> (Decision 15: a no-op in v1, pluggable). False refuses the package as
/// <c>scan_failed</c>; an exception fails the job and is retried.
/// </summary>
public interface IPackageScanner
{
    Task<bool> IsCleanAsync(IPackageContent content, CancellationToken ct);
}
