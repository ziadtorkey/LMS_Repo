using Microsoft.AspNetCore.DataProtection;

namespace Lms.Infrastructure.Comms.Push;

/// <summary>Protects a subscription's two keys at rest (Data Protection). The purpose string is part of the key derivation: never change it.</summary>
public sealed class PushKeyProtector(IDataProtectionProvider provider)
{
    public const string Purpose = "Lms.PushSubscriptions.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string value) => _protector.Protect(value);

    /// <summary>The key, or null when it cannot be decrypted (rotated-away keys, tampering): the subscription is then unusable.</summary>
    public string? Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
