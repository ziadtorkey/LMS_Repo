using System.Text.RegularExpressions;

namespace Lms.Client.Components.Editors;

/// <summary>Formats the editor reports for the current selection (drives the toolbar highlight).</summary>
public sealed record RichTextFormats(bool Bold, bool Italic, string? List, string? Link)
{
    public static readonly RichTextFormats None = new(false, false, null, null);
}

/// <summary>Result of opening the link bar: whether text is selected and the href already on the selection, if any.</summary>
public sealed record RichTextLinkState(bool HasSelection, string? Href);

/// <summary>Link rules for the editor. Only web and mail links are allowed; the server sanitiser enforces the same list.</summary>
public static class RichTextLink
{
    private static readonly Regex Scheme = new(@"^[a-z][a-z0-9+.\-]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Trims the input and returns an absolute http, https or mailto URL, or null when it is empty or unsafe.
    /// A bare host such as "example.com/x" becomes https://example.com/x.
    /// </summary>
    public static string? Normalize(string? input)
    {
        var url = input?.Trim();
        if (string.IsNullOrEmpty(url)) return null;
        if (url.Any(char.IsWhiteSpace) || url.StartsWith("//", StringComparison.Ordinal)) return null;

        if (Scheme.IsMatch(url))
        {
            var ok = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                     || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
            return ok && url.Length > url.IndexOf(':') + 1 ? url : null;
        }

        // No scheme: treat as a host (needs a dot so "hello" is not turned into a link).
        return url.Contains('.') ? "https://" + url : null;
    }
}

public static class RichTextHtml
{
    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);

    /// <summary>True when the HTML has no visible text (empty, only empty paragraphs, line breaks or whitespace).</summary>
    public static bool IsEmpty(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return true;
        var text = System.Net.WebUtility.HtmlDecode(Tags.Replace(html, ""));
        return string.IsNullOrWhiteSpace(text.Replace(' ', ' '));
    }
}
