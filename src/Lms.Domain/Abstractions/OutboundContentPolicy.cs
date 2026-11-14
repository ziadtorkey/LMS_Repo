using System.Text.RegularExpressions;

namespace Lms.Domain.Abstractions;

/// <summary>Thrown when an outbound notification message would carry sensitive data (grades, attendance, scores).</summary>
public sealed class SensitiveContentException(string reason) : InvalidOperationException(reason);

/// <summary>
/// Defense in depth for outbound messages (D66: no student data or message text in push; notification emails stay generic; D76: there is no SMS in v1). The primary rule is that
/// notification messages are built from generic templates; this check refuses a message whose text still mentions a grade, a score, an attendance status or a
/// percentage, so a template mistake fails loudly in tests and never reaches a provider. It is a deny list in English and Arabic, not a classifier: it cannot prove a
/// text is safe, only catch the obvious leaks.
/// </summary>
public static partial class OutboundContentPolicy
{
    [GeneratedRegex(
        @"\b(grades?|marks?|scores?|gpa|attendance|absent|absence|late|tardy|excused|failed|passed|percent(age)?)\b|\d+(\.\d+)?\s*%|(ال)?درج(ة|ات)|(ال)?علام(ة|ات)|(ال)?غياب|غائب|متأخر|(ال)?تأخر|(ال)?نسبة|(ال)?معدل|راسب|ناجح",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sensitive();

    /// <summary>The first sensitive fragment of the text, or null when none was found.</summary>
    public static string? FindSensitive(string? text) => string.IsNullOrEmpty(text) ? null : Sensitive().Match(text) is { Success: true } m ? m.Value : null;

    /// <summary>Throws <see cref="SensitiveContentException"/> (without echoing the text) when any of the parts mentions sensitive data.</summary>
    public static void EnsureSafe(params string?[] parts)
    {
        foreach (var part in parts)
        {
            if (FindSensitive(part) is not null)
            {
                throw new SensitiveContentException("The message mentions sensitive data (grade, score or attendance) and was not sent.");
            }
        }
    }
}
