using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>Authenticates requests from test headers so tests don't need the OpenIddict flow.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string AdminHeader = "X-Test-Admin";
    public const string NameHeader = "X-Test-Name";
    public const string ExternalOrgHeader = "X-Test-Ext-Org";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", user.ToString()) };
        if (Request.Headers.TryGetValue(NameHeader, out var name))
        {
            claims.Add(new Claim("name", name.ToString()));
        }

        if (Request.Headers[AdminHeader] == "true")
        {
            claims.Add(new Claim("platform_admin", "true"));
        }

        if (Request.Headers.TryGetValue(ExternalOrgHeader, out var externalOrg))
        {
            claims.Add(new Claim("lms_ext_org", externalOrg.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "name", "role");
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}