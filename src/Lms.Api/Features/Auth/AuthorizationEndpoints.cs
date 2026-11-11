using System.Security.Claims;
using Lms.Domain.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Lms.Api.Features.Auth;

/// <summary>
/// OpenID Connect endpoints. OpenIddict validates the protocol messages; these handlers only decide
/// who the user is and which claims go into the tokens.
/// </summary>
public static class AuthorizationEndpoints
{
    public const string PlatformAdminClaim = "platform_admin";

    /// <summary>Set when the sign-in went through a client's identity provider; limits the session to that client.</summary>
    public const string ExternalOrgClaim = "lms_ext_org";

    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder app)
    {
        string[] both = [HttpMethods.Get, HttpMethods.Post];
        app.MapMethods("/connect/authorize", both, (Delegate)Authorize).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/connect/token", Exchange).AllowAnonymous().ExcludeFromDescription();
        app.MapMethods("/connect/logout", both, (Delegate)Logout).AllowAnonymous().ExcludeFromDescription();
        app.MapMethods("/connect/userinfo", both, (Delegate)UserInfo).AllowAnonymous().ExcludeFromDescription();

        // Identity only (no permission): tells callers who their token says they are.
        app.MapGet("/api/auth/whoami", (ClaimsPrincipal user) => Results.Ok(new
        {
            sub = user.GetClaim(Claims.Subject),
            platformAdmin = user.GetClaim(PlatformAdminClaim) == "true",
        })).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> Authorize(HttpContext http, UserManager<User> users)
    {
        var request = http.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var cookie = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await users.GetUserAsync(cookie.Principal!) : null;

        if (user is null || !CanSignIn(user))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(Errors.LoginRequired, "The user is not logged in.");
            }

            // Send the user to the login page and come back to this exact request afterwards (local path only).
            var parameters = http.Request.HasFormContentType
                ? http.Request.Form.ToList()
                : http.Request.Query.ToList();
            return Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = http.Request.PathBase + http.Request.Path + QueryString.Create(parameters),
                },
                [IdentityConstants.ApplicationScheme]);
        }

        var principal = BuildPrincipal(user, request.GetScopes(), cookie.Principal!.FindFirst(ExternalOrgClaim)?.Value);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> Exchange(HttpContext http, UserManager<User> users)
    {
        var request = http.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Other grant types (password, client credentials...) are rejected by OpenIddict before reaching here.
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The specified grant type is not supported.");
        }

        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = result.Principal?.GetClaim(Claims.Subject);
        var user = subject is null ? null : await users.FindByIdAsync(subject);

        // The account may have been deactivated or locked since the code or refresh token was issued.
        if (user is null || !CanSignIn(user) || await users.IsLockedOutAsync(user))
        {
            return Forbid(Errors.InvalidGrant, "The account is no longer allowed to sign in.");
        }

        // The binding to the provider's client survives code exchange and refresh.
        var principal = BuildPrincipal(user, result.Principal!.GetScopes(), result.Principal!.GetClaim(ExternalOrgClaim));
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> Logout(SignInManager<User> signIn)
    {
        await signIn.SignOutAsync();
        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> UserInfo(HttpContext http, UserManager<User> users)
    {
        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal;
        var user = principal?.GetClaim(Claims.Subject) is { } id ? await users.FindByIdAsync(id) : null;
        if (principal is null || user is null || !CanSignIn(user))
        {
            return Forbid(Errors.InvalidToken, "The access token is not valid.");
        }

        var claims = new Dictionary<string, object> { [Claims.Subject] = principal.GetClaim(Claims.Subject)! };
        if (principal.GetClaim(Claims.Name) is { } name)
        {
            claims[Claims.Name] = name;
        }

        if (principal.GetClaim(PlatformAdminClaim) is { } admin)
        {
            claims[PlatformAdminClaim] = admin;
        }

        return Results.Ok(claims);
    }

    private static bool CanSignIn(User user) => user.Status == UserStatus.Active;

    private static IResult Forbid(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static ClaimsPrincipal BuildPrincipal(User user, IEnumerable<string> scopes, string? externalOrganization = null)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Name, string.IsNullOrWhiteSpace(user.DisplayNameEn) ? user.Email : user.DisplayNameEn);
        if (user.IsPlatformAdmin)
        {
            identity.SetClaim(PlatformAdminClaim, "true");
        }

        if (!string.IsNullOrEmpty(externalOrganization))
        {
            identity.SetClaim(ExternalOrgClaim, externalOrganization);
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        principal.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name when principal.HasScope(Scopes.Profile) => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        });
        return principal;
    }
}
