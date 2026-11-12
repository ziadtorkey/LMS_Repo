using System.Text.RegularExpressions;
using Ganss.Xss;
using Lms.Domain.Content;

namespace Lms.Infrastructure.Content;

/// <summary>
/// The one allow-list of rich text (D48): p, br, strong, em, u, s, h2-h4, ol, ul, li, blockquote, a (http, https, mailto,
/// forced rel and target), span and p with <c>dir</c> and the Quill alignment / direction classes. No images, frames, styles,
/// scripts, event attributes, or javascript / data URLs. Applied on write; the client renders only what the server stored.
/// </summary>
public sealed partial class RichTextSanitizer : IRichTextSanitizer
{
    [GeneratedRegex("^(ql-align-(left|right|center|justify)|ql-direction-rtl)$")]
    private static partial Regex ClassRegex();

    private readonly HtmlSanitizer _sanitizer = Build();

    public string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var clean = _sanitizer.Sanitize(html).Trim();
        return clean.Length == 0 ? null : clean;
    }

    private static HtmlSanitizer Build()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "br", "strong", "em", "u", "s", "h2", "h3", "h4", "ol", "ul", "li", "blockquote", "a", "span"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "dir", "class", "rel", "target"]);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https", "mailto"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedClasses.Clear();
        sanitizer.AllowedClasses.UnionWith(["ql-align-left", "ql-align-right", "ql-align-center", "ql-align-justify", "ql-direction-rtl"]);
        sanitizer.AllowDataAttributes = false;
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement element)
            {
                var classes = element.GetAttribute("class");
                if (classes is not null)
                {
                    var kept = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(c => ClassRegex().IsMatch(c)).ToArray();
                    if (kept.Length == 0)
                    {
                        element.RemoveAttribute("class");
                    }
                    else
                    {
                        element.SetAttribute("class", string.Join(' ', kept));
                    }
                }

                var dir = element.GetAttribute("dir");
                if (dir is not null && dir is not ("rtl" or "ltr" or "auto"))
                {
                    element.RemoveAttribute("dir");
                }

                if (element.LocalName == "a")
                {
                    element.SetAttribute("rel", "noopener noreferrer");
                    element.SetAttribute("target", "_blank");
                }
                else
                {
                    element.RemoveAttribute("rel");
                    element.RemoveAttribute("target");
                }
            }
        };
        return sanitizer;
    }
}
