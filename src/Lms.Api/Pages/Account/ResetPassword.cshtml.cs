using System.ComponentModel.DataAnnotations;
using Lms.Api.Features.Account;
using Lms.Api.Resources;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Lms.Api.Pages.Account;

/// <summary>One page for both "I forgot my password" links and "choose your first password" invitations.</summary>
[AllowAnonymous]
public sealed class ResetPasswordModel(
    UserManager<User> users,
    IAuditWriter audit,
    IStringLocalizer<AccountResource> localizer) : PageModel
{
    [BindProperty]
    public ResetInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    /// <summary>The link carries invite=1 for invitations (a plain bool binder would not accept the number).</summary>
    [BindProperty(Name = "invite", SupportsGet = true)]
    public string? InviteFlag { get; set; }

    public bool Invite => InviteFlag == "1" || string.Equals(InviteFlag, "true", StringComparison.OrdinalIgnoreCase);

    public bool LinkValid { get; private set; }

    public bool Done { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync() => LinkValid = await FindValidUserAsync() is not null;

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = await FindValidUserAsync();
        LinkValid = user is not null;
        if (user is null)
        {
            return Page();
        }

        if (!ModelState.IsValid || string.IsNullOrEmpty(Input.Password))
        {
            Error = localizer["FieldsRequired"];
            return Page();
        }

        if (Input.Password != Input.ConfirmPassword)
        {
            Error = localizer["PasswordsDiffer"];
            return Page();
        }

        // An invitation token is exchanged for a one-time reset token, so both flows end in the same Identity call.
        var resetToken = Invite ? await users.GeneratePasswordResetTokenAsync(user) : Token!;
        var result = await users.ResetPasswordAsync(user, resetToken, Input.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                LinkValid = false;
            }
            else
            {
                Error = localizer["PasswordRules"];
            }

            return Page();
        }

        // A reset is the way out of a lockout, and an accepted invitation proves the address.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        if (Invite && !user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await audit.WriteSecurityEventAsync(Invite ? "InvitationAccepted" : "PasswordReset", user.Id, null, ip, cancellationToken);
        Done = true;
        return Page();
    }

    /// <summary>The user the link belongs to, or null when the link is unknown, expired, used or of the other kind.</summary>
    private async Task<User?> FindValidUserAsync()
    {
        if (UserId == Guid.Empty || string.IsNullOrEmpty(Token))
        {
            return null;
        }

        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null)
        {
            return null;
        }

        var valid = Invite
            ? await users.VerifyUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, Token)
            : await users.VerifyUserTokenAsync(
                user, users.Options.Tokens.PasswordResetTokenProvider, UserManager<User>.ResetPasswordTokenPurpose, Token);
        return valid ? user : null;
    }
}

public sealed class ResetInput
{
    [Required, MaxLength(256)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
