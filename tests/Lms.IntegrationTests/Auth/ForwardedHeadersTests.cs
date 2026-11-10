using System.Net;
using Lms.Domain.Auditing;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class ForwardedHeadersTests(LmsApiFactory factory)
{
    private const string ProxyIp = "198.51.100.200";

    private static async Task<HttpStatusCode> ForgotAsync(HttpClient browser, string? forwardedFor)
    {
        // The antiforgery token comes from a GET (reads are never limited); only the POST counts.
        var page = await browser.GetAsync("/Account/ForgotPassword");
        var html = await page.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/ForgotPassword")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = "nobody-here@lms.test",
                ["__RequestVerificationToken"] = token,
            }),
        };
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return (await browser.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Rate_limit_uses_forwarded_ip_from_known_proxy()
    {
        var host = new AuthTestHost(factory, permitLimit: 5, remoteIp: ProxyIp, knownProxies: [ProxyIp]);
        using var browser = host.NewBrowser();

        // Five requests of client A use up A's budget; the sixth from A is limited...
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await ForgotAsync(browser, "203.0.113.50"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await ForgotAsync(browser, "203.0.113.50"));

        // ...while client B behind the same proxy is not affected.
        Assert.Equal(HttpStatusCode.OK, await ForgotAsync(browser, "203.0.113.51"));
    }

    [Fact]
    public async Task Forwarded_header_from_unknown_proxy_is_ignored()
    {
        var host = new AuthTestHost(factory, permitLimit: 5, remoteIp: "198.51.100.201");
        using var browser = host.NewBrowser();

        // Every request claims a different client, but they all come from the same untrusted peer: one budget.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await ForgotAsync(browser, $"203.0.113.{100 + i}"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await ForgotAsync(browser, "203.0.113.200"));
    }

    [Fact]
    public async Task Forwarded_ip_from_a_known_proxy_is_what_security_events_record()
    {
        var host = new AuthTestHost(factory, remoteIp: "198.51.100.202", knownProxies: ["198.51.100.202"]);
        var user = await host.CreateUserAsync();
        using var browser = host.NewBrowser();
        var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.Add("X-Forwarded-For", "203.0.113.77");
        var page = await browser.SendAsync(request);
        var html = await page.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var post = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = user.Email!,
                ["Input.Password"] = "Wrong-password1",
                ["__RequestVerificationToken"] = token,
            }),
        };
        post.Headers.Add("X-Forwarded-For", "203.0.113.77");
        await browser.SendAsync(post);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.AuditEntries.SingleAsync(x => x.UserId == user.Id && x.EntityType == "LoginFailed");

        Assert.Equal("203.0.113.77", entry.IpAddress);
    }

    [Fact]
    public async Task Forwarded_proto_from_unknown_proxy_does_not_change_the_scheme()
    {
        var host = new AuthTestHost(factory, remoteIp: "198.51.100.203");
        using var browser = host.NewBrowser();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        var response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
