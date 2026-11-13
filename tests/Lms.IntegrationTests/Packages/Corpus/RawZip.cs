using System.IO.Compression;
using System.Text;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>
/// One entry of a crafted archive. Everything a well-behaved writer would never do is a property here: a name given as raw bytes,
/// header sizes that lie, a Unix symlink mode, the encryption flag, a data descriptor, Zip64 extra fields.
/// </summary>
public sealed class RawEntry
{
    public string Name { get; init; } = "";

    /// <summary>Raw name bytes; when set they replace <see cref="Name"/> (invalid UTF-8, CP437 bytes, NUL, ...).</summary>
    public byte[]? NameBytes { get; init; }

    /// <summary>The name the local header carries when it differs from the central directory (a reader that trusts one of them extracts the other).</summary>
    public byte[]? LocalNameBytes { get; init; }

    public byte[] Data { get; init; } = [];

    /// <summary>Deflate the data (otherwise stored).</summary>
    public bool Deflate { get; init; }

    /// <summary>Declare the UTF-8 name flag (bit 11); defaults to true when the name has non-ASCII characters.</summary>
    public bool? Utf8Flag { get; init; }

    public bool Encrypted { get; init; }

    public bool DataDescriptor { get; init; }

    /// <summary>Write Zip64 extra fields (64-bit sizes and offset) for this entry.</summary>
    public bool Zip64 { get; init; }

    public uint ExternalAttributes { get; init; }

    /// <summary>Version made by; <c>3 &lt;&lt; 8</c> marks Unix, where the high 16 bits of the external attributes are the mode.</summary>
    public ushort VersionMadeBy { get; init; } = 20;

    /// <summary>Sizes the headers claim instead of the real ones (the lie a bomb or a header-trusting reader falls for).</summary>
    public long? DeclaredUncompressedSize { get; init; }

    public long? DeclaredCompressedSize { get; init; }

    /// <summary>Repeat the uncompressed stream this many times (bombs): the data is <see cref="Data"/> x <see cref="Repeat"/>.</summary>
    public long Repeat { get; init; } = 1;

    public const uint UnixSymlink = (0xA000u | 0x1FFu) << 16;

    public const ushort UnixMadeBy = (3 << 8) | 20;
}

/// <summary>Archive-level options: a prefix (polyglot / self-extractor stub), a Zip64 end record and a comment.</summary>
public sealed class RawZipOptions
{
    public byte[] Prefix { get; init; } = [];

    public bool Zip64EndRecord { get; init; }

    public string Comment { get; init; } = "";
}

/// <summary>A deterministic zip writer for hostile archives. Only the corpus uses it; it is not a general zip library.</summary>
public static class RawZipWriter
{
    private const uint Local = 0x04034b50, Central = 0x02014b50, End = 0x06054b50, End64 = 0x06064b50, Locator64 = 0x07064b50, Descriptor = 0x08074b50;

    private const ushort DosTime = 0x6000, DosDate = 0x5821;

    public static byte[] Write(IEnumerable<RawEntry> entries, RawZipOptions? options = null)
    {
        options ??= new RawZipOptions();
        using var ms = new MemoryStream();
        ms.Write(options.Prefix);
        var prefix = ms.Length;
        var centrals = new List<(RawEntry E, byte[] Name, ushort Flags, uint Crc, long Comp, long Uncomp, long Offset)>();
        foreach (var e in entries)
        {
            var name = e.NameBytes ?? Encoding.UTF8.GetBytes(e.Name);
            var utf8 = e.Utf8Flag ?? (e.NameBytes is null && name.Any(b => b > 127));
            var (payload, crc, uncompressed) = Payload(e);
            long comp = payload.Length;
            ushort flags = (ushort)((utf8 ? 0x800 : 0) | (e.Encrypted ? 0x1 : 0) | (e.DataDescriptor ? 0x8 : 0));
            var offset = ms.Length - prefix;
            WriteLocal(ms, e, name, flags, crc, comp, uncompressed);
            ms.Write(payload);
            if (e.DataDescriptor)
            {
                WriteDescriptor(ms, e, crc, comp, uncompressed);
            }

            centrals.Add((e, name, flags, crc, comp, uncompressed, offset));
        }

        var cdStart = ms.Length - prefix;
        foreach (var c in centrals)
        {
            WriteCentral(ms, c.E, c.Name, c.Flags, c.Crc, c.Comp, c.Uncomp, c.Offset);
        }

        var cdSize = ms.Length - prefix - cdStart;
        var count = centrals.Count;
        if (options.Zip64EndRecord)
        {
            var end64At = ms.Length - prefix;
            U32(ms, End64); U64(ms, 44); U16(ms, 45); U16(ms, 45); U32(ms, 0); U32(ms, 0); U64(ms, count); U64(ms, count); U64(ms, cdSize); U64(ms, cdStart);
            U32(ms, Locator64); U32(ms, 0); U64(ms, end64At); U32(ms, 1);
            WriteEnd(ms, 0xFFFF, 0xFFFFFFFF, 0xFFFFFFFF, options.Comment);
        }
        else
        {
            WriteEnd(ms, (ushort)count, (uint)cdSize, (uint)cdStart, options.Comment);
        }

        return ms.ToArray();
    }

    /// <summary>Plain valid entry helper (stored).</summary>
    public static RawEntry File(string name, string text) => new() { Name = name, Data = Encoding.UTF8.GetBytes(text) };

    public static RawEntry File(string name, byte[] data) => new() { Name = name, Data = data };

    private static (byte[] Payload, uint Crc, long Uncompressed) Payload(RawEntry e)
    {
        var crc = 0xFFFFFFFFu;
        long total = 0;
        using var raw = new MemoryStream();
        Stream sink = raw;
        DeflateStream? deflate = null;
        if (e.Deflate)
        {
            deflate = new DeflateStream(raw, CompressionLevel.Optimal, leaveOpen: true);
            sink = deflate;
        }

        for (long i = 0; i < e.Repeat; i++)
        {
            sink.Write(e.Data);
            crc = Crc32.Update(crc, e.Data);
            total += e.Data.Length;
        }

        deflate?.Dispose();
        return (raw.ToArray(), ~crc, total);
    }

    private static void WriteLocal(Stream s, RawEntry e, byte[] name, ushort flags, uint crc, long comp, long uncomp)
    {
        var descriptor = e.DataDescriptor;
        name = e.LocalNameBytes ?? name;
        var zip64 = e.Zip64 && !descriptor;
        U32(s, Local);
        U16(s, e.Zip64 ? (ushort)45 : (ushort)20);
        U16(s, flags);
        U16(s, e.Deflate ? (ushort)8 : (ushort)0);
        U16(s, DosTime); U16(s, DosDate);
        U32(s, descriptor ? 0 : crc);
        U32(s, descriptor ? 0 : zip64 ? 0xFFFFFFFF : (uint)(e.DeclaredCompressedSize ?? comp));
        U32(s, descriptor ? 0 : zip64 ? 0xFFFFFFFF : (uint)(e.DeclaredUncompressedSize ?? uncomp));
        U16(s, (ushort)name.Length);
        U16(s, zip64 ? (ushort)20 : (ushort)0);
        s.Write(name);
        if (zip64)
        {
            U16(s, 1); U16(s, 16); U64(s, e.DeclaredUncompressedSize ?? uncomp); U64(s, e.DeclaredCompressedSize ?? comp);
        }
    }

    private static void WriteDescriptor(Stream s, RawEntry e, uint crc, long comp, long uncomp)
    {
        U32(s, Descriptor);
        U32(s, crc);
        if (e.Zip64)
        {
            U64(s, comp); U64(s, uncomp);
        }
        else
        {
            U32(s, (uint)comp); U32(s, (uint)uncomp);
        }
    }

    private static void WriteCentral(Stream s, RawEntry e, byte[] name, ushort flags, uint crc, long comp, long uncomp, long offset)
    {
        var declaredComp = e.DeclaredCompressedSize ?? comp;
        var declaredUncomp = e.DeclaredUncompressedSize ?? uncomp;
        U32(s, Central);
        U16(s, e.VersionMadeBy);
        U16(s, e.Zip64 ? (ushort)45 : (ushort)20);
        U16(s, flags);
        U16(s, e.Deflate ? (ushort)8 : (ushort)0);
        U16(s, DosTime); U16(s, DosDate);
        U32(s, crc);
        U32(s, e.Zip64 ? 0xFFFFFFFF : (uint)declaredComp);
        U32(s, e.Zip64 ? 0xFFFFFFFF : (uint)declaredUncomp);
        U16(s, (ushort)name.Length);
        U16(s, e.Zip64 ? (ushort)28 : (ushort)0);
        U16(s, 0); U16(s, 0); U16(s, 0);
        U32(s, e.ExternalAttributes);
        U32(s, e.Zip64 ? 0xFFFFFFFF : (uint)offset);
        s.Write(name);
        if (e.Zip64)
        {
            U16(s, 1); U16(s, 24); U64(s, declaredUncomp); U64(s, declaredComp); U64(s, offset);
        }
    }

    private static void WriteEnd(Stream s, ushort count, uint cdSize, uint cdStart, string comment)
    {
        var c = Encoding.UTF8.GetBytes(comment);
        U32(s, End); U16(s, 0); U16(s, 0); U16(s, count); U16(s, count); U32(s, cdSize); U32(s, cdStart); U16(s, (ushort)c.Length);
        s.Write(c);
    }

    private static void U16(Stream s, ushort v) => s.Write(BitConverter.GetBytes(v));

    private static void U32(Stream s, uint v) => s.Write(BitConverter.GetBytes(v));

    private static void U64(Stream s, long v) => s.Write(BitConverter.GetBytes(v));
}

internal static class Crc32
{
    private static readonly uint[] Table = Build();

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            t[i] = c;
        }

        return t;
    }
}
