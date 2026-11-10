using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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
public sealed class LoginModel(
    UserManager<User> users,
    SignInManager<User> signIn,
    IAuditWriter audit,
    IStringLocalizer<AccountResource> localizer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet() => ReturnUrl = SafeReturnUrl();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // Only local targets are followed after login (no open redirect).
        ReturnUrl = SafeReturnUrl();

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Input.Email) || string.IsNullOrEmpty(Input.Password))
        {
            Error = localizer["FieldsRequired"];
            return Page();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Hash anyway so unknown and known emails take about as long (no timing-based user enumeration).
            users.PasswordHasher.HashPassword(new User(), Input.Password);
            await WriteFailureAsync(null, "UnknownUser", ip, cancellationToken);
            return Fail();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await WriteFailureAsync(user.Id, "LockedOut", ip, cancellationToken);
            return Fail();
        }

        if (!result.Succeeded)
        {
            await WriteFailureAsync(user.Id, "BadPassword", ip, cancellationToken);
            return Fail();
        }

        // Reached only with the right password, so the status of an account is never shown to guessers.
        if (user.Status != UserStatus.Active)
        {
            await WriteFailureAsync(user.Id, user.Status.ToString(), ip, cancellationToken);
            Error = user.Status switch
            {
                UserStatus.Expired => localizer["AccountExpired"],
                UserStatus.Withdrawn => localizer["AccountWithdrawn"],
                _ => localizer["AccountDeactivated"],
            };
            return Page();
        }

        await signIn.SignInAsync(user, isPersistent: false);
        await audit.WriteSecurityEventAsync("LoginSucceeded", user.Id, null, ip, cancellationToken);
        return LocalRedirect(ReturnUrl!);
    }

    // One message for unknown email, wrong password and lockout: nothing reveals whether the account exists.
    private PageResult Fail()
    {
        Error = localizer["InvalidCredentials"];
        return Page();
    }

    private Task WriteFailureAsync(Guid? userId, string reason, string? ip, CancellationToken cancellationToken)
    {
        var attempted = Input.Email.Length > 200 ? Input.Email[..200] : Input.Email;
        var details = JsonSerializer.Serialize(new { reason, email = attempted });
        return audit.WriteSecurityEventAsync("LoginFailed", userId, details, ip, cancellationToken);
    }

    private string SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}

public sealed class InputModel
{
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Password { get; set; } = string.Empty;
}
