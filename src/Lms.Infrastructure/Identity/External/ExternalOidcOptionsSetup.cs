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
        options.UsePkce = true;
        options.CallbackPath = new PathString("/signin-oidc/" + provider.Slug);
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        options.BackchannelHttpHandler = guard.CreateHandler();
        options.RequireHttpsMetadata = provider.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        options.Scope.Clear();
        foreach (var scope in provider.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}
