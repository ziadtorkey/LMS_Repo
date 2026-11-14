using System.Net;
using System.Text;
using System.Text.Json;
using Lms.Api.PackageHost;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>The content host: headers on every response, token verification, file serving and revocation (M4b Task 9a, ADR 0005).</summary>
[Collection(ApiCollection.Name)]
public sealed class PackageHostTests(LmsApiFactory factory) : IDisposable
{
    private readonly PackageHostKit _kit = new(factory);

    public void Dispose() => _kit.Dispose();

    private static string Url(string token, string path) => $"/p/{token}/{path}";

    private async Task<(AccessTree Tree, PackageUpload Package)> SeededAsync(string name, params (string Path, string Text)[] files)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var package = await _kit.SeedPackageAsync(tree.Root, tree.School1, files.Length == 0 ? [("index.html", "<p>hi</p>")] : files);
        return (tree, package);
    }

    [Fact]
    public async Task Package_host_responses_carry_csp_sandbox_nosniff_and_no_xfo()
    {
        var (_, package) = await SeededAsync("PH1", ("index.html", "<p>hi</p>"));

        var response = await _kit.Content().GetAsync(Url(_kit.Token(package), "index.html"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("<p>hi</p>", await response.Content.ReadAsStringAsync());
        AssertHostHeaders(response);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Csp_sandbox_is_allow_scripts_only()
    {
        var (_, package) = await SeededAsync("PH2");

        var csp = (await _kit.Content().GetAsync(Url(_kit.Token(package), "index.html"))).Headers.GetValues("Content-Security-Policy").Single();

        Assert.StartsWith("sandbox allow-scripts;", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-forms", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-popups", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-top-navigation", csp, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors http://localhost:5132;"[..^1], csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_response_of_the_package_host_sets_a_cookie()
    {
        var (_, package) = await SeededAsync("PH3");
        var client = _kit.Content();

        var responses = new[]
        {
            await client.GetAsync(Url(_kit.Token(package), "index.html")),
            await client.GetAsync(Url("forged", "index.html")),
            await client.GetAsync("/api/me"),
            await client.GetAsync("/"),
        };

        Assert.All(responses, r => Assert.False(r.Headers.Contains("Set-Cookie")));
    }

    [Fact]
    public async Task Error_responses_carry_the_same_headers()
    {
        var (_, package) = await SeededAsync("PH4");
        var client = _kit.Content();

        var forged = await client.GetAsync(Url("forged", "index.html"));
        var missing = await client.GetAsync(Url(_kit.Token(package), "nope.html"));
        var notAPackageRoute = await client.GetAsync("/api/me");
        var preflight = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, Url("forged", "index.html")));

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound], [forged.StatusCode, missing.StatusCode, notAPackageRoute.StatusCode]);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        foreach (var response in new[] { forged, missing, notAPackageRoute, preflight })
        {
            AssertHostHeaders(response);
        }
    }

    [Theory]
    [InlineData("a//index.html")]
    [InlineData("a%5C..%5Cother.txt")]
    [InlineData("a%2F..%2F..%2Fother.txt")]
    [InlineData("C%3A/windows/win.ini")]
    public async Task Package_path_never_leaves_its_package(string path)
    {
        var (tree, package) = await SeededAsync("PH5", ("index.html", "<p>mine</p>"));
        await _kit.SeedPackageAsync(tree.Root, tree.School1, ("other.txt", "someone else"));

        var response = await _kit.Content().GetAsync(Url(_kit.Token(package), path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("someone else", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.All(_kit.Opened, o => Assert.Equal(package.StorePrefix, o.Prefix));
    }

    [Theory]
    [InlineData("../other.txt")]
    [InlineData("a/../../other.txt")]
    [InlineData("a/./b.txt")]
    [InlineData("a\\..\\b.txt")]
    [InlineData("/index.html")]
    [InlineData("a//b.txt")]
    [InlineData("a/")]
    [InlineData("C:/x")]
    [InlineData("a\0b")]
    [InlineData("")]
    public void Path_rules_reject_everything_that_is_not_a_clean_relative_path(string path) =>
        Assert.False(PackagePathRules.TryNormalise(path, out _));

    [Theory]
    [InlineData("index.html")]
    [InlineData("js/app.min.js")]
    [InlineData("media/ملف.png")]
    public void Path_rules_accept_clean_relative_paths(string path)
    {
        Assert.True(PackagePathRules.TryNormalise(path, out var relative));
        Assert.Equal(path, relative);
    }

    [Fact]
    public async Task Token_only_reaches_its_own_organization()
    {
        var (_, packageA) = await SeededAsync("PH6A", ("index.html", "client a"));
        var (treeB, packageB) = await SeededAsync("PH6B", ("index.html", "client b"));
        var client = _kit.Content();

        // A token signed for client A that names client B's package id (and B's unit, or A's) must not reach it.
        var withBsPackage = _kit.Token(packageB, org: packageA.OrganizationId, unit: packageA.OrgUnitPath);
        var withBsUnit = _kit.Token(packageB, org: packageA.OrganizationId, unit: treeB.School1.Path);
        var own = _kit.Token(packageA);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(withBsPackage, "index.html"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(withBsUnit, "index.html"))).StatusCode);
        Assert.Equal("client a", await (await client.GetAsync(Url(own, "index.html"))).Content.ReadAsStringAsync());
        Assert.DoesNotContain(_kit.Opened, o => o.Prefix == packageB.StorePrefix);
    }

    [Fact]
    public async Task Store_key_is_built_from_the_token_only()
    {
        var (_, package) = await SeededAsync("PH7", ("css/site.css", "body{}"));

        await _kit.Content().GetAsync(Url(_kit.Token(package), "css/site.css"));

        Assert.Equal([(IPackageStore.PrefixOf(package.OrganizationId, package.Id), "css/site.css")], _kit.Opened);
    }

    [Fact]
    public async Task Token_from_another_package_client_or_audience_is_404()
    {
        var (_, package) = await SeededAsync("PH8A", ("index.html", "ok"));
        var (_, other) = await SeededAsync("PH8B", ("index.html", "other"));
        var client = _kit.Content();
        var provider = _kit.Host.Services.GetRequiredService<IDataProtectionProvider>();
        var foreignAudience = new PackageTokenService(provider, Options.Create(new PackageHostOptions { Origin = "http://127.0.0.1:6000" }))
            .Issue(package.OrganizationId, package.OrgUnitPath, package.Id, null, null, false, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
        var otherPurpose = Convert.ToBase64String(provider.CreateProtector("Some.Other.Purpose").Protect(
            JsonSerializer.SerializeToUtf8Bytes(new PackageTokenClaims(package.OrganizationId, package.OrgUnitPath, package.Id, null, null, false,
                _kit.Tokens.Audience, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()))))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // The token of the other package reads the other package's files only; asking for this package's file under it finds nothing.
        Assert.Equal("other", await (await client.GetAsync(Url(_kit.Token(other), "index.html"))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(foreignAudience, "index.html"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(otherPurpose, "index.html"))).StatusCode);
    }

    [Fact]
    public async Task Forged_expired_and_malformed_tokens_are_identical_404()
    {
        var (_, package) = await SeededAsync("PH9");
        var client = _kit.Content();
        var valid = _kit.Token(package);
        var tokens = new[]
        {
            valid[..^4] + "AAAA",                                    // altered
            valid + "x",                                              // extended
            new string('A', 200),                                     // random
            "not-a-token",
            _kit.Token(package, lifetime: TimeSpan.FromMinutes(-1)), // expired
            string.Empty,
        };

        var answers = new List<(HttpStatusCode, string, string)>();
        foreach (var token in tokens)
        {
            var response = await client.GetAsync($"/p/{token}/index.html");
            answers.Add((response.StatusCode, string.Join("|", response.Headers.Where(h => h.Key != "X-Correlation-Id").OrderBy(h => h.Key).Select(h => h.Key + "=" + string.Join(",", h.Value))), await response.Content.ReadAsStringAsync()));
        }

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.NotFound, a.Item1));
        Assert.Single(answers.Select(a => a.Item2).Distinct());
        Assert.Single(answers.Select(a => a.Item3).Distinct());
    }

    [Fact]
    public async Task Revoked_student_token_is_refused_within_the_cache_window()
    {
        var (_, package) = await SeededAsync("PH10");
        var student = Guid.NewGuid();
        var revoked = _kit.Token(package, student);
        var bystander = _kit.Token(package, Guid.NewGuid());
        var client = _kit.Content();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url(revoked, "index.html"))).StatusCode);

        await RevokeAsync(package, student);

        await EventuallyAsync(async () => (await client.GetAsync(Url(revoked, "index.html"))).StatusCode == HttpStatusCode.NotFound); // 1 s cache in the test host
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url(bystander, "index.html"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url(_kit.Token(package, student), "index.html"))).StatusCode); // issued after the marker: a re-launch works
    }

    [Fact]
    public async Task Revocation_of_client_a_does_not_affect_client_b()
    {
        var (_, packageA) = await SeededAsync("PH11A");
        var (_, packageB) = await SeededAsync("PH11B");
        var student = Guid.NewGuid();
        var tokenA = _kit.Token(packageA, student);
        var tokenB = _kit.Token(packageB, student);
        var client = _kit.Content();
        await client.GetAsync(Url(tokenA, "index.html"));

        await RevokeAsync(packageA, null); // the whole package of client A (deleted)

        await EventuallyAsync(async () => (await client.GetAsync(Url(tokenA, "index.html"))).StatusCode == HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url(tokenB, "index.html"))).StatusCode);
    }

    [Fact]
    public async Task Top_level_svg_and_unknown_types_are_attachments()
    {
        var (_, package) = await SeededAsync("PH12", ("logo.svg", "<svg xmlns='http://www.w3.org/2000/svg'/>"), ("data.bin", "xx"), ("page.html", "<p/>"));
        var client = _kit.Content();
        var token = _kit.Token(package);

        var svgAsImage = await client.SendAsync(Get(Url(token, "logo.svg"), "image"));
        var svgAsPage = await client.SendAsync(Get(Url(token, "logo.svg"), "document"));
        var unknown = await client.SendAsync(Get(Url(token, "data.bin"), "empty"));
        var html = await client.SendAsync(Get(Url(token, "page.html"), "iframe"));

        Assert.Equal("image/svg+xml", svgAsImage.Content.Headers.ContentType!.ToString());
        Assert.Null(svgAsImage.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", svgAsPage.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/octet-stream", svgAsPage.Content.Headers.ContentType!.ToString());
        Assert.Equal("attachment", unknown.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/octet-stream", unknown.Content.Headers.ContentType!.ToString());
        Assert.Null(html.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task Logs_never_contain_the_token()
    {
        var token = _kit.Token((await SeededAsync("PH13")).Package);
        var events = new List<Serilog.Events.LogEvent>();
        var logger = new Serilog.LoggerConfiguration().Enrich.With(new TokenScrubbingEnricher()).WriteTo.Sink(new CollectingSink(events)).CreateLogger();

        // The shapes ASP.NET Core and Serilog request logging write for a request path.
        logger.Information("Request starting {Protocol} {Method} {Scheme}://{Host}{PathBase}{Path}{QueryString}", "HTTP/1.1", "GET", "http", "127.0.0.1:5999", "", $"/p/{token}/index.html", "");
        logger.Information("HTTP {RequestMethod} {RequestPath} responded {StatusCode}", "GET", $"/p/{token}/a/b.js", 404);

        var rendered = events.Select(e => e.RenderMessage()).ToList();
        Assert.All(rendered, text => Assert.DoesNotContain(token, text, StringComparison.Ordinal));
        Assert.Contains("/p/[redacted]/index.html", rendered[0], StringComparison.Ordinal);
        Assert.Equal("/p/[redacted]/a/b.js", LogScrubbing.Redact($"/p/{token}/a/b.js"));
    }

    private sealed class CollectingSink(List<Serilog.Events.LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => events.Add(logEvent);
    }

    private static HttpRequestMessage Get(string url, string fetchDest)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Sec-Fetch-Dest", fetchDest);
        return request;
    }

    private static void AssertHostHeaders(HttpResponseMessage response)
    {
        var headers = response.Headers;
        Assert.StartsWith("sandbox allow-scripts;", headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", headers.GetValues("Referrer-Policy").Single());
        Assert.True(headers.CacheControl is { NoStore: true, Private: true }, "Cache-Control: private, no-store");
        Assert.Equal("*", headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("GET, HEAD, OPTIONS", headers.GetValues("Access-Control-Allow-Methods").Single());
        foreach (var absent in new[] { "X-Frame-Options", "Set-Cookie", "Access-Control-Allow-Credentials", "Access-Control-Allow-Headers", "Cross-Origin-Resource-Policy", "Cross-Origin-Embedder-Policy", "Cross-Origin-Opener-Policy" })
        {
            Assert.False(headers.Contains(absent), absent);
        }
    }

    private async Task RevokeAsync(PackageUpload package, Guid? student)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<PackageUpload>().SingleAsync(p => p.Id == package.Id);
        db.Add(new PackageRevocation(tracked, student, DateTime.UtcNow));
        await db.SaveChangesAsync();
        await Task.Delay(1100); // a token issued after this moment is newer than the marker (unix seconds)
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 30; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(500);
        }

        Assert.Fail("The condition did not hold within 15 s.");
    }
}
