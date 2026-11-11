using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.IdentityProviders;

namespace Lms.Api.Features.IdentityProviders;

/// <summary>Request validation. Error values are message keys the client translates (never display text).</summary>
public static partial class IdentityProviderValidators
{
    public const int MaxNameLength = 200;
    public const int MaxSecretLength = 1000;

    public static string AzureAuthority(string tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";

    /// <summary>The request with trimmed, normalised values (Authority derived for Azure AD), or the problem to return.</summary>
    public static (SaveIdentityProviderRequest? Request, IResult? Problem) Validate(
        SaveIdentityProviderRequest? r, bool isCreate, bool allowHttpAuthority)
    {
        if (r is null)
        {
            return (null, ValidationFailed(new Dictionary<string, string[]> { ["body"] = ["required"] }));
        }

        var errors = new Dictionary<string, string[]>();
        var kind = r.Kind?.Trim() ?? string.Empty;
        var slug = r.Slug?.Trim() ?? string.Empty;
        var nameAr = r.DisplayNameAr?.Trim() ?? string.Empty;
        var nameEn = r.DisplayNameEn?.Trim() ?? string.Empty;
        var clientId = r.ClientId?.Trim() ?? string.Empty;
        var scopes = string.Join(' ', (r.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var matchBy = r.MatchBy?.Trim() ?? string.Empty;
        var claim = string.IsNullOrWhiteSpace(r.NationalIdClaim) ? null : r.NationalIdClaim.Trim();
        var tenant = string.IsNullOrWhiteSpace(r.AzureTenantId) ? null : r.AzureTenantId.Trim();
        var authority = r.Authority?.Trim() ?? string.Empty;
        var secret = string.IsNullOrEmpty(r.ClientSecret) ? null : r.ClientSecret;

        if (!IdentityProviderKinds.All.Contains(kind))
        {
            errors["kind"] = ["invalid_value"];
        }

        if (!SlugPattern().IsMatch(slug))
        {
            errors["slug"] = ["invalid_format"];
        }

        Name(errors, "displayNameAr", nameAr);
        Name(errors, "displayNameEn", nameEn);
        if (clientId.Length is 0 or > MaxNameLength)
        {
            errors["clientId"] = [clientId.Length == 0 ? "required" : "too_long"];
        }

        if (secret is null && isCreate)
        {
            errors["clientSecret"] = ["required"];
        }
        else if (secret is { Length: > MaxSecretLength })
        {
            errors["clientSecret"] = ["too_long"];
        }

        if (!scopes.Split(' ').Contains("openid") || scopes.Length > 500)
        {
            errors["scopes"] = ["openid_required"];
        }

        if (!ExternalMatchBy.All.Contains(matchBy))
        {
            errors["matchBy"] = ["invalid_value"];
        }
        else if (matchBy == ExternalMatchBy.NationalId && (claim is null || claim.Length > 100))
        {
            errors["nationalIdClaim"] = ["required"];
        }

        if (kind == IdentityProviderKinds.AzureAd)
        {
            if (tenant is null || !TenantPattern().IsMatch(tenant))
            {
                errors["azureTenantId"] = ["invalid_format"];
            }
            else
            {
                authority = AzureAuthority(tenant);
            }
        }
        else if (kind == IdentityProviderKinds.Oidc)
        {
            tenant = null;
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || authority.Length > 500
                || !(uri.Scheme == Uri.UriSchemeHttps || (allowHttpAuthority && uri.Scheme == Uri.UriSchemeHttp)))
            {
                errors["authority"] = ["invalid_authority"];
            }
        }

        var domains = new List<string>();
        foreach (var raw in r.EmailDomains ?? [])
        {
            var domain = raw?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!DomainPattern().IsMatch(domain))
            {
                errors["emailDomains"] = ["invalid_format"];
                break;
            }

            if (!domains.Contains(domain))
            {
                domains.Add(domain);
            }
        }

        return errors.Count > 0
            ? (null, ValidationFailed(errors))
            : (new SaveIdentityProviderRequest(kind, slug, nameAr, nameEn, authority, tenant, clientId, secret, scopes,
                matchBy, matchBy == ExternalMatchBy.NationalId ? claim : null, domains, r.Enabled), null);
    }

    public static IResult ValidationFailed(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    private static void Name(Dictionary<string, string[]> errors, string field, string value)
    {
        if (value.Length == 0)
        {
            errors[field] = ["required"];
        }
        else if (value.Length > MaxNameLength)
        {
            errors[field] = ["too_long"];
        }
    }

    [GeneratedRegex("^[a-z0-9-]{3,40}$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,100}$")]
    private static partial Regex TenantPattern();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9.\-]{0,251}[a-z0-9])?$")]
    private static partial Regex DomainPattern();
}
