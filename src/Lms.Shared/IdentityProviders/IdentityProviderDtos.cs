namespace Lms.Shared.IdentityProviders;

/// <summary>Kinds of client sign-in providers (FR-SEC-08, D33).</summary>
public static class IdentityProviderKinds
{
    public const string Oidc = "Oidc";
    public const string AzureAd = "AzureAd";

    /// <summary>M7 wave 2 (D85): SAML 2.0, SP-initiated only. The settings are <see cref="SamlSettingsDto"/>.</summary>
    public const string Saml = "Saml";

    /// <summary>M7 wave 2 (D85): LDAP search-bind over LDAPS or StartTLS only. The settings are <see cref="LdapSettingsDto"/>.</summary>
    public const string Ldap = "Ldap";

    public static IReadOnlyList<string> All { get; } = [Oidc, AzureAd, Saml, Ldap];
}

/// <summary>How an external identity is matched to a person of the provider's client (no auto-provisioning).</summary>
public static class ExternalMatchBy
{
    public const string Email = "Email";
    public const string NationalId = "NationalId";

    public static IReadOnlyList<string> All { get; } = [Email, NationalId];
}

/// <summary>A client's sign-in provider. The client secret is never returned (<c>HasClientSecret</c> only).</summary>
public sealed record IdentityProviderDto(
    Guid Id,
    string Kind,
    string Slug,
    string DisplayNameAr,
    string DisplayNameEn,
    string Authority,
    string? AzureTenantId,
    string ClientId,
    bool HasClientSecret,
    string Scopes,
    string MatchBy,
    string? NationalIdClaim,
    IReadOnlyList<string> EmailDomains,
    bool Enabled,
    string CallbackUrl,
    SamlSettingsDto? Saml = null,
    LdapSettingsDto? Ldap = null);

/// <summary>
/// Body of POST /api/identity-providers and PUT /api/identity-providers/{id}. On update a null
/// <c>ClientSecret</c> keeps the stored secret. <c>Authority</c> may be null for <c>AzureAd</c> (built from the tenant id).
/// </summary>
public sealed record SaveIdentityProviderRequest(
    string Kind,
    string Slug,
    string DisplayNameAr,
    string DisplayNameEn,
    string? Authority,
    string? AzureTenantId,
    string ClientId,
    string? ClientSecret,
    string Scopes,
    string MatchBy,
    string? NationalIdClaim,
    IReadOnlyList<string> EmailDomains,
    bool Enabled,
    SamlSettingsRequest? Saml = null,
    LdapSettingsRequest? Ldap = null);

/// <summary>
/// Result of POST /api/identity-providers/{id}/test. Oidc and AzureAd: the discovery document fetch, always 200 with <c>Ok</c> (unchanged). Saml and Ldap (M7): 200 when every check passes; otherwise
/// <b>422 <c>provider_test_failed</c></b> whose problem body carries this DTO in <c>extra</c> with <c>Ok</c> false and the named <c>Checks</c> (<see cref="ProviderTestCheckCodes"/>); never a secret, an address or a server message.
/// </summary>
public sealed record ProviderTestResultDto(bool Ok, string? Issuer, string? Error, IReadOnlyList<ProviderTestCheckDto>? Checks = null);
