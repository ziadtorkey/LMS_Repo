using System.Security.Claims;
using System.Text.Json;
using Lms.Api.Features.Account;
using Lms.Api.Features.Auth;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.Infrastructure.Identity.External;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Lms.Api.Pages.Account;

/// <summary>
/// Where the provider's sign-in returns to. The external identity is matched to an existing account of the provider's client
/// (nothing is created); on success the session is bound to that client (<c>lms_ext_org</c>) and the original authorize
/// request continues. Every failure goes back to the login page, which shows it in the same error element as password login.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(AccountModule.AuthRateLimitPolicy)]
public sealed class ExternalCallbackModel(
    SignInManager<User> signIn, ProviderLookup providers, ExternalLoginLinker linker, IAuditWriter audit) : PageModel
{
    private const string SchemePrefix = "ext-";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var returnUrl = !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
        var info = await signIn.GetExternalLoginInfoAsync();
        var provider = info is not null && info.LoginProvider.StartsWith(SchemePrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(info.LoginProvider[SchemePrefix.Length..], "N", out var id)
                ? await providers.FindByIdAsync(id, ct)
                : null;
        if (info is null || provider is null)
        {
            return await BackAsync(ExternalLoginLinker.Failed, returnUrl);
        }

        var result = await linker.ResolveAsync(provider, info.Principal, ct);
        if (result.User is not { } user)
        {
            return await BackAsync(result.ErrorCode ?? ExternalLoginLinker.Failed, returnUrl);
        }

        // Same gate as password login: a blocked account never gets a session, and its status is shown as in M1.
        if (user.Status != UserStatus.Active)
        {
            var details = JsonSerializer.Serialize(new { provider = provider.Slug, reason = user.Status.ToString() });
            await audit.WriteSecurityEventAsync(
                "ExternalLoginFailed", user.Id, details, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            return await BackAsync("account_" + user.Status.ToString().ToLowerInvariant(), returnUrl);
        }

        await signIn.SignInWithClaimsAsync(
            user, isPersistent: false, [new Claim(AuthorizationEndpoints.ExternalOrgClaim, provider.OrganizationId.ToString())]);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return LocalRedirect(returnUrl);
    }

    private async Task<IActionResult> BackAsync(string error, string returnUrl)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return RedirectToPage("/Account/Login", new { externalError = error, returnUrl });
    }
}
