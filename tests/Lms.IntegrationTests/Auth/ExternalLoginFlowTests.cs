using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.IdentityProviders;
using Lms.Domain.People;
using Lms.Infrastructure.Identity.External;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Auth;

/// <summary>The whole browser flow against a real OpenID Connect provider (mock-oauth2-server container).</summary>
[Collection(ApiCollection.Name)]
public sealed class ExternalLoginFlowTests(LmsApiFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly AuthTestHost _host = new(factory);

    [Fact]
    public async Task End_to_end_code_flow_against_mock_oauth2_server_issues_token_with_ext_org()
    {
        var email = $"flow-{Guid.NewGuid():N}@school.example";
        await using var provider = await MockOidcServer.StartAsync($"ext-sub-{Guid.NewGuid():N}", new Dictionary<string, string> { ["email"] = email });
        var a = await AccessTestData.SeedAsync(factory, "FLOW-A");
        var b = await AccessTestData.SeedAsync(factory, "FLOW-B");
        var user = await _host.CreateUserAsync(email);
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user.Id);
        await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user.Id); // dual member
        var slug = "flow-" + Guid.NewGuid().ToString("N")[..8];
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == a.School1.Id);
            db.Set<StaffProfile>().Add(new StaffProfile(unit, user.Id, "اسم", "Name", email, null, null, null, null, null, null));
            var protector = _host.Services.GetRequiredService<ProviderSecretProtector>();
            db.Set<IdentityProviderConfig>().Add(new IdentityProviderConfig(
                a.Org.Id, IdentityProviderKind.Oidc, slug, "مزود", "Mock", provider.Authority, null, "lms-client",
                protector.Protect("lms-secret"), "openid email", ExternalMatchBy.Email, null, "school.example", enabled: true));
            await db.SaveChangesAsync();
        }

        await _host.Services.GetRequiredService<ExternalSchemeRegistry>().SyncAsync(CancellationToken.None);

        using var browser = _host.NewBrowser();
        using var external = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var (verifier, challenge) = AuthTestHost.NewPkcePair();
        var authorizeUrl = AuthTestHost.AuthorizeUrl(challenge, "openid profile offline_access lms_api");

        // 1) the login page sends the browser to the provider; 2) the provider answers at once with a code
        var start = await browser.GetAsync($"/Account/ExternalLogin/{slug}?returnUrl={Uri.EscapeDataString(authorizeUrl)}");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var providerReply = await external.GetAsync(start.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, providerReply.StatusCode);

        // 3) back at the callback path, 4) the linker matches the staff member, 5) the authorize request continues
        var callback = await browser.GetAsync(new Uri(providerReply.Headers.Location!.PathAndQuery, UriKind.Relative));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var done = await browser.GetAsync(callback.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Equal(authorizeUrl, done.Headers.Location!.ToString());
        var authorized = await browser.GetAsync(done.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
        var code = System.Web.HttpUtility.ParseQueryString(authorized.Headers.Location!.Query)["code"]!;
        var tokenResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "lms-wasm", ["code"] = code,
            ["redirect_uri"] = AuthTestHost.RedirectUri, ["code_verifier"] = verifier,
        }));
        var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("access_token").GetString()!;

        // The token is bound to client A: the dual member sees only A's context, and cannot select B's.
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meDto = (await (await _host.NewBrowser().SendAsync(me)).Content.ReadFromJsonAsync<MeDto>(Web))!;
        Assert.Equal([inA.Id], meDto.Contexts.Select(c => c.AssignmentId));

        // the first sign-in linked the account and left an audit trail
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await check.AuditEntries.IgnoreQueryFilters().AnyAsync(e => e.EntityType == "ExternalLoginLinked" && e.UserId == user.Id));
    }

    [Fact]
    public async Task Unmatched_identity_returns_to_login_with_not_found_and_no_session()
    {
        var email = $"ghost-{Guid.NewGuid():N}@school.example";
        await using var provider = await MockOidcServer.StartAsync($"ext-sub-{Guid.NewGuid():N}", new Dictionary<string, string> { ["email"] = email });
        var a = await AccessTestData.SeedAsync(factory, "FLOW-C");
        var slug = "flow-" + Guid.NewGuid().ToString("N")[..8];
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var protector = _host.Services.GetRequiredService<ProviderSecretProtector>();
            db.Set<IdentityProviderConfig>().Add(new IdentityProviderConfig(
                a.Org.Id, IdentityProviderKind.Oidc, slug, "مزود", "Mock", provider.Authority, null, "lms-client",
                protector.Protect("lms-secret"), "openid email", ExternalMatchBy.Email, null, "school.example", enabled: true));
            await db.SaveChangesAsync();
        }

        await _host.Services.GetRequiredService<ExternalSchemeRegistry>().SyncAsync(CancellationToken.None);
        using var browser = _host.NewBrowser();
        using var external = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var start = await browser.GetAsync($"/Account/ExternalLogin/{slug}");
        var providerReply = await external.GetAsync(start.Headers.Location);
        var callback = await browser.GetAsync(new Uri(providerReply.Headers.Location!.PathAndQuery, UriKind.Relative));
        var done = await browser.GetAsync(callback.Headers.Location);

        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Contains("externalError=external_account_not_found", done.Headers.Location!.ToString());
        var page = await (await browser.GetAsync(done.Headers.Location!.ToString() + "&culture=en&ui-culture=en")).Content.ReadAsStringAsync();
        Assert.Equal("We could not find an account for this identity. Contact your administrator.", AuthTestHost.TestId(page, "login-error"));
    }

    [Fact]
    public async Task Deactivated_staff_external_login_shows_deactivated_message_and_starts_no_session()
    {
        var email = $"off-{Guid.NewGuid():N}@school.example";
        await using var provider = await MockOidcServer.StartAsync($"ext-sub-{Guid.NewGuid():N}", new Dictionary<string, string> { ["email"] = email });
        var a = await AccessTestData.SeedAsync(factory, "FLOW-D");
        var user = await _host.CreateUserAsync(email, status: Lms.Domain.Identity.UserStatus.Deactivated);
        var slug = "flow-" + Guid.NewGuid().ToString("N")[..8];
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == a.School1.Id);
            db.Set<StaffProfile>().Add(new StaffProfile(unit, user.Id, "اسم", "Name", email, null, null, null, null, null, null));
            var protector = _host.Services.GetRequiredService<ProviderSecretProtector>();
            db.Set<IdentityProviderConfig>().Add(new IdentityProviderConfig(
                a.Org.Id, IdentityProviderKind.Oidc, slug, "مزود", "Mock", provider.Authority, null, "lms-client",
                protector.Protect("lms-secret"), "openid email", ExternalMatchBy.Email, null, "school.example", enabled: true));
            await db.SaveChangesAsync();
        }

        await _host.Services.GetRequiredService<ExternalSchemeRegistry>().SyncAsync(CancellationToken.None);
        using var browser = _host.NewBrowser();
        using var external = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        var start = await browser.GetAsync($"/Account/ExternalLogin/{slug}");
        var providerReply = await external.GetAsync(start.Headers.Location);
        var callback = await browser.GetAsync(new Uri(providerReply.Headers.Location!.PathAndQuery, UriKind.Relative));
        var done = await browser.GetAsync(callback.Headers.Location);

        Assert.Contains("externalError=account_deactivated", done.Headers.Location!.ToString());
        var page = await (await browser.GetAsync(done.Headers.Location!.ToString() + "&culture=en&ui-culture=en")).Content.ReadAsStringAsync();
        Assert.Equal("This account is deactivated. Please contact your administrator.", AuthTestHost.TestId(page, "login-error"));
        // no application cookie: the authorize endpoint still sends the browser to the login page
        var (_, challenge) = AuthTestHost.NewPkcePair();
        var authorize = await browser.GetAsync(AuthTestHost.AuthorizeUrl(challenge));
        Assert.Contains("/Account/Login", authorize.Headers.Location!.ToString());
    }

    private async Task<(string Slug, IdentityProviderConfig Row)> AddProviderWithoutSyncAsync(MockOidcServer provider, string tag)
    {
        _ = _host.Services; // the host (this "node") is already running: its startup sync has passed
        var a = await AccessTestData.SeedAsync(factory, tag);
        var slug = "lazy-" + Guid.NewGuid().ToString("N")[..8];
        var row = new IdentityProviderConfig(
            a.Org.Id, IdentityProviderKind.Oidc, slug, "مزود", "Mock", provider.Authority, null, "lms-client",
            _host.Services.GetRequiredService<ProviderSecretProtector>().Protect("lms-secret"), "openid email",
            ExternalMatchBy.Email, null, "school.example", enabled: true);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Set<IdentityProviderConfig>().Add(row); // as if another node saved it: this node was not told
        await db.SaveChangesAsync();
        return (slug, row);
    }

    [Fact]
    public async Task Provider_saved_on_another_node_is_resolved_lazily_when_the_login_starts()
    {
        await using var provider = await MockOidcServer.StartAsync($"ext-sub-{Guid.NewGuid():N}", new Dictionary<string, string> { ["email"] = "lazy@school.example" });
        var (slug, _) = await AddProviderWithoutSyncAsync(provider, "FLOW-L1");
        using var browser = _host.NewBrowser();

        var start = await browser.GetAsync($"/Account/ExternalLogin/{slug}");

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.StartsWith(provider.Authority, start.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_path_of_a_provider_this_node_has_not_seen_registers_its_scheme()
    {
        await using var provider = await MockOidcServer.StartAsync($"ext-sub-{Guid.NewGuid():N}", new Dictionary<string, string> { ["email"] = "lazy2@school.example" });
        var (slug, row) = await AddProviderWithoutSyncAsync(provider, "FLOW-L2");
        var registry = _host.Services.GetRequiredService<ExternalSchemeRegistry>();
        Assert.Null(registry.Find(ExternalSchemeRegistry.SchemeName(row.Id)));
        using var browser = _host.NewBrowser();

        await browser.GetAsync($"/signin-oidc/{slug}?error=access_denied");

        Assert.NotNull(registry.Find(ExternalSchemeRegistry.SchemeName(row.Id)));
        // an unknown slug is just an unhandled path (no provider, nothing registered, no error)
        Assert.NotEqual(HttpStatusCode.InternalServerError, (await browser.GetAsync("/signin-oidc/no-such-provider?error=x")).StatusCode);
    }
}
