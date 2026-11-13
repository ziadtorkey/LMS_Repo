using System.Text;

namespace Lms.Infrastructure.Packages.Scorm;

/// <summary>
/// Puts the SCORM prelude into a package page (M4b Task 10, Decision 11 and #427 7.2, Design A). The injector inserts a static prelude
/// script and a per-request data block as the <b>first children of head</b> of <b>every</b> HTML page, so the API exists before any package
/// script runs. It works on the decoded text and writes the page back in the encoding it came in: a UTF-8 byte order mark is kept,
/// UTF-16 pages (byte order mark) are decoded and re-encoded, a <c>meta charset</c> (UTF-8, ASCII, ISO-8859-1 / windows-1252) decides
/// otherwise, and bytes outside the injected text are never changed. A page without head gets one right after the html tag (or at the first
/// element); doctype, comments and conditional comments before html are skipped, and a head that is only mentioned in a comment or a script
/// is not mistaken for the real one.
/// </summary>
public static class ScormPreludeInjector
{
    /// <summary>The markup inserted: the cacheable static script, then the data block (JSON made safe for a script element).</summary>
    public static string Markup(string preludeSrc, string dataJson) =>
        $"<script src=\"{System.Net.WebUtility.HtmlEncode(preludeSrc)}\"></script><script type=\"application/json\" id=\"lms-scorm-data\">{ScriptSafe(dataJson)}</script>";

    /// <summary>Injects into an HTML page; every other kind of file is returned as the same array (never modified).</summary>
    public static byte[] InjectIfHtml(string relativePath, byte[] content, string preludeSrc, string dataJson) =>
        PackageContentTypes.For(relativePath).Type.StartsWith("text/html", StringComparison.Ordinal)
            ? Inject(content, preludeSrc, dataJson)
            : content;

    public static byte[] Inject(byte[] html, string preludeSrc, string dataJson)
    {
        var markup = Markup(preludeSrc, dataJson);
        if (IsUtf16(html))
        {
            // UTF-16 decodes cleanly in both directions, so it is the one case that goes through text.
            var text = Decode(html, out var encoding, out var preamble);
            var textAt = InsertionPoint(text, out var textNeedsHead);
            var injected = text[..textAt] + (textNeedsHead ? "<head>" + markup + "</head>" : markup) + text[textAt..];
            return [.. preamble, .. encoding.GetBytes(injected)];
        }

        // Every other page is spliced as bytes: Latin1 maps each byte to one character, which is only used to find the insertion offset (the tags
        // are ASCII in every ASCII-compatible charset), and the ASCII markup is inserted into the original array. The page is never decoded
        // or re-encoded, so Shift_JIS, windows-1256, invalid UTF-8 or anything else keeps every byte it had.
        var at = InsertionPoint(Encoding.Latin1.GetString(html), out var needsHead);
        var piece = Encoding.ASCII.GetBytes(needsHead ? "<head>" + markup + "</head>" : markup);
        return [.. html.AsSpan(0, at), .. piece, .. html.AsSpan(at)];
    }

    private static bool IsUtf16(byte[] bytes) => bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF);

    /// <summary>
    /// A text view of a page for searching: UTF-16 (byte order mark) is decoded, anything else is seen byte for byte (Latin1), which is enough for
    /// the ASCII markup the callers look for. Also returns the encoding and the preamble of the UTF-16 case.
    /// </summary>
    public static string Decode(byte[] bytes, out Encoding encoding, out byte[] preamble)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            (encoding, preamble) = (new UnicodeEncoding(false, false), [0xFF, 0xFE]);
            return encoding.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            (encoding, preamble) = (new UnicodeEncoding(true, false), [0xFE, 0xFF]);
            return encoding.GetString(bytes, 2, bytes.Length - 2);
        }

        (encoding, preamble) = (Encoding.Latin1, []);
        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// The index to insert at: right after the real head start tag, or (needsHead) after the html start tag or at the first element when there
    /// is no head. Comments, the doctype and other declarations are skipped; text before the first element is skipped.
    /// </summary>
    private static int InsertionPoint(string text, out bool needsHead)
    {
        needsHead = false;
        var afterHtml = -1;
        var i = 0;
        while (i < text.Length)
        {
            var lt = text.IndexOf('<', i);
            if (lt < 0)
            {
                break;
            }

            if (string.CompareOrdinal(text, lt, "<!--", 0, 4) == 0)
            {
                var close = text.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = close < 0 ? text.Length : close + 3;
                continue;
            }

            if (lt + 1 < text.Length && (text[lt + 1] == '!' || text[lt + 1] == '?'))
            {
                var close = text.IndexOf('>', lt);
                i = close < 0 ? text.Length : close + 1;
                continue;
            }

            var tag = TagName(text, lt);
            var end = TagEnd(text, lt);
            if (tag == "html")
            {
                afterHtml = end;
                i = end;
                continue;
            }

            if (tag == "head")
            {
                return end;
            }

            // The first other element: the page has no head (a body, a frameset or a bare fragment).
            needsHead = true;
            return afterHtml >= 0 ? afterHtml : lt;
        }

        needsHead = true;
        return afterHtml >= 0 ? afterHtml : text.Length;
    }

    private static string TagName(string text, int lt)
    {
        var p = lt + 1;
        var start = p;
        while (p < text.Length && (char.IsLetterOrDigit(text[p]) || text[p] is '-' or ':'))
        {
            p++;
        }

        return text[start..p].ToLowerInvariant();
    }

    /// <summary>The index just after the <c>&gt;</c> that closes the tag starting at <paramref name="lt"/> (quoted attribute values may hold a <c>&gt;</c>).</summary>
    private static int TagEnd(string text, int lt)
    {
        char quote = '\0';
        for (var p = lt + 1; p < text.Length; p++)
        {
            var c = text[p];
            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return p + 1;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// JSON inside a script element, as ASCII only: nothing may close the element or open a comment, and every non-ASCII character is written as a
    /// \u escape, so the markup is the same bytes in any ASCII-compatible charset the page uses.
    /// </summary>
    private static string ScriptSafe(string json)
    {
        var sb = new StringBuilder(json.Length);
        foreach (var c in json)
        {
            if (c is '<' or '>' or '&' || c > 0x7E)
            {
                sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
