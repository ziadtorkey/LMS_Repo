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
        var text = Decode(html, out var encoding, out var preamble);
        var at = InsertionPoint(text, out var needsHead);
        var markup = Markup(preludeSrc, dataJson);
        var injected = text[..at] + (needsHead ? "<head>" + markup + "</head>" : markup) + text[at..];
        return [.. preamble, .. encoding.GetBytes(injected)];
    }

    /// <summary>Decodes a page (byte order mark, else <c>meta charset</c> in the first kilobyte, else UTF-8); also returns the encoding and the preamble to put back.</summary>
    public static string Decode(byte[] bytes, out Encoding encoding, out byte[] preamble)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            (encoding, preamble) = (ScormLaunchResolver.Utf8NoBom, [0xEF, 0xBB, 0xBF]);
            return encoding.GetString(bytes, 3, bytes.Length - 3);
        }

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

        preamble = [];
        encoding = DeclaredCharset(bytes);
        return encoding.GetString(bytes);
    }

    private static Encoding DeclaredCharset(byte[] bytes)
    {
        var head = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
        var at = head.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return ScormLaunchResolver.Utf8NoBom;
        }

        var name = new string(head[(at + 8)..].TrimStart('"', '\'').TakeWhile(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray()).ToLowerInvariant();
        return name switch
        {
            "iso-8859-1" or "latin1" or "windows-1252" or "cp1252" or "us-ascii" or "ascii" => Encoding.Latin1,
            _ => ScormLaunchResolver.Utf8NoBom,
        };
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

    /// <summary>JSON inside a script element: nothing may close the element or open a comment, and the line separators of old parsers are escaped.</summary>
    private static string ScriptSafe(string json) =>
        json.Replace("<", "\\u003c", StringComparison.Ordinal).Replace(">", "\\u003e", StringComparison.Ordinal)
            .Replace("&", "\\u0026", StringComparison.Ordinal).Replace(""+(char)0x2028, "\\u2028", StringComparison.Ordinal)
            .Replace(""+(char)0x2029, "\\u2029", StringComparison.Ordinal);
}
