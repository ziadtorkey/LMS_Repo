namespace Lms.Infrastructure.Packages;

/// <summary>One central-directory record exactly as the file says it (no trust, no normalisation).</summary>
internal sealed record CentralRecord(
    byte[] NameBytes, ushort Flags, ushort Method, ushort VersionMadeBy, long CompressedSize, long UncompressedSize, uint ExternalAttributes,
    long LocalOffset)
{
    public bool Utf8Flag => (Flags & 0x800) != 0;

    public bool Encrypted => (Flags & 0x1) != 0 || (Flags & 0x40) != 0;

    /// <summary>The Unix mode when the entry was made on Unix (high 16 bits of the external attributes), otherwise 0.</summary>
    public uint UnixMode => (VersionMadeBy >> 8) == 3 ? ExternalAttributes >> 16 : 0;

    public bool IsSymlink => (UnixMode & 0xF000) == 0xA000;
}

/// <summary>
/// A minimal reader of the end record and the central directory, including Zip64 and archives with a prefix. It does not use
/// <c>ZipArchive</c> because the rules need what that class hides: raw name bytes, the encryption flag, the Unix mode, the local
/// header and the sizes as written. Anything malformed throws <see cref="InvalidDataException"/>.
/// </summary>
internal sealed class ZipCentralDirectory
{
    private const uint EndSignature = 0x06054b50, End64Signature = 0x06064b50, Locator64Signature = 0x07064b50, CentralSignature = 0x02014b50;

    private const int MaxDirectoryBytes = 32 * 1024 * 1024;

    private ZipCentralDirectory(IReadOnlyList<CentralRecord> records, long prefixLength, long declaredCount)
    {
        Records = records;
        PrefixLength = prefixLength;
        DeclaredCount = declaredCount;
    }

    public IReadOnlyList<CentralRecord> Records { get; }

    /// <summary>Bytes before the first local header (a polyglot stub); local offsets are relative to it.</summary>
    public long PrefixLength { get; }

    public long DeclaredCount { get; }

    /// <summary>Reads the directory; <paramref name="maxRecords"/> bounds the work (a larger declared count is returned as such, with no records).</summary>
    public static ZipCentralDirectory Read(Stream archive, int maxRecords)
    {
        var length = archive.Length;
        if (length < 22)
        {
            throw new InvalidDataException("Too short for a zip.");
        }

        var tailLength = (int)Math.Min(length, 22 + 65535 + 20 + 56);
        var tail = new byte[tailLength];
        archive.Position = length - tailLength;
        archive.ReadExactly(tail);
        var end = -1;
        for (var i = tailLength - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) == EndSignature)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            throw new InvalidDataException("No end of central directory.");
        }

        var endAbsolute = length - tailLength + end;
        long count = BitConverter.ToUInt16(tail, end + 10);
        long directorySize = BitConverter.ToUInt32(tail, end + 12);
        long directoryStart = BitConverter.ToUInt32(tail, end + 16);
        var directoryEnd = endAbsolute;
        if (end >= 20 && BitConverter.ToUInt32(tail, end - 20) == Locator64Signature)
        {
            var end64At = endAbsolute - 20 - 56;
            if (end64At < 0)
            {
                throw new InvalidDataException("Bad Zip64 locator.");
            }

            var record = new byte[56];
            archive.Position = end64At;
            archive.ReadExactly(record);
            if (BitConverter.ToUInt32(record, 0) != End64Signature)
            {
                throw new InvalidDataException("Bad Zip64 end record.");
            }

            count = BitConverter.ToInt64(record, 32);
            directorySize = BitConverter.ToInt64(record, 40);
            directoryStart = BitConverter.ToInt64(record, 48);
            directoryEnd = end64At;
        }

        if (count < 0 || directorySize < 0 || directoryStart < 0 || directorySize > directoryEnd || directorySize > MaxDirectoryBytes)
        {
            throw new InvalidDataException("Bad central directory.");
        }

        if (count > maxRecords)
        {
            return new ZipCentralDirectory([], 0, count);
        }

        var directoryAbsolute = directoryEnd - directorySize;
        var prefix = directoryAbsolute - directoryStart;
        if (prefix < 0)
        {
            throw new InvalidDataException("Bad central directory offset.");
        }

        var bytes = new byte[directorySize];
        archive.Position = directoryAbsolute;
        archive.ReadExactly(bytes);
        var records = new List<CentralRecord>((int)count);
        var p = 0;
        for (long i = 0; i < count; i++)
        {
            if (p + 46 > bytes.Length || BitConverter.ToUInt32(bytes, p) != CentralSignature)
            {
                throw new InvalidDataException("No central directory record.");
            }

            var madeBy = BitConverter.ToUInt16(bytes, p + 4);
            var flags = BitConverter.ToUInt16(bytes, p + 8);
            var method = BitConverter.ToUInt16(bytes, p + 10);
            long compressed = BitConverter.ToUInt32(bytes, p + 20);
            long uncompressed = BitConverter.ToUInt32(bytes, p + 24);
            int nameLength = BitConverter.ToUInt16(bytes, p + 28), extraLength = BitConverter.ToUInt16(bytes, p + 30), commentLength = BitConverter.ToUInt16(bytes, p + 32);
            var attributes = BitConverter.ToUInt32(bytes, p + 38);
            long offset = BitConverter.ToUInt32(bytes, p + 42);
            var next = p + 46 + nameLength + extraLength + commentLength;
            if (next > bytes.Length)
            {
                throw new InvalidDataException("Record runs past the directory.");
            }

            ReadZip64(bytes, p + 46 + nameLength, extraLength, ref uncompressed, ref compressed, ref offset);
            records.Add(new CentralRecord(bytes[(p + 46)..(p + 46 + nameLength)], flags, method, madeBy, compressed, uncompressed, attributes, offset));
            p = next;
        }

        return new ZipCentralDirectory(records, prefix, count);
    }

    private static void ReadZip64(byte[] b, int at, int length, ref long uncompressed, ref long compressed, ref long offset)
    {
        var end = at + length;
        while (at + 4 <= end)
        {
            int id = BitConverter.ToUInt16(b, at), size = BitConverter.ToUInt16(b, at + 2);
            if (id == 1)
            {
                var q = at + 4;
                if (uncompressed == 0xFFFFFFFF && q + 8 <= end)
                {
                    uncompressed = BitConverter.ToInt64(b, q);
                    q += 8;
                }

                if (compressed == 0xFFFFFFFF && q + 8 <= end)
                {
                    compressed = BitConverter.ToInt64(b, q);
                    q += 8;
                }

                if (offset == 0xFFFFFFFF && q + 8 <= end)
                {
                    offset = BitConverter.ToInt64(b, q);
                }
            }

            at += 4 + size;
        }
    }
}
