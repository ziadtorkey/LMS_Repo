using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lms.Infrastructure.Zoom;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The Zoom webhook as an attacker sees it (M4b plan Task 20, Review Focus 3, D54): the one anonymous M4b route. Written from the contract, not from the handler: the
/// signature is over the raw bytes (<c>v0:{timestamp}:{body}</c>, HMAC-SHA256, hex, <c>x-zm-signature</c> and <c>x-zm-request-timestamp</c>) and the window is five minutes;
/// a missing, malformed, wrong or stale signature is 401 with the same empty answer (no hint which); a body over 256 KB is 413; only failed verifications are rate limited
/// (429), a validly signed request is never refused for the address; a re-delivery is a 200 no-op; the URL-validation challenge is answered. Each test starts its own host
/// (fresh limiter) with the secret configured, and waits for the route through <see cref="M4bPending"/> (#511).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4bWebhookProbeTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private const string Secret = "qa-webhook-secret";

    private HttpClient NewClient(out IDisposable host)
    {
        var f = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.Configure<ZoomOptions>(o => o.WebhookSecretToken = Secret)));
        host = f;
        return f.CreateClient();
    }

    private static string Event(string name = "meeting.started") =>
        "{\"event\":\"" + name + "\",\"event_ts\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        + ",\"payload\":{\"account_id\":\"qa\",\"object\":{\"id\":\"" + Guid.NewGuid().ToString("N") + "\"}}}";

    private static HttpRequestMessage Request(string json, string? timestamp = null, string secret = Secret, string? signature = "compute", bool timestampHeader = true)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, Routes.ZoomWebhook) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        if (timestampHeader)
        {
            request.Headers.Add("x-zm-request-timestamp", ts);
        }

        if (signature is not null)
        {
            var message = Encoding.UTF8.GetBytes($"v0:{ts}:").Concat(body).ToArray();
            request.Headers.Add("x-zm-signature", signature == "compute"
                ? "v0=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message)).ToLowerInvariant()
                : signature);
        }

        return request;
    }

    private bool Pending() => M4bPending.Skip(factory, output, M4bPending.Webhook);

    [Fact]
    public async Task A_valid_event_is_accepted_and_a_redelivery_is_a_no_op()
    {
        if (Pending())
        {
            return;
        }

        using var client = NewClient(out var host);
        using (host)
        {
            var json = Event();
            using var first = await client.SendAsync(Request(json));
            using var again = await client.SendAsync(Request(json)); // the same bytes: Zoom sends no event id, so the hash is the identity
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("wrong-secret")]
    [InlineData("tampered-body")]
    [InlineData("no-timestamp")]
    [InlineData("malformed-signature")]
    [InlineData("stale")]
    [InlineData("future")]
    public async Task A_bad_missing_or_stale_signature_is_401_with_the_same_empty_answer(string defect)
    {
        if (Pending())
        {
            return;
        }

        var json = Event();
        var request = defect switch
        {
            "none" => Request(json, signature: null),
            "wrong-secret" => Request(json, secret: "not-the-secret"),
            "tampered-body" => TamperedAfterSigning(json),
            "no-timestamp" => Request(json, timestampHeader: false),
            "malformed-signature" => Request(json, signature: "v1=deadbeef"),
            "stale" => Request(json, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString()), // correctly signed, too old
            "future" => Request(json, timestamp: DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };

        using var client = NewClient(out var host);
        using (host)
        {
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(string.IsNullOrWhiteSpace(body) || !body.Contains("signature", StringComparison.OrdinalIgnoreCase) && !body.Contains("timestamp", StringComparison.OrdinalIgnoreCase),
                "a 401 must not say which check failed: " + body);
        }
    }

    private static HttpRequestMessage TamperedAfterSigning(string json)
    {
        var request = Request(json);
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(json.Replace("qa", "qb"))); // signed one body, sent another
        request.Content.Headers.ContentType = new("application/json");
        return request;
    }

    [Fact]
    public async Task A_body_over_256_KB_is_413()
    {
        if (Pending())
        {
            return;
        }

        using var client = NewClient(out var host);
        using (host)
        {
            var big = "{\"event\":\"x\",\"pad\":\"" + new string('a', 300 * 1024) + "\"}";
            using var response = await client.SendAsync(Request(big));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
    }

    [Fact]
    public async Task Repeated_failures_are_429_but_a_valid_signature_still_gets_through_and_a_valid_burst_is_never_429()
    {
        if (Pending())
        {
            return;
        }

        using var client = NewClient(out var host);
        using (host)
        {
            var blocked = false;
            for (var i = 0; i < 500 && !blocked; i++)
            {
                using var bad = await client.SendAsync(Request(Event(), secret: "wrong"));
                blocked = bad.StatusCode == HttpStatusCode.TooManyRequests;
                Assert.True(bad.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests, $"a bad signature answered {(int)bad.StatusCode}");
            }

            Assert.True(blocked, "500 failed verifications never produced a 429: the failure limiter is missing");

            // Zoom itself, behind the same address, is never locked out by the flood.
            using var valid = await client.SendAsync(Request(Event()));
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        }

        using var fresh = NewClient(out var host2);
        using (host2)
        {
            for (var i = 0; i < 60; i++)
            {
                using var response = await fresh.SendAsync(Request(Event()));
                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task The_url_validation_challenge_is_answered_with_the_hmac_of_the_plain_token()
    {
        if (Pending())
        {
            return;
        }

        using var client = NewClient(out var host);
        using (host)
        {
            const string plain = "qa-plain-token";
            var json = "{\"event\":\"endpoint.url_validation\",\"payload\":{\"plainToken\":\"" + plain + "\"}}";
            using var response = await client.SendAsync(Request(json));
            var body = await response.Content.ReadAsStringAsync();
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(expected, body);
            Assert.DoesNotContain(Secret, body); // the answer never carries the secret
        }
    }

    [Fact]
    public async Task An_event_for_an_unknown_meeting_is_a_200_no_op()
    {
        if (Pending())
        {
            return;
        }

        using var client = NewClient(out var host);
        using (host)
        {
            using var response = await client.SendAsync(Request(Event("meeting.ended")));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
