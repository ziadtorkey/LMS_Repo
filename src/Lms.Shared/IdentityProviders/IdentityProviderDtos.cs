namespace Lms.Shared.IdentityProviders;

/// <summary>Kinds of client sign-in providers (FR-SEC-08, D33).</summary>
public static class IdentityProviderKinds
{
    public const string Oidc = "Oidc";
    public const string AzureAd = "AzureAd";

    public static IReadOnlyList<string> All { get; } = [Oidc, AzureAd];
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
    string CallbackUrl);

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
    bool Enabled);

/// <summary>Result of POST /api/identity-providers/{id}/test (discovery document fetch).</summary>
public sealed record ProviderTestResultDto(bool Ok, string? Issuer, string? Error);
