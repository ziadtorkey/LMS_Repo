using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.IdentityProviders;

public enum IdentityProviderKind
{
    Oidc = 1,
    AzureAd = 2,
}

public enum ExternalMatchBy
{
    Email = 1,
    NationalId = 2,
}

/// <summary>
/// One external sign-in provider (OpenID Connect / Azure AD) of one client (FR-SEC-08, X-08). People of that client may
/// sign in through it; the client secret is stored only in protected form and never audited.
/// </summary>
[Audited]
public sealed class IdentityProviderConfig : AuditableEntity, ITenantOwned
{
    private IdentityProviderConfig()
    {
        Slug = DisplayNameAr = DisplayNameEn = Authority = ClientId = Scopes = EmailDomains = string.Empty;
    }

    public IdentityProviderConfig(
        Guid organizationId, IdentityProviderKind kind, string slug, string displayNameAr, string displayNameEn,
        string authority, string? azureTenantId, string clientId, string? protectedClientSecret, string scopes,
        ExternalMatchBy matchBy, string? nationalIdClaim, string emailDomains, bool enabled,
        DateTime? clientSecretUpdatedAt = null)
        : this()
    {
        OrganizationId = organizationId;
        Kind = kind;
        Slug = slug;
        Apply(displayNameAr, displayNameEn, authority, azureTenantId, clientId, scopes, matchBy, nationalIdClaim, emailDomains, enabled);
        ProtectedClientSecret = protectedClientSecret;
        ClientSecretUpdatedAt = protectedClientSecret is null ? null : clientSecretUpdatedAt;
    }

    public Guid OrganizationId { get; private set; }
    public IdentityProviderKind Kind { get; private set; }

    /// <summary>Lower-case url-safe name, unique across all clients; part of the callback path.</summary>
    public string Slug { get; private set; }

    public string DisplayNameAr { get; private set; }
    public string DisplayNameEn { get; private set; }
    public string Authority { get; private set; }
    public string? AzureTenantId { get; private set; }
    public string ClientId { get; private set; }

    /// <summary>Data-Protection ciphertext of the client secret; never returned by any API and never written to the audit log.</summary>
    [AuditIgnore]
    public string? ProtectedClientSecret { get; private set; }

    /// <summary>When the secret was last set. Audited, so that replacing the (audit-ignored) secret still leaves a trail.</summary>
    public DateTime? ClientSecretUpdatedAt { get; private set; }

    public string Scopes { get; private set; }
    public ExternalMatchBy MatchBy { get; private set; }
    public string? NationalIdClaim { get; private set; }

    /// <summary>Comma-separated lower-case e-mail domains that route to this provider.</summary>
    public string EmailDomains { get; private set; }

    public bool Enabled { get; private set; }

    public void Update(
        string displayNameAr, string displayNameEn, string authority, string? azureTenantId, string clientId,
        string? newProtectedSecret, string scopes, ExternalMatchBy matchBy, string? nationalIdClaim, string emailDomains,
        bool enabled, DateTime utcNow)
    {
        Apply(displayNameAr, displayNameEn, authority, azureTenantId, clientId, scopes, matchBy, nationalIdClaim, emailDomains, enabled);

        // Null keeps the stored secret: the API never sends it back, so an edit without a new one must not erase it.
        if (newProtectedSecret is not null)
        {
            ProtectedClientSecret = newProtectedSecret;
            ClientSecretUpdatedAt = utcNow;
        }
    }

    private void Apply(
        string displayNameAr, string displayNameEn, string authority, string? azureTenantId, string clientId,
        string scopes, ExternalMatchBy matchBy, string? nationalIdClaim, string emailDomains, bool enabled)
    {
        DisplayNameAr = displayNameAr;
        DisplayNameEn = displayNameEn;
        Authority = authority;
        AzureTenantId = azureTenantId;
        ClientId = clientId;
        Scopes = scopes;
        MatchBy = matchBy;
        NationalIdClaim = nationalIdClaim;
        EmailDomains = emailDomains.ToLowerInvariant();
        Enabled = enabled;
    }
}
