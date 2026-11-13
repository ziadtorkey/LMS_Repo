namespace Lms.Infrastructure.Content;

/// <summary>
/// Magic-byte check of an upload against its extension (X-05): a file named .png that holds HTML, or a .pdf that is a
/// script, is refused. It looks at the first bytes only; it does not validate the whole file.
/// </summary>
public static class AssetSignatures
{
    /// <summary>How many leading bytes <see cref="Matches"/> needs.</summary>
    public const int HeaderLength = 512;

    public static bool Matches(string extension, ReadOnlySpan<byte> header) => extension.ToLowerInvariant() switch
    {
        ".pdf" => header.StartsWith("%PDF-"u8),
        ".png" => header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".jpg" or ".jpeg" => header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        ".gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
        ".webp" => IsRiff(header, "WEBP"u8),
        ".wav" => IsRiff(header, "WAVE"u8),
        ".mp3" => header.StartsWith("ID3"u8) || (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0),
        ".m4a" or ".mp4" => header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8),
        ".ogg" => header.StartsWith("OggS"u8),
        ".webm" => header.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        ".docx" or ".xlsx" or ".pptx" => header.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
        ".txt" => IsText(header),
        _ => false,
    };

    private static bool IsRiff(ReadOnlySpan<byte> header, ReadOnlySpan<byte> form) =>
        header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual(form);

    // Plain text: no NUL bytes, and not markup (a .txt that starts like HTML is refused).
    private static bool IsText(ReadOnlySpan<byte> header) =>
        !header.Contains((byte)0) && !LooksLikeMarkup(header);

    private static bool LooksLikeMarkup(ReadOnlySpan<byte> header)
    {
        var trimmed = header.TrimStart("\t\r\n ﻿"u8.ToArray());
        return trimmed.Length > 0 && trimmed[0] == (byte)'<';
    }
}
