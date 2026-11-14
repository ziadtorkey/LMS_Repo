using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Sms;

/// <summary>
/// The only SMS texts that exist (D66, D68): fixed, generic and bilingual, with no student data, no grade, no attendance and no message text. A template names the
/// parameters it accepts; a message with another parameter, a missing one, an over-long value or an unknown template is refused before it reaches a provider.
/// </summary>
public static class SmsTemplates
{
    public const string GenericNotice = "generic-notice";

    public const int MaxParameterLength = 60;

    private sealed record Template(string Ar, string En, string[] Parameters);

    private static readonly Dictionary<string, Template> Catalog = new(StringComparer.Ordinal)
    {
        [GenericNotice] = new(
            "لديك إشعار جديد في {AppName}. افتح التطبيق للاطلاع عليه.",
            "You have a new notification in {AppName}. Open the app to read it.",
            ["AppName"]),
    };

    public static IReadOnlyCollection<string> Keys => Catalog.Keys;

    /// <summary>The text in the message language (<c>ar</c> or <c>en</c>; anything else is English); throws <see cref="ArgumentException"/> when the message breaks the rules.</summary>
    public static string Render(SmsMessage message)
    {
        Validate(message);
        var template = Catalog[message.TemplateKey];
        var text = message.Language == "ar" ? template.Ar : template.En;
        foreach (var name in template.Parameters)
        {
            text = text.Replace("{" + name + "}", message.Parameters[name], StringComparison.Ordinal);
        }

        OutboundContentPolicy.EnsureSafe(text); // the rendered text too: a parameter value cannot smuggle a grade in
        return text;
    }

    public static void Validate(SmsMessage message)
    {
        if (!Catalog.TryGetValue(message.TemplateKey, out var template))
        {
            throw new ArgumentException("Unknown SMS template.", nameof(message));
        }

        if (!IsE164(message.To))
        {
            throw new ArgumentException("The recipient must be an E.164 number.", nameof(message));
        }

        if (message.Parameters.Keys.Any(k => !template.Parameters.Contains(k)) || template.Parameters.Any(p => !message.Parameters.ContainsKey(p)))
        {
            throw new ArgumentException("The parameters do not match the template.", nameof(message));
        }

        if (message.Parameters.Values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > MaxParameterLength || v.Any(char.IsControl)))
        {
            throw new ArgumentException("A template parameter is empty, too long or has control characters.", nameof(message));
        }
    }

    private static bool IsE164(string? number) =>
        number is { Length: >= 8 and <= 16 } && number[0] == '+' && number[1] != '0' && number.Skip(1).All(char.IsAsciiDigit);
}
