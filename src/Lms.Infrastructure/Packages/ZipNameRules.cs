using System.Text;
using System.Text.RegularExpressions;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// The name rules of the hardening reader (#427 7.1): how a stored name is decoded and which names are refused. Every check returns
/// a <see cref="PackageRejectReasons"/> value, or null when the name is safe.
/// </summary>
public static partial class ZipNameRules
{
    private static readonly HashSet<string> Executables = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "dll", "so", "dylib", "com", "msi", "scr", "php", "phtml", "jsp", "jspx", "asp", "aspx", "cgi", "sh", "bash", "bat", "cmd", "ps1", "pl",
    };

    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "jar", "war", "ear", "7z", "rar", "tar", "gz", "tgz", "bz2", "xz", "cab", "iso",
    };

    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    // The 128 characters of code page 437 above 0x7F (the zip default when the UTF-8 flag is not set).
    private const string Cp437High =
        "ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜ¢£¥₧ƒáíóúñÑªº¿⌐¬½¼¡«»░▒▓│┤╡╢╖╕╣║╗╝╜╛┐└┴┬├─┼╞╟╚╔╩╦╠═╬╧╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀αßΓπΣσµτΦΘΩδ∞φε∩≡±≥≤⌠⌡÷≈°∙·√ⁿ²■ ";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Decodes a stored name: strict UTF-8 with the flag, code page 437 without it. Null when the bytes are not valid in the declared encoding.</summary>
    public static string? Decode(byte[] bytes, bool utf8Flag)
    {
        if (utf8Flag)
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : Cp437High[bytes[i] - 0x80];
        }

        return new string(chars);
    }

    /// <summary>The reject reason of a decoded name, or null. Order: hidden characters, path escapes, unusable names.</summary>
    public static string? Check(string name, ZipHardeningLimits limits)
    {
        if (name.Any(IsHidden))
        {
            return PackageRejectReasons.bad_name_encoding;
        }

        var isDirectory = name.EndsWith('/');
        var path = isDirectory ? name[..^1] : name;
        if (path.Length == 0 || name.Contains('\\') || path.StartsWith('/') || DriveLetter().IsMatch(path))
        {
            return PackageRejectReasons.unsafe_path;
        }

        var segments = path.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".."))
        {
            return PackageRejectReasons.unsafe_path;
        }

        var depth = isDirectory ? segments.Length : segments.Length - 1;
        if (depth > limits.MaxDepth || segments.Any(s => s.Length > limits.MaxSegmentLength || s.EndsWith('.') || s.EndsWith(' ')
                || Devices.Contains(BaseName(s))))
        {
            return PackageRejectReasons.bad_name;
        }

        return null;
    }

    /// <summary>The reject reason of a file name by its extension (executables and nested archives), or null.</summary>
    public static string? CheckExtension(string name)
    {
        var extension = Path.GetExtension(name).TrimStart('.');
        if (Executables.Contains(extension))
        {
            return PackageRejectReasons.executable;
        }

        return Archives.Contains(extension) ? PackageRejectReasons.nested_archive : null;
    }

    /// <summary>The key two names collide on: Unicode composed form, case folded, without a trailing slash.</summary>
    public static string CollisionKey(string name) => name.TrimEnd('/').Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static string BaseName(string segment)
    {
        var dot = segment.IndexOf('.');
        return dot < 0 ? segment : segment[..dot];
    }

    private static bool IsHidden(char c) =>
        char.IsControl(c) || c == '﻿' || c is >= '​' and <= '‏' || c is >= '‪' and <= '‮' || c == '⁠'
        || c is >= '⁦' and <= '⁩';

    [GeneratedRegex("^[A-Za-z]:")]
    private static partial Regex DriveLetter();
}
