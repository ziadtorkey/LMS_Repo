using System.Globalization;
using System.Text.RegularExpressions;
using Lms.Client.Resources;
using Lms.Shared.Notifications;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Services;

/// <summary>
/// Renders a notification from resources: template <c>Notif_{EventKey with . → _}</c> with <c>{Name}</c> placeholders.
/// A parameter pair <c>NameAr</c>/<c>NameEn</c> fills <c>{Name}</c> with the variant of the given culture.
/// </summary>
public static partial class NotificationText
{
    public static string TemplateKey(string eventKey) => "Notif_" + eventKey.Replace('.', '_');
    public static string EventLabelKey(string eventKey) => "NotifEvent_" + eventKey.Replace('.', '_');

    public static string Format(NotificationDto n, IStringLocalizer<SharedResource> l, CultureInfo culture)
    {
        var template = l[TemplateKey(n.EventKey)];
        if (template.ResourceNotFound) return l["Notif_Unknown"];

        var arabic = culture.TwoLetterISOLanguageName == "ar";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in n.Parameters)
        {
            if (key.EndsWith("Ar", StringComparison.Ordinal) && key.Length > 2)
            {
                var name = key[..^2];
                if (arabic || !n.Parameters.ContainsKey(name + "En")) values[name] = value;
            }
            else if (key.EndsWith("En", StringComparison.Ordinal) && key.Length > 2)
            {
                var name = key[..^2];
                if (!arabic || !n.Parameters.ContainsKey(name + "Ar")) values[name] = value;
            }
            else
            {
                values[key] = value;
            }
        }
        return Placeholder().Replace(template.Value, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
