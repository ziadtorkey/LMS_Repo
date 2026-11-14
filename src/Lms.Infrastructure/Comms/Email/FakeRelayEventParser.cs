using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Domain.Comms;

namespace Lms.Infrastructure.Comms.Email;

/// <summary>
/// The webhook shape of the development relay (D67), which the real relay's mapping replaces before go-live. The relay signs <c>{timestamp}.{body}</c> with
/// HMAC-SHA256 and the shared secret (<c>X-Relay-Signature</c>, hex; <c>X-Relay-Timestamp</c>, Unix seconds); the body is
/// <c>{"events":[{"id","type","messageId","address","at"}]}</c> with type <c>delivered</c>, <c>soft_bounce</c>, <c>hard_bounce</c> or <c>complaint</c>.
/// Order of checks: size, signature (constant time), timestamp window, then the body is read. An unsigned or stale request reveals nothing.
/// </summary>
public sealed class FakeRelayEventParser(string? secret, TimeSpan? tolerance = null) : IEmailEventParser
{
    public const int DefaultMaxBodyBytes = 64 * 1024;

    private readonly TimeSpan _tolerance = tolerance ?? TimeSpan.FromMinutes(5);

    public int MaxBodyBytes => DefaultMaxBodyBytes;

    /// <summary>The signature the relay would send, for tests and the development trigger.</summary>
    public static string Sign(string secret, string timestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var data = new byte[prefix.Length + body.Length];
        prefix.CopyTo(data, 0);
        body.CopyTo(data.AsSpan(prefix.Length));
        return Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data));
    }

    public EmailEventParseResult Parse(string? timestampHeader, string? signatureHeader, ReadOnlySpan<byte> body, DateTime nowUtc)
    {
        if (body.Length > MaxBodyBytes)
        {
            return EmailEventParseResult.Failed(EmailEventParseStatus.TooLarge);
        }

        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(timestampHeader))
        {
            return EmailEventParseResult.Failed(EmailEventParseStatus.BadSignature); // no secret configured: nothing is accepted
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, timestampHeader, body));
        var given = Encoding.ASCII.GetBytes(signatureHeader.Trim().ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(expected, given))
        {
            return EmailEventParseResult.Failed(EmailEventParseStatus.BadSignature);
        }

        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs((nowUtc - DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime).TotalSeconds) > _tolerance.TotalSeconds)
        {
            return EmailEventParseResult.Failed(EmailEventParseStatus.Stale);
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var events = new List<EmailEvent>();
            foreach (var element in document.RootElement.GetProperty("events").EnumerateArray())
            {
                var kind = element.GetProperty("type").GetString() switch
                {
                    "delivered" => EmailEventKind.Delivered,
                    "soft_bounce" => EmailEventKind.SoftBounce,
                    "hard_bounce" => EmailEventKind.HardBounce,
                    "complaint" => EmailEventKind.Complaint,
                    _ => (EmailEventKind?)null,
                };
                if (kind is null)
                {
                    continue; // an event type this version does not know is skipped, not an error
                }

                events.Add(new EmailEvent(
                    element.GetProperty("id").GetString() ?? throw new FormatException(),
                    kind.Value,
                    element.GetProperty("messageId").GetString() ?? throw new FormatException(),
                    element.TryGetProperty("address", out var address) ? address.GetString() : null,
                    element.GetProperty("at").GetDateTime().ToUniversalTime()));
            }

            return new EmailEventParseResult(EmailEventParseStatus.Ok, events);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return EmailEventParseResult.Failed(EmailEventParseStatus.Malformed);
        }
    }
}
