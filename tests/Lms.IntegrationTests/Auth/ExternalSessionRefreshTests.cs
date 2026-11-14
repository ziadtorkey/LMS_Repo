using System.Security.Claims;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Auth;

/// <summary>The Identity cookie is rebuilt every 30 minutes; the client binding of an external session must survive that.</summary>
[Collection(ApiCollection.Name)]
public sealed class ExternalSessionRefreshTests(LmsApiFactory factory) : IDisposable
{
    private readonly AuthTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<ClaimsPrincipal> RefreshedAsync(User user, params Claim[] extra)
    {
        using var scope = _host.Services.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<User>>();
        var principal = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaims(extra);

        // A cookie issued two hours ago: older than the validation interval, so the validator rebuilds the principal.
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(6),
        }, IdentityConstants.ApplicationScheme);
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var context = new CookieValidatePrincipalContext(
            http, new AuthenticationScheme(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(), ticket);

        await scope.ServiceProvider.GetRequiredService<ISecurityStampValidator>().ValidateAsync(context);

        Assert.True(context.ShouldRenew, "the test must force a refresh of the principal");
        return context.Principal!;
    }

    [Fact]
    public async Task Security_stamp_refresh_keeps_the_client_binding_of_an_external_session()
    {
        var user = await _host.CreateUserAsync();
        var org = Guid.NewGuid().ToString();

        var refreshed = await RefreshedAsync(user, new Claim("lms_ext_org", org));

        Assert.Equal(org, refreshed.FindFirst("lms_ext_org")?.Value);
    }

    [Fact]
    public async Task Refresh_of_a_password_session_adds_no_binding()
    {
        var user = await _host.CreateUserAsync();

        var refreshed = await RefreshedAsync(user);

        Assert.Null(refreshed.FindFirst("lms_ext_org"));
    }
}
