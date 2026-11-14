using Lms.Domain.Files;

namespace Lms.Infrastructure.Excuses;

/// <summary>Bound from <c>Excuses</c> configuration. <see cref="ScanAttachments"/> is off by default (D65).</summary>
public sealed class ExcuseAttachmentOptions
{
    public const string Section = "Excuses";

    /// <summary>Run <see cref="IFileScanner"/> on every attachment before it is stored. Off by default; the registered scanner is a no-op until a real one is wired.</summary>
    public bool ScanAttachments { get; set; }
}

/// <summary>The outcome of checking an attachment: the content type found by sniffing, or the reason it is refused (400 <c>excuse_attachment_invalid</c>).</summary>
public sealed record ExcuseAttachmentCheck(string? ContentType, string? Refusal)
{
    public bool IsValid => Refusal is null;
}

/// <summary>
/// Checks an excuse attachment (plan L10): the type comes from the <b>content</b> (magic bytes), never from the file name or the client header, and must be in the
/// allow-list (jpeg, png, webp, pdf); the size is capped by <c>Excuses.MaxAttachmentMb</c>; the optional scanner has the last word.
/// </summary>
public sealed class ExcuseAttachmentValidator(IFileScanner scanner, bool scanEnabled)
{
    public const string TooLarge = "TooLarge";
    public const string Empty = "Empty";
    public const string TypeNotAllowed = "TypeNotAllowed";
    public const string Infected = "Infected";

    public async Task<ExcuseAttachmentCheck> CheckAsync(Stream content, string fileName, long maxBytes, CancellationToken ct = default)
    {
        if (content.Length == 0)
        {
            return new(null, Empty);
        }

        if (content.Length > maxBytes)
        {
            return new(null, TooLarge);
        }

        var head = new byte[16];
        content.Position = 0;
        var read = await content.ReadAsync(head, ct);
        content.Position = 0;
        var type = Sniff(head.AsSpan(0, read));
        if (type is null)
        {
            return new(null, TypeNotAllowed);
        }

        if (scanEnabled && !(await scanner.ScanAsync(content, fileName, ct)).IsClean)
        {
            content.Position = 0;
            return new(null, Infected);
        }

        content.Position = 0;
        return new(type, null);
    }

    /// <summary>The allow-listed content type of the first bytes, or null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return head.StartsWith("%PDF-"u8) ? "application/pdf" : null;
    }
}
