using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Lms.Infrastructure.Identity.External;

/// <summary>Encrypts external provider client secrets at rest (X-08). The purpose string is part of the key derivation: never change it.</summary>
public sealed class ProviderSecretProtector(IDataProtectionProvider provider)
{
    public const string Purpose = "Lms.IdentityProviders.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string secret) => _protector.Protect(secret);

    /// <summary>The secret, or null when the payload cannot be decrypted (rotated-away keys, tampering).</summary>
    public string? Unprotect(string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
