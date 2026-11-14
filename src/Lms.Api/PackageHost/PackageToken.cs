using System.Text.Json;
using Lms.Infrastructure.Packages;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Lms.Api.PackageHost;

/// <summary>
/// What a package token says (ADR 0005 section 5). <see cref="Unit"/> is the org-unit path of the package at issue time: together with
/// <see cref="Org"/> it is the scope of the system context the host reads under, so a token reaches one client's package only.
/// </summary>
public sealed record PackageTokenClaims(
    Guid Org, string Unit, Guid Package, Guid? Student, Guid? Attempt, bool Preview, string Aud, long Iat, long Exp);

/// <summary>
/// Issues and verifies the path token of the package host. The payload is protected with a Data Protection protector whose purpose is
/// unique to the content host (authenticated encryption under a key no other purpose shares, so a token of another purpose, a forged,
/// altered or malformed one never verifies); expiry and audience are checked on top. Every failure is the same <c>null</c>.
/// </summary>
public sealed class PackageTokenService(IDataProtectionProvider protection, IOptions<PackageHostOptions> options)
{
    /// <summary>The Data Protection purpose of package-host tokens.</summary>
    public const string Purpose = "Lms.PackageHost.Token.v1";

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>The audience of every token: the content origin, normalised.</summary>
    public string Audience => Normalise(options.Value.Origin);

    public string Issue(
        Guid org, string unitPath, Guid package, Guid? student, Guid? attempt, bool preview, TimeSpan lifetime, DateTimeOffset now)
    {
        var claims = new PackageTokenClaims(org, unitPath, package, student, attempt, preview, Audience, now.ToUnixTimeSeconds(), (now + lifetime).ToUnixTimeSeconds());
        return Base64Url(_protector.Protect(JsonSerializer.SerializeToUtf8Bytes(claims)));
    }

    public PackageTokenClaims? Verify(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2048)
        {
            return null;
        }

        try
        {
            var claims = JsonSerializer.Deserialize<PackageTokenClaims>(_protector.Unprotect(FromBase64Url(token)));
            if (claims is null || claims.Exp <= now.ToUnixTimeSeconds() || claims.Iat > now.ToUnixTimeSeconds() + 60
                || !string.Equals(claims.Aud, Audience, StringComparison.Ordinal)
                || claims.Unit.Length < 3 || claims.Unit[0] != '/' || claims.Unit[^1] != '/')
            {
                return null;
            }

            return claims;
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    internal static string Normalise(string? origin) =>
        Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() : string.Empty;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }
}
