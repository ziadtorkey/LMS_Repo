using System.ComponentModel.DataAnnotations;
using Lms.Api.Features.Account;
using Lms.Api.Resources;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Lms.Shared.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Lms.Api.Pages.Account;

/// <summary>
/// The step after signing in with a temporary password (D24): the person replaces it, the flag is cleared, the security stamp is
/// rotated and the original request continues. Only a signed-in (cookie) user can open it.
/// </summary>
[Authorize(AuthenticationSchemes = "Identity.Application")]
[EnableRateLimiting(AccountModule.AuthRateLimitPolicy)]
public sealed class ChangePasswordModel(
    UserManager<User> users, SignInManager<User> signIn, IAuditWriter audit, IStringLocalizer<AccountResource> localizer) : PageModel
{
    [BindProperty]
    public ChangePasswordInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet() => ReturnUrl = SafeReturnUrl();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ReturnUrl = SafeReturnUrl();
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (Input.NewPassword != Input.Confirm)
        {
            Error = localizer["ChangePassword_Mismatch"];
            return Page();
        }

        var result = await users.ChangePasswordAsync(user, Input.Current, Input.NewPassword);
        if (!result.Succeeded)
        {
            Error = localizer["ChangePassword_Failed"];
            return Page();
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user); // the security stamp changed: keep this session
        await audit.WriteSecurityEventAsync(AuditSecurityEvents.PasswordChanged, user.Id, null, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return LocalRedirect(ReturnUrl!);
    }

    private string SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}

public sealed class ChangePasswordInput
{
    [Required, MaxLength(256)]
    public string Current { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Confirm { get; set; } = string.Empty;
}
