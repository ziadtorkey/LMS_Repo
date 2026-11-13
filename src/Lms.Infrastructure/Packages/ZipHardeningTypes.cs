namespace Lms.Infrastructure.Packages;

/// <summary>The caps of the hardening reader (the plan's Decision 9 defaults; entries and uncompressed size come from settings).</summary>
public sealed record ZipHardeningLimits
{
    public int MaxEntries { get; init; } = 10_000;

    public long MaxUncompressedBytes { get; init; } = 1024L * 1024 * 1024;

    /// <summary>Largest uncompressed/compressed ratio of one entry (checked once the entry passes <see cref="RatioFloorBytes"/>).</summary>
    public int MaxEntryRatio { get; init; } = 100;

    /// <summary>Largest ratio of all real bytes to the archive size (checked past <see cref="TotalRatioFloorBytes"/>).</summary>
    public int MaxTotalRatio { get; init; } = 50;

    public long RatioFloorBytes { get; init; } = 1024 * 1024;

    public long TotalRatioFloorBytes { get; init; } = 8L * 1024 * 1024;

    public int MaxSegmentLength { get; init; } = 255;

    public int MaxDepth { get; init; } = 20;
}

/// <summary>One safe entry of an inspected archive. <see cref="Length"/> is the real uncompressed size counted while reading.</summary>
public sealed record ZipEntry(string Name, long Length, bool IsDirectory, long DataOffset, long CompressedSize, ushort Method);

/// <summary>
/// The outcome of <see cref="ZipHardening.InspectAsync"/>: a <c>PackageRejectReasons</c> value with the entry that broke the rule, or the
/// safe entries. Nothing is written anywhere by an inspection.
/// </summary>
public sealed record ZipInspection(string? RejectReason, string? Detail, IReadOnlyList<ZipEntry> Entries, long TotalBytes)
{
    public bool Accepted => RejectReason is null;

    public static ZipInspection Rejected(string reason, string? detail = null) => new(reason, detail, [], 0);
}

/// <summary>An archive turned out to break a rule while it was read; <see cref="Reason"/> is a <c>PackageRejectReasons</c> value.</summary>
public sealed class ZipHardeningException(string reason, string? detail = null) : Exception(reason + (detail is null ? "" : ": " + detail))
{
    public string Reason { get; } = reason;

    public string? Detail { get; } = detail;
}
