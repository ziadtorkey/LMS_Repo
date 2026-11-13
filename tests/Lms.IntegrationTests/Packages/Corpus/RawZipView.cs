using System.IO.Compression;
using System.Text;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>One central-directory record as the file says it (no trust, no normalisation).</summary>
public sealed record RawZipRecord(byte[] NameBytes, ushort Flags, ushort Method, ushort VersionMadeBy, long CompressedSize, long UncompressedSize, uint ExternalAttributes, long LocalOffset)
{
    public string NameUtf8 => Encoding.UTF8.GetString(NameBytes);

    public bool Encrypted => (Flags & 0x1) != 0;

    public bool Utf8Flag => (Flags & 0x800) != 0;

    public bool DataDescriptor => (Flags & 0x8) != 0;

    /// <summary>Unix mode from the high 16 bits when the entry was made on Unix; 0 otherwise.</summary>
    public uint UnixMode => (VersionMadeBy >> 8) == 3 ? ExternalAttributes >> 16 : 0;

    public bool IsSymlink => (UnixMode & 0xF000) == 0xA000;
}

/// <summary>
/// An independent, minimal reader of the corpus files. It parses the end record and the central directory directly (including Zip64)
/// so a corpus probe does not depend on the code under test, nor on how <see cref="ZipArchive"/> treats hostile headers.
/// </summary>
public sealed class RawZipView
{
    private readonly byte[] _bytes;

    private RawZipView(byte[] bytes, List<RawZipRecord> records, bool zip64End, long prefixLength)
    {
        _bytes = bytes;
        Records = records;
        HasZip64EndRecord = zip64End;
        PrefixLength = prefixLength;
    }

    public IReadOnlyList<RawZipRecord> Records { get; }

    public bool HasZip64EndRecord { get; }

    /// <summary>Bytes before the first local header (a polyglot or self-extractor stub), 0 for a normal archive.</summary>
    public long PrefixLength { get; }

    public byte[] Bytes => _bytes;

    public static RawZipView Parse(byte[] bytes)
    {
        var end = FindEnd(bytes);
        long count = U16(bytes, end + 10);
        long cdSize = U32(bytes, end + 12);
        long cdStart = U32(bytes, end + 16);
        var zip64 = false;
        long cdAbsolute = end - cdSize;
        if (end >= 20 && U32(bytes, end - 20) == 0x07064b50)
        {
            var end64 = FindSignature(bytes, 0x06064b50, end - 20 + 56);
            count = BitConverter.ToInt64(bytes, end64 + 32);
            cdSize = BitConverter.ToInt64(bytes, end64 + 40);
            cdStart = BitConverter.ToInt64(bytes, end64 + 48);
            cdAbsolute = end64 - cdSize;
            zip64 = true;
        }

        var prefix = cdAbsolute - cdStart;
        var records = new List<RawZipRecord>();
        var p = (int)cdAbsolute;
        for (long i = 0; i < count; i++)
        {
            if (U32(bytes, p) != 0x02014b50)
            {
                throw new InvalidDataException($"No central directory record at {p}");
            }

            var madeBy = U16(bytes, p + 4);
            var flags = U16(bytes, p + 8);
            var method = U16(bytes, p + 10);
            long comp = U32(bytes, p + 20);
            long uncomp = U32(bytes, p + 24);
            var nameLen = U16(bytes, p + 28);
            var extraLen = U16(bytes, p + 30);
            var commentLen = U16(bytes, p + 32);
            var attrs = (uint)U32(bytes, p + 38);
            long offset = U32(bytes, p + 42);
            var name = bytes[(p + 46)..(p + 46 + nameLen)];
            ReadZip64Extra(bytes, p + 46 + nameLen, extraLen, ref uncomp, ref comp, ref offset);
            records.Add(new RawZipRecord(name, (ushort)flags, (ushort)method, (ushort)madeBy, comp, uncomp, attrs, offset));
            p += 46 + nameLen + extraLen + commentLen;
        }

        return new RawZipView(bytes, records, zip64, prefix);
    }

    /// <summary>The decompressed bytes of an entry, read with the real stream (never the header size), capped at <paramref name="limit"/>.</summary>
    public byte[] ReadData(RawZipRecord r, long limit = 16 * 1024 * 1024)
    {
        var p = (int)(PrefixLength + r.LocalOffset);
        var nameLen = U16(_bytes, p + 26);
        var extraLen = U16(_bytes, p + 28);
        var start = p + 30 + nameLen + extraLen;
        using var compressed = new MemoryStream(_bytes, start, (int)Math.Min(r.CompressedSize, _bytes.Length - start));
        using Stream source = r.Method == 8 ? new DeflateStream(compressed, CompressionMode.Decompress) : compressed;
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > limit)
            {
                break;
            }
        }

        return output.ToArray();
    }

    public RawZipRecord Find(string name) => Records.Single(r => r.NameUtf8 == name);

    private static void ReadZip64Extra(byte[] b, int at, int len, ref long uncomp, ref long comp, ref long offset)
    {
        var end = at + len;
        while (at + 4 <= end)
        {
            int id = U16(b, at), size = U16(b, at + 2);
            if (id == 1)
            {
                var q = at + 4;
                if (uncomp == 0xFFFFFFFF)
                {
                    uncomp = BitConverter.ToInt64(b, q);
                    q += 8;
                }

                if (comp == 0xFFFFFFFF)
                {
                    comp = BitConverter.ToInt64(b, q);
                    q += 8;
                }

                if (offset == 0xFFFFFFFF)
                {
                    offset = BitConverter.ToInt64(b, q);
                }
            }

            at += 4 + size;
        }
    }

    private static int FindEnd(byte[] b)
    {
        for (var i = b.Length - 22; i >= Math.Max(0, b.Length - 22 - 65535); i--)
        {
            if (U32(b, i) == 0x06054b50)
            {
                return i;
            }
        }

        throw new InvalidDataException("No end of central directory record");
    }

    private static int FindSignature(byte[] b, uint sig, int before)
    {
        for (var i = before - 56; i >= 0; i--)
        {
            if (U32(b, i) == sig)
            {
                return i;
            }
        }

        throw new InvalidDataException("Signature not found");
    }

    private static int U16(byte[] b, int p) => BitConverter.ToUInt16(b, p);

    private static long U32(byte[] b, int p) => BitConverter.ToUInt32(b, p);
}
