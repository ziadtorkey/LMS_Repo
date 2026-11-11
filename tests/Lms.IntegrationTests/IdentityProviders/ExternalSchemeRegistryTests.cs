using Lms.Domain.IdentityProviders;
using Lms.Infrastructure.Identity.External;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lms.IntegrationTests.IdentityProviders;

[Collection(ApiCollection.Name)]
public sealed class ExternalSchemeRegistryTests(LmsApiFactory factory)
{
    [Fact]
    public void Scheme_name_is_ext_plus_id_without_dashes()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal("ext-11111111222233334444555555555555", ExternalSchemeRegistry.SchemeName(id));
    }

    [Fact]
    public async Task Sync_registers_enabled_providers_only_and_configures_options_from_the_row()
    {
        var tree = await AccessTestData.SeedAsync(factory, "ESR1");
        var protector = factory.Services.GetRequiredService<ProviderSecretProtector>();
        var slug = "reg-" + Guid.NewGuid().ToString("N")[..8];
        var enabled = new IdentityProviderConfig(
            tree.Org.Id, IdentityProviderKind.Oidc, slug, "مزود", "On", "https://idp.example.test", null, "cid",
            protector.Protect("the-secret"), "openid email", ExternalMatchBy.Email, null, "x.example", enabled: true);
        var disabled = new IdentityProviderConfig(
            tree.Org.Id, IdentityProviderKind.Oidc, slug + "-off", "مزود", "Off", "https://idp.example.test", null, "cid",
            protector.Protect("the-secret"), "openid email", ExternalMatchBy.Email, null, "y.example", enabled: false);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Set<IdentityProviderConfig>().AddRange(enabled, disabled);
            await db.SaveChangesAsync();
        }

        var registry = factory.Services.GetRequiredService<ExternalSchemeRegistry>();
        await registry.SyncAsync(CancellationToken.None);

        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.NotNull(await schemes.GetSchemeAsync(ExternalSchemeRegistry.SchemeName(enabled.Id)));
        Assert.Null(await schemes.GetSchemeAsync(ExternalSchemeRegistry.SchemeName(disabled.Id)));
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(ExternalSchemeRegistry.SchemeName(enabled.Id));
        Assert.Equal("https://idp.example.test", options.Authority);
        Assert.Equal("cid", options.ClientId);
        Assert.Equal("the-secret", options.ClientSecret);
        Assert.Equal("code", options.ResponseType);
        Assert.True(options.UsePkce);
        Assert.Equal($"/signin-oidc/{slug}", options.CallbackPath.Value);
        Assert.Equal(Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme, options.SignInScheme);
        Assert.False(options.MapInboundClaims);
        Assert.Contains("email", options.Scope);
        // Provider calls from the server: short timeout, never a redirect or proxy (SSRF guard, #185).
        // Query response mode: the cookies must be SameSite=Lax, because browsers reject SameSite=None without Secure (over http)
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, options.CorrelationCookie.SameSite);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, options.NonceCookie.SameSite);
        Assert.Equal(Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseMode.Query, options.ResponseMode);
        Assert.Equal(TimeSpan.FromSeconds(5), options.BackchannelTimeout);
        var handler = Assert.IsType<SocketsHttpHandler>(options.BackchannelHttpHandler);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
    }
}
