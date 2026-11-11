using Lms.Api.Features.Account;
using Lms.Api.Resources;
using Lms.Domain.IdentityProviders;
using Lms.Infrastructure.Identity.External;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Lms.Api.Pages.Account;

/// <summary>
/// Starts a sign-in through a client's identity provider: by e-mail address (the domain picks the provider) or directly
/// by provider slug. Whether a domain is unknown or simply has no provider is never revealed (one message for both).
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(AccountModule.AuthRateLimitPolicy)]
public sealed class ExternalLoginModel(
    ProviderLookup providers, IAuthenticationSchemeProvider schemes, ExternalSchemeRegistry registry,
    IStringLocalizer<AccountResource> localizer) : PageModel
{
    /// <summary>Remembers, across the round trip, which provider scheme this sign-in started with.</summary>
    public const string ProviderItem = "lms_provider";

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? slug, CancellationToken ct)
    {
        ReturnUrl = SafeReturnUrl();
        return string.IsNullOrEmpty(slug) ? Page() : await ChallengeAsync(await providers.FindBySlugAsync(slug, ct));
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        ReturnUrl = SafeReturnUrl();
        return await ChallengeAsync(await providers.FindByEmailDomainAsync(Email ?? string.Empty, ct));
    }

    private async Task<IActionResult> ChallengeAsync(IdentityProviderConfig? provider)
    {
        var scheme = provider is null ? null : ExternalSchemeRegistry.SchemeName(provider.Id);
        // A provider saved through another node is unknown to this one until it restarts: register it on demand.
        if (scheme is not null && await schemes.GetSchemeAsync(scheme) is null)
        {
            await registry.SyncAsync(HttpContext.RequestAborted);
        }

        if (scheme is null || await schemes.GetSchemeAsync(scheme) is null)
        {
            Error = localizer["ExternalNoProvider"];
            return Page();
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Page("/Account/ExternalCallback", values: new { returnUrl = ReturnUrl }),
            Items = { [ProviderItem] = scheme },
        };
        return Challenge(properties, scheme);
    }

    private string SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
