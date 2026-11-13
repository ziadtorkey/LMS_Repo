using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Text.Json;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>The app side of the content host (M4b Task 9a, D56): <c>frame-src</c>, host-only cookies, fail-closed wiring and the ScormContent guard.</summary>
[Collection(ApiCollection.Name)]
public sealed class PackageHostAppTests(LmsApiFactory factory)
{
    private static readonly string[] AppPaths = ["/health", "/api/nothing-here", "/", "/Account/Login"];

    private static string Csp(HttpResponseMessage response) => response.Headers.GetValues("Content-Security-Policy").Single();

    [Fact]
    public async Task Every_app_response_sends_frame_src_for_the_content_origin()
    {
        using var kit = new PackageHostKit(factory);
        var client = kit.App();

        foreach (var path in AppPaths)
        {
            var csp = Csp(await client.GetAsync(path));
            Assert.Contains($"frame-src {PackageHostKit.ContentOrigin}", csp, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task App_frame_src_lists_exactly_the_configured_content_origin()
    {
        using var kit = new PackageHostKit(factory);

        foreach (var path in AppPaths)
        {
            var directives = Csp(await kit.App().GetAsync(path)).Split(';', StringSplitOptions.TrimEntries);
            var frameSrc = directives.Single(d => d.StartsWith("frame-src", StringComparison.Ordinal));
            Assert.Equal($"frame-src {PackageHostKit.ContentOrigin}", frameSrc); // only the content origin: no 'self', no app origin, no wildcard
        }
    }

    [Fact]
    public async Task App_csp_adds_nothing_but_frame_src()
    {
        using var configured = new PackageHostKit(factory);
        using var unconfigured = new PackageHostKit(factory, origin: null);

        var baseline = Csp(await unconfigured.App().GetAsync("/health"));
        var withHost = Csp(await configured.App().GetAsync("/health"));

        Assert.Equal("frame-ancestors 'none'", baseline);
        Assert.Equal(baseline + $"; frame-src {PackageHostKit.ContentOrigin}", withHost);
    }

    [Fact]
    public async Task App_cookies_are_host_only()
    {
        using var kit = new PackageHostKit(factory);
        var client = kit.App();

        foreach (var path in AppPaths)
        {
            var response = await client.GetAsync(path);
            Assert.All(
                response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
                cookie => Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData(null, null)]                                            // not configured
    [InlineData("http://localhost:5132", null)]                         // the app origin itself
    [InlineData("http://content.localhost:5132", null)]                 // another name on the app's registrable domain (localhost names share one site)
    [InlineData("https://content.lms.example", "https://lms.example")]  // a subdomain of the app's registrable domain
    [InlineData("not an origin", null)]
    public async Task Pipeline_is_not_mapped_without_a_separate_content_origin(string? origin, string? appOrigin)
    {
        using var kit = new PackageHostKit(factory, origin, appOrigin);
        var client = kit.Host.CreateClient();
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            client.BaseAddress = uri;
        }

        var response = await client.GetAsync("/p/anything/index.html");

        // It is the app's own answer: the app headers, never the package host's.
        Assert.Equal("frame-ancestors 'none'", Csp(response));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Flag_enable_refused_when_host_not_configured()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PHF");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var flag = FeatureFlags.ScormContent.Name;

        using var unconfigured = new PackageHostKit(factory, origin: null);
        using var configured = new PackageHostKit(factory);

        var refused = await SetAsync(unconfigured, admin, tree.School1.Id, flag, "true");
        var disabling = await SetAsync(unconfigured, admin, tree.School1.Id, flag, "false");
        var accepted = await SetAsync(configured, admin, tree.School1.Id, flag, "true");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("package_host_not_configured", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, disabling.StatusCode); // switching it off is always allowed
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    private static Task<HttpResponseMessage> SetAsync(PackageHostKit kit, RoleAssignment who, Guid unitId, string name, string valueJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, Routes.OrgUnitSetting(unitId, name))
        {
            Content = JsonContent.Create(new SetSettingRequest(valueJson, false)),
        };
        request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, who.Id.ToString());
        return kit.App().SendAsync(request);
    }
}
