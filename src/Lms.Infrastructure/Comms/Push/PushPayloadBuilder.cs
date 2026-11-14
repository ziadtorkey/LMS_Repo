using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Lms.Infrastructure.Comms.Push;

/// <summary>
/// Builds the Web Push payload (M6 plan Task 8, L9, L22): a short title and body written by the caller from a template (never message text or a grade), a
/// local deep link and ids only in <c>data</c>. The result is at most <see cref="MaxBytes"/> (the push services allow about 4 KB; 2 KB keeps headroom),
/// cut on a character boundary. A link that is not a local path is replaced by <c>/</c> so a push can never send the user to another site.
/// </summary>
public static partial class PushPayloadBuilder
{
    public const int MaxBytes = 2048;

    private const int MaxText = 200;
    private const int MaxLink = 300;

    public static byte[] Build(string eventKey, Guid deliveryId, string title, string body, string? deepLink)
    {
        var text = (Title: Cut(title, MaxText), Body: Cut(body, MaxText), Url: LocalLink(deepLink));
        var bytes = Serialize(eventKey, deliveryId, text.Title, text.Body, text.Url);
        // the fixed fields are small, so shrinking the two texts is always enough
        for (var limit = MaxText; bytes.Length > MaxBytes && limit > 0; limit -= 10)
        {
            bytes = Serialize(eventKey, deliveryId, Cut(text.Title, limit), Cut(text.Body, limit), text.Url);
        }

        return bytes;
    }

    private static byte[] Serialize(string eventKey, Guid deliveryId, string title, string body, string url) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            title,
            body,
            url,
            data = new { eventKey, deliveryId },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // A browser drops tab, newline and carriage return when it parses a URL, so only a short, fixed set of path characters is accepted.
    [GeneratedRegex(@"^/[A-Za-z0-9/_\-.~%?=&#:+,]*$")]
    private static partial Regex SafeLink();

    private static string LocalLink(string? link)
    {
        if (string.IsNullOrEmpty(link) || link.Length > MaxLink || !SafeLink().IsMatch(link)
            || link.Contains("//", StringComparison.Ordinal) || link.Contains("%2f", StringComparison.OrdinalIgnoreCase) || link.Contains("%5c", StringComparison.OrdinalIgnoreCase)
            || link.Split('?', '#')[0].Split('/').Any(segment => segment is "." or ".."))
        {
            return "/";
        }

        return link;
    }

    private static string Cut(string value, int maxChars) =>
        value.Length <= maxChars ? value : new StringBuilder(value[..maxChars]).ToString();
}
