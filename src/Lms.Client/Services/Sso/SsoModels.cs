using Lms.Client.Services.Api;
using Lms.Shared;

namespace Lms.Client.Services.Sso;

// M7 H7 (FE proposal #808, D85): SAML 2.0 and LDAP providers. PROVISIONAL: the SSO contract (lead/m7-contracts-2) is not written yet, so these types and the fake live in the
// client; when the contract lands only this file, the HTTP client and the fake change, the dialog keeps its shape.

public static class SsoKinds
{
    public const string Saml = "Saml";
    public const string Ldap = "Ldap";

    public static IReadOnlyList<string> All { get; } = [Saml, Ldap];
}

/// <summary>D85: LDAP search-bind over LDAPS or StartTLS only; there is no unencrypted option.</summary>
public static class LdapTransports
{
    public const string Ldaps = "Ldaps";
    public const string StartTls = "StartTls";

    public static IReadOnlyList<string> All { get; } = [Ldaps, StartTls];
}

public static class SsoLimits
{
    public const int MetadataMaxBytes = 512 * 1024;
    public const int CertWarnDays = 30;
}

public sealed record SpValuesDto(string AcsUrl, string EntityId, string MetadataUrl);

/// <summary>SAML settings as the server returns them: the certificate fingerprint and expiry are read only after the metadata was imported.</summary>
public sealed record SamlSettingsDto(
    string MetadataUrl, string EntityId, string SsoUrl, string? CertFingerprint, DateOnly? CertExpires, string NameIdFormat, string EmailAttribute, string NationalIdAttribute);

/// <summary>LDAP settings as the server returns them. <c>BindPasswordStored</c> is all the client ever learns about the password.</summary>
public sealed record LdapSettingsDto(
    string Host, int Port, string Transport, string BindDn, bool BindPasswordStored, string SearchBase, string UserFilter, string EmailAttribute, string NationalIdAttribute);

public sealed record SsoProviderDto(Guid Id, string Kind, string Slug, string DisplayNameAr, string DisplayNameEn, bool Enabled, SamlSettingsDto? Saml, LdapSettingsDto? Ldap, SpValuesDto? Sp);

/// <summary>The write model: the bind password and the metadata XML are write-only; empty password = keep the stored one.</summary>
public sealed record SaveSsoProviderRequest(
    string Kind, string Slug, string DisplayNameAr, string DisplayNameEn, bool Enabled, SamlSettingsDto? Saml, string? SamlMetadataXml, LdapSettingsDto? Ldap, string? BindPassword);

public sealed record SsoCheckDto(string Key, bool Ok, string? Detail);

/// <summary>The checks of a connection test, each in plain words; never a stack trace, never a secret.</summary>
public sealed record SsoTestResultDto(bool Ok, IReadOnlyList<SsoCheckDto> Checks);

public interface ISsoProvidersApi
{
    Task<SsoProviderDto> CreateAsync(SaveSsoProviderRequest request, CancellationToken ct = default);
    Task<SsoProviderDto> UpdateAsync(Guid id, SaveSsoProviderRequest request, CancellationToken ct = default);
    Task<SsoTestResultDto> TestAsync(SaveSsoProviderRequest request, CancellationToken ct = default);
}

/// <summary>
/// In-memory SSO providers for the standalone dev server and tests: a transport other than LDAPS or StartTLS is 400 (D85), the bind password is stored and never returned
/// (<see cref="Stored"/> is the test's window), uploaded metadata yields a fingerprint and an expiry, and the connection test says which check failed in plain words.
/// </summary>
public sealed class FakeSsoProvidersApi : ISsoProvidersApi
{
    private readonly Dictionary<Guid, string?> _passwords = [];

    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public List<SaveSsoProviderRequest> Saved { get; } = [];

    /// <summary>What the server holds as the bind password of a provider (a test checks it was sent, and never shown).</summary>
    public string? Stored(Guid id) => _passwords.GetValueOrDefault(id);

    public Task<SsoProviderDto> CreateAsync(SaveSsoProviderRequest request, CancellationToken ct = default) => Save(Guid.NewGuid(), request);

    public Task<SsoProviderDto> UpdateAsync(Guid id, SaveSsoProviderRequest request, CancellationToken ct = default) => Save(id, request);

    private Task<SsoProviderDto> Save(Guid id, SaveSsoProviderRequest r)
    {
        if (r.Kind == SsoKinds.Ldap)
        {
            if (r.Ldap is null || !LdapTransports.All.Contains(r.Ldap.Transport)) throw new ApiProblemException(400, ProblemCodes.validation_failed, null, null, "Transport");
            if (!string.IsNullOrEmpty(r.BindPassword)) _passwords[id] = r.BindPassword;
        }
        Saved.Add(r);
        var saml = r.Saml is null ? null : r.Saml with { CertFingerprint = r.SamlMetadataXml is null ? r.Saml.CertFingerprint : Fingerprint(), CertExpires = r.SamlMetadataXml is null ? r.Saml.CertExpires : Expiry(r.SamlMetadataXml) };
        var ldap = r.Ldap is null ? null : r.Ldap with { BindPasswordStored = _passwords.ContainsKey(id) };
        var sp = new SpValuesDto($"https://lms.test/saml/{r.Slug}/acs", $"https://lms.test/saml/{r.Slug}", $"https://lms.test/saml/{r.Slug}/metadata");
        return Task.FromResult(new SsoProviderDto(id, r.Kind, r.Slug, r.DisplayNameAr, r.DisplayNameEn, r.Enabled, saml, ldap, sp));
    }

    private static string Fingerprint() => "3F:A9:0C:7B:5D:21:E4:88:19:C2:6A:F0:34:B7:9E:D1:42:AB:0F:66";

    private DateOnly Expiry(string xml) => DateOnly.FromDateTime(Now().AddDays(xml.Contains("expiring", StringComparison.OrdinalIgnoreCase) ? 17 : 400));

    public Task<SsoTestResultDto> TestAsync(SaveSsoProviderRequest request, CancellationToken ct = default)
    {
        List<SsoCheckDto> checks;
        if (request.Kind == SsoKinds.Ldap)
        {
            var bad = request.Ldap?.Host.Contains("bad", StringComparison.OrdinalIgnoreCase) == true;
            checks =
            [
                new("Reachable", !bad, bad ? request.Ldap!.Host : null),
                new("Encrypted", !bad, null),
                new("Bind", !bad, null),
                new("Search", !bad, null),
            ];
        }
        else
        {
            var missing = string.IsNullOrWhiteSpace(request.Saml?.SsoUrl);
            checks = [new("Metadata", !missing, null), new("Certificate", !missing, null), new("SignedAssertions", true, null)];
        }
        return Task.FromResult(new SsoTestResultDto(checks.All(c => c.Ok), checks));
    }
}
