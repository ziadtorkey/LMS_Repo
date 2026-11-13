using System.IO.Compression;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// The streamed hardening reader of package archives (M4b Task 8a, #427 7.1). <see cref="InspectAsync"/> applies every rule without
/// writing anything: the central directory is read as the file says it, names are decoded and checked, and each entry is
/// decompressed into a counter so that the real byte count, never a header, decides the size and ratio rules.
/// <see cref="ExtractAsync"/> then hands each safe entry to a sink with the same counting guard, so an archive that changes between
/// the two passes cannot write more than was inspected. The archive stream must be seekable.
/// </summary>
public static class ZipHardening
{
    private const uint LocalSignature = 0x04034b50;

    public static async Task<ZipInspection> InspectAsync(Stream archive, ZipHardeningLimits limits, CancellationToken ct = default)
    {
        ZipCentralDirectory directory;
        try
        {
            directory = ZipCentralDirectory.Read(archive, limits.MaxEntries);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException or OverflowException)
        {
            return ZipInspection.Rejected(PackageRejectReasons.invalid_archive, ex.Message);
        }

        if (directory.DeclaredCount > limits.MaxEntries)
        {
            return ZipInspection.Rejected(PackageRejectReasons.too_many_entries, $"{directory.DeclaredCount} entries");
        }

        if (directory.Records.Count == 0)
        {
            return ZipInspection.Rejected(PackageRejectReasons.invalid_archive, "No entries.");
        }

        // The sizes the headers claim are only a cheap early refusal; the real bytes are counted below.
        long declared = 0;
        foreach (var record in directory.Records)
        {
            declared += Math.Max(0, record.UncompressedSize);
            if (declared > limits.MaxUncompressedBytes)
            {
                return ZipInspection.Rejected(PackageRejectReasons.too_large, "The archive declares more than the uncompressed limit.");
            }
        }

        var names = new List<(CentralRecord Record, string Name)>(directory.Records.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in directory.Records)
        {
            var (name, reason) = CheckRecord(record, limits);
            if (reason is null && (!seen.Add(ZipNameRules.CollisionKey(name!)) || Collides(name!, files, folders)))
            {
                reason = PackageRejectReasons.duplicate_name;
            }

            if (reason is null)
            {
                reason = CheckLocalHeader(archive, directory, record);
            }

            if (reason is not null)
            {
                return ZipInspection.Rejected(reason, name ?? "(unreadable name)");
            }

            names.Add((record, name!));
        }

        var entries = new List<ZipEntry>(names.Count);
        long total = 0;
        foreach (var (record, name) in names)
        {
            ct.ThrowIfCancellationRequested();
            if (name.EndsWith('/'))
            {
                entries.Add(new ZipEntry(name, 0, true, 0, 0, record.Method));
                continue;
            }

            var dataOffset = DataOffset(archive, directory, record);
            long length;
            try
            {
                length = await CountAsync(archive, dataOffset, record, limits, total, ct);
            }
            catch (ZipHardeningException ex)
            {
                return ZipInspection.Rejected(ex.Reason, name);
            }

            if (record.UncompressedSize != length)
            {
                return ZipInspection.Rejected(PackageRejectReasons.invalid_archive, $"{name}: the header size differs from the real size.");
            }

            total += length;
            if (total > limits.TotalRatioFloorBytes && total / Math.Max(1, archive.Length) > limits.MaxTotalRatio)
            {
                return ZipInspection.Rejected(PackageRejectReasons.compression_ratio, "The archive expands far more than normal content.");
            }

            entries.Add(new ZipEntry(name, length, false, dataOffset, record.CompressedSize, record.Method));
        }

        return new ZipInspection(null, null, entries, total);
    }

    /// <summary>
    /// Hands every file entry of an accepted inspection to <paramref name="sink"/> (name, stream of exactly <see cref="ZipEntry.Length"/> bytes).
    /// The stream stops the archive from yielding more than was inspected: a longer entry throws <see cref="ZipHardeningException"/>.
    /// </summary>
    public static async Task ExtractAsync(
        Stream archive, ZipInspection inspection, Func<ZipEntry, Stream, Task> sink, CancellationToken ct = default)
    {
        if (!inspection.Accepted)
        {
            throw new InvalidOperationException("Only an accepted inspection can be extracted.");
        }

        foreach (var entry in inspection.Entries.Where(e => !e.IsDirectory))
        {
            ct.ThrowIfCancellationRequested();
            archive.Position = entry.DataOffset;
            await using var bounded = new BoundedReadStream(archive, entry.CompressedSize);
            Stream source = entry.Method == 8 ? new DeflateStream(bounded, CompressionMode.Decompress) : bounded;
            await using (source)
            {
                await using var guard = new ExactLengthStream(source, entry.Length);
                await sink(entry, guard);
                await guard.EnsureCompleteAsync(ct);
            }
        }
    }

    /// <summary>A file whose name is also a folder of another entry (a and a/b) cannot both exist on a file system: refused as a duplicate. Records what it sees.</summary>
    private static bool Collides(string name, HashSet<string> files, HashSet<string> folders)
    {
        var key = ZipNameRules.CollisionKey(name);
        var parts = key.Split('/');
        var collides = false;
        for (var i = 1; i < parts.Length; i++)
        {
            var folder = string.Join('/', parts[..i]);
            collides |= files.Contains(folder);
            folders.Add(folder);
        }

        if (name.EndsWith('/'))
        {
            collides |= files.Contains(key);
            folders.Add(key);
        }
        else
        {
            collides |= folders.Contains(key);
            files.Add(key);
        }

        return collides;
    }

    private static (string? Name, string? Reason) CheckRecord(CentralRecord record, ZipHardeningLimits limits)
    {
        if (record.Encrypted)
        {
            return (null, PackageRejectReasons.encrypted);
        }

        if (record.Method is not (0 or 8))
        {
            return (null, PackageRejectReasons.invalid_archive);
        }

        var name = ZipNameRules.Decode(record.NameBytes, record.Utf8Flag);
        if (name is null)
        {
            return (null, PackageRejectReasons.bad_name_encoding);
        }

        if (ZipNameRules.Check(name, limits) is { } nameReason)
        {
            return (name, nameReason);
        }

        if (record.IsSymlink)
        {
            return (name, PackageRejectReasons.symlink);
        }

        if (!name.EndsWith('/') && ZipNameRules.CheckExtension(name) is { } extensionReason)
        {
            return (name, extensionReason);
        }

        return (name, null);
    }

    /// <summary>The local header must exist and name the entry exactly as the central directory does (a reader that trusts the other one extracts something else).</summary>
    private static string? CheckLocalHeader(Stream archive, ZipCentralDirectory directory, CentralRecord record)
    {
        try
        {
            var header = ReadLocalHeader(archive, directory, record, out var localName);
            return header && localName.AsSpan().SequenceEqual(record.NameBytes) ? null : PackageRejectReasons.bad_name;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            return PackageRejectReasons.invalid_archive;
        }
    }

    private static long DataOffset(Stream archive, ZipCentralDirectory directory, CentralRecord record)
    {
        ReadLocalHeader(archive, directory, record, out _, out var dataOffset);
        return dataOffset;
    }

    private static bool ReadLocalHeader(Stream archive, ZipCentralDirectory directory, CentralRecord record, out byte[] name) =>
        ReadLocalHeader(archive, directory, record, out name, out _);

    private static bool ReadLocalHeader(Stream archive, ZipCentralDirectory directory, CentralRecord record, out byte[] name, out long dataOffset)
    {
        var at = directory.PrefixLength + record.LocalOffset;
        name = [];
        dataOffset = 0;
        if (at < 0 || at + 30 > archive.Length)
        {
            throw new InvalidDataException("Local header outside the archive.");
        }

        var header = new byte[30];
        archive.Position = at;
        archive.ReadExactly(header);
        if (BitConverter.ToUInt32(header, 0) != LocalSignature)
        {
            throw new InvalidDataException("No local header.");
        }

        int nameLength = BitConverter.ToUInt16(header, 26), extraLength = BitConverter.ToUInt16(header, 28);
        name = new byte[nameLength];
        archive.ReadExactly(name);
        dataOffset = at + 30 + nameLength + extraLength;
        if (dataOffset > archive.Length)
        {
            throw new InvalidDataException("Local header runs past the archive.");
        }

        return true;
    }

    /// <summary>Decompresses an entry into a counter; throws a <see cref="ZipHardeningException"/> as soon as a size or ratio rule breaks.</summary>
    private static async Task<long> CountAsync(
        Stream archive, long dataOffset, CentralRecord record, ZipHardeningLimits limits, long totalSoFar, CancellationToken ct)
    {
        archive.Position = dataOffset;
        await using var bounded = new BoundedReadStream(archive, record.CompressedSize);
        Stream source = record.Method == 8 ? new DeflateStream(bounded, CompressionMode.Decompress) : bounded;
        await using (source)
        {
            var buffer = new byte[81920];
            long produced = 0;
            int read;
            try
            {
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    produced += read;
                    if (totalSoFar + produced > limits.MaxUncompressedBytes)
                    {
                        throw new ZipHardeningException(PackageRejectReasons.too_large, "The real size exceeds the uncompressed limit.");
                    }

                    if (produced > limits.RatioFloorBytes && produced > limits.MaxEntryRatio * Math.Max(1L, record.CompressedSize))
                    {
                        throw new ZipHardeningException(PackageRejectReasons.compression_ratio, "An entry expands far more than normal content.");
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw new ZipHardeningException(PackageRejectReasons.invalid_archive, "A compressed entry is corrupt.");
            }

            return produced;
        }
    }
}
