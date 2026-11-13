using System.Text;
using System.Text.RegularExpressions;

namespace Lms.Infrastructure.Packages.Scorm;

/// <summary>
/// The launch file of a SCORM package (M4b Task 10, Decision 11): the <c>href</c> of the manifest is resolved against the package root and
/// nothing else. <c>xml:base</c> is ignored, an absolute or scheme URL, a protocol-relative one, a backslash, a rooted path or a <c>..</c>
/// segment is refused (so a path can never select another package), and a query or fragment is cut off before the file is looked up.
/// </summary>
public static partial class ScormLaunchResolver
{
    /// <summary>The normalised relative path of the launch file, or null when the href is not a path inside the package.</summary>
    public static string? Normalize(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var path = href.Trim();
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        if (path.Length == 0 || path.Contains('\\') || path.StartsWith('/') || SchemeOrDrive().IsMatch(path) || path.Any(char.IsControl))
        {
            return null;
        }

        var segments = path.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".."))
        {
            return null;
        }

        return string.Join('/', segments.Select(Uri.UnescapeDataString));
    }

    /// <summary>
    /// True when the launch page is a frameset or nests another page in a frame or iframe: its SCO would look for the API in a parent frame that,
    /// under the opaque-origin sandbox, it cannot reach (Design A, <c>needs_nested_frames</c>). Comments are ignored; the text is decoded as
    /// the byte order mark or <c>meta charset</c> says.
    /// </summary>
    public static bool UsesFrames(byte[] html)
    {
        var text = ScormPreludeInjector.Decode(html, out _, out _);
        text = Comments().Replace(text, string.Empty);
        return Frames().IsMatch(text);
    }

    [GeneratedRegex("^(?:[A-Za-z][A-Za-z0-9+.-]*:|//)")]
    private static partial Regex SchemeOrDrive();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex("<\\s*(?:frameset|frame|iframe)[\\s>/]", RegexOptions.IgnoreCase)]
    private static partial Regex Frames();

    internal static Encoding Utf8NoBom { get; } = new UTF8Encoding(false);
}
