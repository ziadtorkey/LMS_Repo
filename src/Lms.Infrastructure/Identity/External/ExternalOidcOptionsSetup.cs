using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Identity.External;

/// <summary>Builds the OpenID Connect options of a provider scheme from its stored configuration.</summary>
public sealed class ExternalOidcOptionsSetup(ExternalSchemeRegistry registry, OutboundGuard guard) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name is null || registry.Find(name) is not { } provider)
        {
            return;
        }

        options.Authority = provider.Authority;
        options.ClientId = provider.ClientId;
        options.ClientSecret = provider.ClientSecret;
        options.ResponseType = "code";
        // Authorization code comes back in the query string: no cross-site POST, so the cookies can stay SameSite=Lax-friendly.
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.CallbackPath = new PathString("/signin-oidc/" + provider.Slug);
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        // Secure whenever the request is https; the correlation cookies must also work over plain http in development.
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.BackchannelHttpHandler = guard.CreateHandler();
        options.BackchannelTimeout = OutboundGuard.Timeout;
        options.RequireHttpsMetadata = provider.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        options.Scope.Clear();
        foreach (var scope in provider.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}
