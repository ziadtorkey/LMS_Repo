using Lms.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Lms.Api.Features.Auth;

/// <summary>
/// Access tokens are self-contained, so deactivating a user would not stop a token already issued. After the token
/// itself is validated, this checks that the account behind it still exists and is active, on every API request.
/// </summary>
public sealed class ValidateUserIsActiveHandler(UserManager<User> users)
    : IOpenIddictValidationHandler<OpenIddictValidationEvents.ProcessAuthenticationContext>
{
    public static OpenIddictValidationHandlerDescriptor Descriptor { get; } =
        OpenIddictValidationHandlerDescriptor
            .CreateBuilder<OpenIddictValidationEvents.ProcessAuthenticationContext>()
            .UseScopedHandler<ValidateUserIsActiveHandler>()
            .SetOrder(int.MaxValue - 100_000)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictValidationEvents.ProcessAuthenticationContext context)
    {
        if (context.IsRejected || context.AccessTokenPrincipal is null)
        {
            return;
        }

        var subject = context.AccessTokenPrincipal.GetClaim(Claims.Subject);
        var user = subject is null ? null : await users.FindByIdAsync(subject);
        if (user is null || user.Status != UserStatus.Active)
        {
            context.Reject(Errors.InvalidToken, "The account of this token is no longer active.");
        }
    }
}
