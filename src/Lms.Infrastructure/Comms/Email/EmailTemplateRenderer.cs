using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Lms.Domain.Comms;

namespace Lms.Infrastructure.Comms.Email;

public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

/// <summary>Raised when a template has no subject or body in any language: a programming error found by the parity test, never a user error.</summary>
public sealed class EmailTemplateMissingException(string templateKey) : Exception($"No email template '{templateKey}'.");

/// <summary>
/// Renders an email from resources (M6 L22): subject and body keys <c>Email_{Template}_Subject</c> / <c>_Body</c> with <c>{Name}</c> placeholders, in the
/// recipient's language (Arabic unless English is asked for), as HTML (right to left for Arabic, every value encoded) and a plain-text alternative.
/// A parameter pair <c>NameAr</c>/<c>NameEn</c> fills <c>{Name}</c> with the variant of the language. Message text is never a parameter: names that would carry
/// it are refused, so an email says that something happened and where to read it (L22, D66).
/// </summary>
public sealed partial class EmailTemplateRenderer(IEmailTemplateText text)
{
    /// <summary>Parameter names that would carry message or announcement content into an email.</summary>
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase) { "Body", "Text", "Message", "MessageText", "Content" };

    /// <summary>A <c>Link</c> parameter becomes a button-style link in HTML and a line in the text alternative.</summary>
    public const string LinkParameter = "Link";

    public static string SubjectKey(string template) => $"Email_{template}_Subject";

    public static string BodyKey(string template) => $"Email_{template}_Body";

    public RenderedEmail Render(string template, string language, IReadOnlyDictionary<string, string> parameters)
    {
        if (parameters.Keys.FirstOrDefault(Forbidden.Contains) is { } forbidden)
        {
            throw new ArgumentException($"The parameter '{forbidden}' would carry message text into an email.", nameof(parameters));
        }

        var lang = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ar";
        var subjectTemplate = text.Get(SubjectKey(template), lang);
        var bodyTemplate = text.Get(BodyKey(template), lang);
        if (subjectTemplate is null || bodyTemplate is null)
        {
            throw new EmailTemplateMissingException(template);
        }

        var values = Resolve(parameters, lang);
        var subject = Fill(subjectTemplate, values, encode: false).Replace('\r', ' ').Replace('\n', ' ').Trim();
        var plain = Fill(bodyTemplate, values, encode: false);
        var html = Fill(bodyTemplate, values, encode: true);
        values.TryGetValue(LinkParameter, out var link);

        var htmlBuilder = new StringBuilder();
        htmlBuilder.Append("<!DOCTYPE html><html lang=\"").Append(lang).Append("\" dir=\"").Append(lang == "ar" ? "rtl" : "ltr").Append("\"><body>");
        foreach (var line in html.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            htmlBuilder.Append("<p>").Append(line.Trim()).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(link))
        {
            var encoded = WebUtility.HtmlEncode(link);
            htmlBuilder.Append("<p><a href=\"").Append(encoded).Append("\">").Append(encoded).Append("</a></p>");
        }

        htmlBuilder.Append("</body></html>");
        var textBody = string.IsNullOrWhiteSpace(link) ? plain.Trim() : plain.Trim() + "\n\n" + link;
        return new RenderedEmail(subject, htmlBuilder.ToString(), textBody);
    }

    private static Dictionary<string, string> Resolve(IReadOnlyDictionary<string, string> parameters, string lang)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            if (key.Length > 2 && key.EndsWith("Ar", StringComparison.Ordinal))
            {
                var name = key[..^2];
                if (lang == "ar" || !parameters.ContainsKey(name + "En"))
                {
                    values[name] = value;
                }
            }
            else if (key.Length > 2 && key.EndsWith("En", StringComparison.Ordinal))
            {
                var name = key[..^2];
                if (lang == "en" || !parameters.ContainsKey(name + "Ar"))
                {
                    values[name] = value;
                }
            }
            else
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string Fill(string template, Dictionary<string, string> values, bool encode) =>
        encode
            ? Placeholder().Replace(WebUtility.HtmlEncode(template), m => values.TryGetValue(m.Groups[1].Value, out var v) ? WebUtility.HtmlEncode(v) : m.Value)
            : Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
