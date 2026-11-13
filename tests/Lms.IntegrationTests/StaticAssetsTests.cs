using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests;

/// <summary>#451: the fingerprinted client assets named by the import map in index.html must be served by the API host.</summary>
[Collection(ApiCollection.Name)]
public sealed class StaticAssetsTests(LmsApiFactory factory)
{
    private async Task<IReadOnlyList<string>> ImportMapTargetsAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        var match = Regex.Match(html, "<script type=\"importmap\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success, "index.html has no import map");
        using var doc = JsonDocument.Parse(match.Groups[1].Value);
        return doc.RootElement.GetProperty("imports").EnumerateObject()
            .Select(p => "/" + p.Value.GetString()!.TrimStart('.', '/')).ToList();
    }

    [Theory]
    [InlineData("/js/richtext.")]
    [InlineData("/_framework/blazor.webassembly.")]
    public async Task Fingerprinted_import_map_asset_resolves(string prefix)
    {
        var client = factory.CreateClient();
        var targets = await ImportMapTargetsAsync(client);

        var target = Assert.Single(targets, t => t.StartsWith(prefix, StringComparison.Ordinal));
        Assert.Matches(@"\.[a-z0-9]{10}\.js$", target);
        var response = await client.GetAsync(target);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{target} answered {(int)response.StatusCode}: {body}");
    }

    [Theory]
    [InlineData("/courses/123")]
    [InlineData("/org/units/7/members")]
    public async Task Deep_client_route_is_answered_with_the_index_page_by_the_spa_fallback(string route)
    {
        var response = await factory.CreateClient().GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<script type=\"importmap\">", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Static_asset_responses_carry_the_security_headers()
    {
        var response = await factory.CreateClient().GetAsync("/js/richtext.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task Every_import_map_target_resolves()
    {
        var client = factory.CreateClient();
        foreach (var target in await ImportMapTargetsAsync(client))
        {
            Assert.True((await client.GetAsync(target)).StatusCode == HttpStatusCode.OK, target);
        }
    }

    [Fact]
    public async Task Unfingerprinted_boot_files_still_resolve()
    {
        var client = factory.CreateClient();
        foreach (var path in new[] { "/_framework/blazor.webassembly.js", "/js/richtext.js", "/css/richtext.css", "/css/app.css" })
        {
            Assert.True((await client.GetAsync(path)).StatusCode == HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Unknown_api_route_is_still_404_not_index()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
