using System.Security.Cryptography;
using System.Text;
using Lms.Domain.Abstractions;

namespace Lms.Infrastructure.Zoom;

public enum ZoomVerification
{
    Valid,

    /// <summary>Missing, malformed or wrong signature (also a missing or unparseable timestamp: it is part of the signed message).</summary>
    BadSignature,

    /// <summary>Correctly signed, but the timestamp is outside the replay window.</summary>
    StaleTimestamp,
}

/// <summary>
/// Verifies Zoom webhook requests (D54): <c>x-zm-signature</c> = <c>v0=</c> + hex HMAC-SHA256 of <c>v0:{timestamp}:{raw body}</c> under
/// the webhook secret token, computed over the raw bytes before any JSON parsing and compared in constant time; then the timestamp
/// must be within five minutes of the server clock. Callers answer 401 for both failures without saying which.
/// </summary>
public sealed class ZoomWebhookVerifier(string secretToken, IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private const string Prefix = "v0=";

    public ZoomVerification Verify(ReadOnlySpan<byte> rawBody, string? timestampHeader, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(secretToken) || !long.TryParse(timestampHeader, out var seconds) || !TryReadSignature(signatureHeader, out var provided))
        {
            return ZoomVerification.BadSignature;
        }

        var prefix = Encoding.UTF8.GetBytes($"v0:{timestampHeader}:");
        var message = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(message, 0);
        rawBody.CopyTo(message.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretToken), message);
        if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        {
            return ZoomVerification.BadSignature;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        return (clock.UtcNow - sentAt).Duration() < Window ? ZoomVerification.Valid : ZoomVerification.StaleTimestamp;
    }

    /// <summary>The answer to the <c>endpoint.url_validation</c> challenge: hex HMAC-SHA256 of the plain token.</summary>
    public string EncryptPlainToken(string plainToken) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretToken), Encoding.UTF8.GetBytes(plainToken))).ToLowerInvariant();

    private static bool TryReadSignature(string? header, out byte[] signature)
    {
        signature = [];
        if (header is null || !header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            signature = Convert.FromHexString(header.AsSpan(Prefix.Length));
            return signature.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
