using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Lms.Infrastructure.Identity.External;

/// <summary>
/// Protects the Data Protection key ring at rest (#186). The keys live in the same database as the encrypted provider
/// secrets, so without this a database dump would be enough to decrypt them. Outside Development a certificate is
/// mandatory and the host refuses to start without one; Development keeps the plain key ring.
/// </summary>
/// <remarks>
/// Configuration (environment variables or secret store, never committed):
/// <c>DataProtection:Certificate:Path</c> or <c>:Base64</c> (a PFX) with <c>:Password</c>; for rotation, the certificates the
/// existing keys were protected with go under <c>DataProtection:PreviousCertificates:{n}</c> with the same three settings.
/// </remarks>
public static class KeyProtection
{
    public const string CertificateSection = "DataProtection:Certificate";
    public const string PreviousCertificatesSection = "DataProtection:PreviousCertificates";

    public static IDataProtectionBuilder Apply(IDataProtectionBuilder builder, IConfiguration configuration, bool isDevelopment)
    {
        var current = LoadCertificate(configuration, CertificateSection);
        if (current is null)
        {
            return isDevelopment
                ? builder
                : throw new InvalidOperationException(
                    $"The Data Protection key ring must be protected outside the Development environment: set {CertificateSection}:Path "
                    + $"(or {CertificateSection}:Base64) and {CertificateSection}:Password to a PFX with a private key. "
                    + "See docs/runbook-key-protection.md.");
        }

        var previous = configuration.GetSection(PreviousCertificatesSection).GetChildren()
            .Select(child => LoadCertificate(configuration, child.Path))
            .OfType<X509Certificate2>();

        // New keys are protected with the current certificate; keys written under an older one stay readable until re-protected.
        return builder.ProtectKeysWithCertificate(current).UnprotectKeysWithAnyCertificate([current, .. previous]);
    }

    /// <summary>The certificate configured under <paramref name="section"/>, or null when none is configured.</summary>
    public static X509Certificate2? LoadCertificate(IConfiguration configuration, string section)
    {
        var path = configuration[$"{section}:Path"];
        var base64 = configuration[$"{section}:Base64"];
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        var password = configuration[$"{section}:Password"];
        X509Certificate2 certificate;
        try
        {
            certificate = !string.IsNullOrWhiteSpace(path)
                ? X509CertificateLoader.LoadPkcs12FromFile(path, password)
                : X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64!), password);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or FormatException or UnauthorizedAccessException)
        {
            // The message names the setting and file, never the password.
            throw new InvalidOperationException(
                $"The key-protection certificate in {section} could not be loaded ({ex.GetType().Name}); "
                + (string.IsNullOrWhiteSpace(path) ? "check the Base64 value and Password." : $"check the file '{path}' and Password."), ex);
        }

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException($"The key-protection certificate in {section} has no private key; provide a PFX that includes it.");
        }

        if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
        {
            throw new InvalidOperationException($"The key-protection certificate in {section} has expired ({certificate.NotAfter:u}); rotate it (see docs/runbook-key-protection.md).");
        }

        return certificate;
    }
}
