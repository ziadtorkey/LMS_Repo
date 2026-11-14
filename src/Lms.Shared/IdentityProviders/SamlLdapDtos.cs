namespace Lms.Shared.IdentityProviders;

// M7 wave 2: SAML 2.0 (SP-initiated only) and LDAP (search-bind over LDAPS or StartTLS only) providers (FR-SEC-08 part 2, D85, D18).
// They sit beside OIDC and Azure AD in the same /api/identity-providers resource (client-root Org Admin, IdentityProviders.View/Manage), match people by email or national id
// (ExternalMatchBy) and never map groups to roles (D85). **No DTO ever returns a secret**: the LDAP bind password and a custom CA are write-only (HasBindPassword, HasCustomCa); signing
// certificates are public data and are shown as thumbprint, subject and expiry only. For the Saml and Ldap kinds the OIDC fields (Authority, ClientId, Scopes, AzureTenantId) are
// sent empty and ignored; MatchBy, NationalIdClaim (the attribute name), EmailDomains and Enabled keep their meaning.

/// <summary>How the LDAP connection is secured. Plain <c>ldap://</c> is not an option: a configuration that cannot use TLS is refused on save and at connect (D85).</summary>
public static class LdapTlsModes
{
    public const string Ldaps = "Ldaps";
    public const string StartTls = "StartTls";
    public static IReadOnlyList<string> All { get; } = [Ldaps, StartTls];
}

/// <summary>The NameID format the SP asks the IdP for.</summary>
public static class SamlNameIdFormats
{
    public const string EmailAddress = "EmailAddress";
    public const string Persistent = "Persistent";
    public const string Unspecified = "Unspecified";
    public static IReadOnlyList<string> All { get; } = [EmailAddress, Persistent, Unspecified];
}

public static class SamlLdapLimits
{
    public const int ClockSkewDefaultSeconds = 120;
    public const int ClockSkewMaxSeconds = 300;
    public const int MaxSigningCertificates = 3;
    public const int LdapPortMin = 1;
    public const int LdapPortMax = 65535;
    public const int LdapTimeoutDefaultSeconds = 10;
    public const int LdapTimeoutMinSeconds = 3;
    public const int LdapTimeoutMaxSeconds = 30;
    public const int FilterMaxLength = 500;
    public const int DnMaxLength = 500;
    public const int AttributeMaxLength = 100;
    public const int UrlMaxLength = 500;

    /// <summary>The marker the LDAP user search filter must contain; the typed user name replaces it escaped (RFC 4515), never concatenated.</summary>
    public const string FilterUserPlaceholder = "{0}";
}

/// <summary>A public signing certificate of the IdP: what the admin may check against the IdP's own metadata. Never the PEM, never a private key.</summary>
public sealed record CertificateInfoDto(string Thumbprint, string Subject, DateTime NotAfterUtc);

/// <summary>
/// SAML settings of a provider as read. <c>SpEntityId</c> and <c>AcsUrl</c> are what the IdP admin enters on their side (also in <see cref="SamlSpMetadataDto"/>).
/// Signed assertions are always required and there is no toggle (D85); up to <see cref="SamlLdapLimits.MaxSigningCertificates"/> certificates may be trusted at once so a rotation needs no outage.
/// </summary>
public sealed record SamlSettingsDto(
    string IdpEntityId,
    string SsoUrl,
    IReadOnlyList<CertificateInfoDto> SigningCertificates,
    string NameIdFormat,
    string EmailAttribute,
    string? NationalIdAttribute,
    int ClockSkewSeconds,
    string SpEntityId,
    string AcsUrl);

/// <summary>SAML settings as written. <c>SigningCertificatesPem</c> are public certificates (PEM); on update null keeps the stored ones, a list replaces them (1 to <see cref="SamlLdapLimits.MaxSigningCertificates"/>, 422 <c>validation_failed</c> on an unreadable or expired one).</summary>
public sealed record SamlSettingsRequest(
    string IdpEntityId,
    string SsoUrl,
    IReadOnlyList<string>? SigningCertificatesPem,
    string NameIdFormat,
    string EmailAttribute,
    string? NationalIdAttribute,
    int ClockSkewSeconds);

/// <summary>LDAP settings as read. The bind password and a custom CA are write-only: only <c>HasBindPassword</c> and <c>HasCustomCa</c> are returned.</summary>
public sealed record LdapSettingsDto(
    string Host,
    int Port,
    string TlsMode,
    string BaseDn,
    string BindDn,
    bool HasBindPassword,
    string UserSearchFilter,
    string EmailAttribute,
    string? NationalIdAttribute,
    string? DisplayNameAttribute,
    int TimeoutSeconds,
    bool HasCustomCa);

/// <summary>
/// LDAP settings as written. <c>UserSearchFilter</c> must contain <see cref="SamlLdapLimits.FilterUserPlaceholder"/>; <c>Host</c> goes through the outbound guard (no private address outside development).
/// On update a null <c>BindPassword</c> keeps the stored one; <c>CustomCaCertificatePem</c> null keeps, an empty string removes, a PEM (public CA certificate) replaces. Certificate validation is always on.
/// </summary>
public sealed record LdapSettingsRequest(
    string Host,
    int Port,
    string TlsMode,
    string BaseDn,
    string BindDn,
    string? BindPassword,
    string UserSearchFilter,
    string EmailAttribute,
    string? NationalIdAttribute,
    string? DisplayNameAttribute,
    int TimeoutSeconds,
    string? CustomCaCertificatePem);

/// <summary>GET <c>/api/identity-providers/{id}/saml/metadata</c> (<c>IdentityProviders.View</c>; Saml kind only, else 404): the SP side to give the IdP admin.</summary>
public sealed record SamlSpMetadataDto(string EntityId, string AcsUrl, string MetadataUrl, string MetadataXml);

/// <summary>Stable codes of <see cref="ProviderTestCheckDto"/>; the client maps each to plain words (<c>ProviderCheck_&lt;code&gt;</c>). Never a secret, an address or a server message.</summary>
public static class ProviderTestCheckCodes
{
    // SAML
    public const string SsoUrlReachable = "SsoUrlReachable";
    public const string SigningCertificatesValid = "SigningCertificatesValid";
    public const string SigningCertificateExpiresSoon = "SigningCertificateExpiresSoon";

    // LDAP
    public const string HostReachable = "HostReachable";
    public const string TlsEstablished = "TlsEstablished";
    public const string ServerCertificateTrusted = "ServerCertificateTrusted";
    public const string ServiceBindSucceeded = "ServiceBindSucceeded";
    public const string BaseDnFound = "BaseDnFound";
    public const string SearchFilterValid = "SearchFilterValid";
    public const string AttributesFound = "AttributesFound";

    public static IReadOnlyList<string> All { get; } =
    [
        SsoUrlReachable, SigningCertificatesValid, SigningCertificateExpiresSoon,
        HostReachable, TlsEstablished, ServerCertificateTrusted, ServiceBindSucceeded, BaseDnFound, SearchFilterValid, AttributesFound,
    ];
}

/// <summary>One named check of a connection test.</summary>
public sealed record ProviderTestCheckDto(string Code, bool Ok);

/// <summary>
/// Where the people go (server-rendered pages beside <c>/Account/ExternalLogin/{slug}</c>, the OIDC route that already exists). SAML is SP-initiated: the page redirects to the IdP with a stored
/// request id and answers an unsolicited or replayed response with the generic sign-in failure. LDAP's password is typed on the LMS login page, sent once in the form body, never logged and never
/// in a URL; a failure is one generic message whatever the cause, and the existing auth rate limiting applies per user and per address.
/// </summary>
public static class ExternalSignInPages
{
    public const string SamlLogin = "/Account/Saml/{slug}";
    public const string SamlAcs = "/Account/Saml/{slug}/acs";
    public const string SamlMetadata = "/Account/Saml/{slug}/metadata";
    public const string LdapLogin = "/Account/Ldap/{slug}";

    public static string SamlLoginFor(string slug) => $"/Account/Saml/{slug}";
    public static string SamlAcsFor(string slug) => $"/Account/Saml/{slug}/acs";
    public static string SamlMetadataFor(string slug) => $"/Account/Saml/{slug}/metadata";
    public static string LdapLoginFor(string slug) => $"/Account/Ldap/{slug}";
}
