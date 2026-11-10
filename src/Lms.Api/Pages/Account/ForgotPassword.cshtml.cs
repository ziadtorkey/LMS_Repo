using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Lms.Api.Features.Account;
using Lms.Api.Resources;
using Lms.Domain.Auditing;
using Lms.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Lms.Api.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting(AccountModule.AuthRateLimitPolicy)]
public sealed class ForgotPasswordModel(
    UserManager<User> users,
    IAccountEmails emails,
    IAuditWriter audit,
    IStringLocalizer<AccountResource> localizer,
    ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty]
    public ForgotInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Input.Email))
        {
            Error = localizer["FieldsRequired"];
            return Page();
        }

        // The answer is the same for known and unknown addresses: nothing reveals whether an account exists.
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is not null)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await audit.WriteSecurityEventAsync("PasswordResetRequested", user.Id, null, ip, cancellationToken);
            try
            {
                await emails.SendPasswordResetAsync(user, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A mail outage must not turn into an error page that tells guessers the address exists.
                logger.LogError(ex, "Could not send the password reset email for user {UserId}.", user.Id);
            }
        }

        Sent = true;
        return Page();
    }
}

public sealed class ForgotInput
{
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
