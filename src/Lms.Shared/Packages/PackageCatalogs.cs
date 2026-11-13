namespace Lms.Shared.Packages;

/// <summary>Package formats (FR-STD-01 SCORM, FR-STD-02 H5P). Always <c>ModuleKinds.Scorm</c> / <c>ModuleKinds.H5p</c> once attached.</summary>
public static class PackageKinds
{
    public const string Scorm = "Scorm";
    public const string H5p = "H5p";

    public static IReadOnlyList<string> All { get; } = [Scorm, H5p];
}

/// <summary>Where an upload is: stored, being validated by the system job, usable, or refused (<c>RejectReason</c> says why).</summary>
public static class PackageUploadStatuses
{
    public const string Uploaded = "Uploaded";
    public const string Validating = "Validating";
    public const string Ready = "Ready";
    public const string Rejected = "Rejected";

    public static IReadOnlyList<string> All { get; } = [Uploaded, Validating, Ready, Rejected];
}

/// <summary>SCORM versions the runtime supports (single-SCO only in v1).</summary>
public static class ScormVersions
{
    public const string Scorm12 = "Scorm12";
    public const string Scorm2004 = "Scorm2004";

    public static IReadOnlyList<string> All { get; } = [Scorm12, Scorm2004];
}

/// <summary>
/// Why a package was refused (<c>PackageUploadDto.RejectReason</c>, and <c>ProblemDetails.reason</c> of <c>package_invalid</c>).
/// One plain-words label each (<c>Error_package_reason_&lt;value&gt;</c>, ar and en). The values are the corpus expectations of
/// <c>tests/Lms.IntegrationTests/Packages/Corpus</c>.
/// </summary>
public static class PackageRejectReasons
{
    /// <summary>An entry path escapes the package root: <c>..</c>, absolute, drive letter, UNC, backslash.</summary>
    public const string unsafe_path = nameof(unsafe_path);

    /// <summary>The archive has more entries than <c>Packages.MaxEntries</c>.</summary>
    public const string too_many_entries = nameof(too_many_entries);

    /// <summary>The real uncompressed size (counted while streaming, never from headers) exceeds <c>Packages.MaxUncompressedMb</c>.</summary>
    public const string too_large = nameof(too_large);

    /// <summary>An entry or the archive compresses by more than the allowed ratio (a zip bomb).</summary>
    public const string compression_ratio = nameof(compression_ratio);

    /// <summary>An entry is a symbolic link.</summary>
    public const string symlink = nameof(symlink);

    /// <summary>Two entries have the same name (exactly, ignoring case, or after Unicode normalisation).</summary>
    public const string duplicate_name = nameof(duplicate_name);

    /// <summary>A name has control, byte-order-mark, bidirectional-override or NUL characters, or is not valid in its declared encoding.</summary>
    public const string bad_name_encoding = nameof(bad_name_encoding);

    /// <summary>An executable or server-side script file (<c>.exe</c>, <c>.php</c>, <c>.jsp</c>, ...).</summary>
    public const string executable = nameof(executable);

    /// <summary>An entry is encrypted.</summary>
    public const string encrypted = nameof(encrypted);

    /// <summary>The package contains an archive (<c>.zip</c>, <c>.jar</c>, <c>.7z</c>, ...); nested archives are never extracted.</summary>
    public const string nested_archive = nameof(nested_archive);

    /// <summary>The manifest is missing, is not XML, or its launch file is missing or leaves the package.</summary>
    public const string manifest_invalid = nameof(manifest_invalid);

    /// <summary>The XML uses a DOCTYPE or entities, or is larger than the document cap.</summary>
    public const string xml_unsafe = nameof(xml_unsafe);

    /// <summary>More than one SCO, or SCORM 2004 sequencing rules: out of v1.</summary>
    public const string multi_sco = nameof(multi_sco);

    /// <summary>The launch page is a frameset or nests the SCO in a child frame (needs a design the platform does not use in v1).</summary>
    public const string needs_nested_frames = nameof(needs_nested_frames);

    /// <summary>The content needs a looser browser policy than the platform allows (never network access).</summary>
    public const string needs_relaxed_policy = nameof(needs_relaxed_policy);

    /// <summary>The H5P file is missing <c>h5p.json</c>, or its JSON or libraries are invalid or too large.</summary>
    public const string h5p_invalid = nameof(h5p_invalid);

    /// <summary>Validation did not finish within its time limit.</summary>
    public const string validation_timeout = nameof(validation_timeout);

    /// <summary>The client's package storage quota would be exceeded.</summary>
    public const string quota_exceeded = nameof(quota_exceeded);

    /// <summary>The malware scan refused the package.</summary>
    public const string scan_failed = nameof(scan_failed);

    /// <summary>The file is not a readable zip: not an archive, empty, truncated, or without entries.</summary>
    public const string invalid_archive = nameof(invalid_archive);

    /// <summary>A name is unusable: over 255 characters, deeper than 20 folders, a device name, a trailing dot or space, or the central and local headers name the entry differently.</summary>
    public const string bad_name = nameof(bad_name);

    public static IReadOnlyList<string> All { get; } =
    [
        unsafe_path, too_many_entries, too_large, compression_ratio, symlink, duplicate_name, bad_name_encoding, executable,
        encrypted, nested_archive, manifest_invalid, xml_unsafe, multi_sco, needs_nested_frames, needs_relaxed_policy,
        h5p_invalid, validation_timeout, quota_exceeded, scan_failed, invalid_archive, bad_name,
    ];
}

/// <summary>A student's attempt at a package: open, or finished (completed, passed or failed as the server judged).</summary>
public static class PackageAttemptStatuses
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";

    public static IReadOnlyList<string> All { get; } = [InProgress, Completed];
}

/// <summary>Formats of a tracking commit: SCORM data-model keys, or the flattened xAPI result of an H5P content.</summary>
public static class PackageCommitFormats
{
    public const string Scorm = "Scorm";
    public const string Xapi = "Xapi";

    public static IReadOnlyList<string> All { get; } = [Scorm, Xapi];
}
