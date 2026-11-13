using System.Text.RegularExpressions;
using Ganss.Xss;
using Lms.Domain.Content;

namespace Lms.Infrastructure.Content;

/// <summary>
/// The one allow-list for rich text (D48): p, br, strong, em, u, s, h2-h4, ol, ul, li, blockquote, span and a; links
/// http/https/mailto only and always <c>rel="noopener noreferrer" target="_blank"</c>; <c>dir</c> and the Quill
/// <c>ql-align-*</c> / <c>ql-direction-rtl</c> classes. No images, frames, styles, scripts, event handlers, ids or data
/// attributes. Stateless and thread safe: one configured instance is shared.
/// </summary>
public sealed partial class RichTextSanitizer : IRichTextSanitizer
{
    private static readonly string[] Tags =
        ["p", "br", "strong", "em", "u", "s", "h2", "h3", "h4", "ol", "ul", "li", "blockquote", "span", "a"];

    private static readonly string[] Schemes = ["http", "https", "mailto"];

    private readonly HtmlSanitizer sanitizer = Build();

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        if (html.Length > IRichTextSanitizer.MaxLength)
        {
            throw new RichTextTooLongException(html.Length);
        }

        lock (sanitizer)
        {
            return sanitizer.Sanitize(html);
        }
    }

    private static HtmlSanitizer Build()
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(["href", "dir", "class", "rel", "target", "data-list"], StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(Schemes, StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
        };
        var sanitizer = new HtmlSanitizer(options)
        {
            KeepChildNodes = false,
        };

        // Classes: only the editor's alignment and direction markers.
        sanitizer.AllowedClasses.Clear();
        foreach (var name in new[] { "left", "center", "right", "justify" })
        {
            sanitizer.AllowedClasses.Add($"ql-align-{name}");
        }

        sanitizer.AllowedClasses.Add("ql-direction-rtl");

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement { LocalName: "a" } link)
            {
                // Absolute http, https or mailto only: protocol-relative ("//host") and relative links are dropped.
                if (link.GetAttribute("href") is { } href && !IsAllowedLink(href))
                {
                    link.RemoveAttribute("href");
                }

                link.SetAttribute("rel", "noopener noreferrer");
                link.SetAttribute("target", "_blank");
            }
        };

        // data-list (Quill bullets and checklists) only on li and only with the four editor values.
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement element && element.GetAttribute("data-list") is { } list
                && (element.LocalName != "li" || !ListKinds.Contains(list)))
            {
                element.RemoveAttribute("data-list");
            }
        };

        // dir may only be rtl, ltr or auto.
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement element && element.GetAttribute("dir") is { } dir
                && !DirPattern().IsMatch(dir))
            {
                element.RemoveAttribute("dir");
            }
        };

        return sanitizer;
    }

    private static readonly HashSet<string> ListKinds = new(["bullet", "ordered", "checked", "unchecked"], StringComparer.Ordinal);

    private static bool IsAllowedLink(string href) =>
        Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri) && Schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
        && (uri.Scheme == Uri.UriSchemeMailto || !href.TrimStart().StartsWith("//", StringComparison.Ordinal));

    [GeneratedRegex("^(rtl|ltr|auto)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirPattern();
}
