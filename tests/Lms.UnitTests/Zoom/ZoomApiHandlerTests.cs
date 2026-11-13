using System.Net;
using System.Net.Http.Headers;
using Lms.Infrastructure.Zoom;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomApiHandlerTests
{
    private static readonly ZoomOptions Options = new() { AccountId = "acct", ClientId = "cid", ClientSecret = "test-secret-value" };

    private sealed class Rig
    {
        public required List<TimeSpan> Waits { get; init; }
        public required FakeZoomHandler Api { get; init; }
        public required HttpClient Client { get; init; }
    }

    private static Rig Build(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> api)
    {
        var clock = new ManualClock(new DateTime(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc));
        var waits = new List<TimeSpan>();
        var auth = new FakeZoomHandler((_, n) => Task.FromResult(FakeZoomHandler.Token("tok" + n)));
        var tokens = new ZoomTokenCache(new HttpClient(auth), Microsoft.Extensions.Options.Options.Create(Options), clock);
        var limiter = new ZoomOutboundLimiter(clock, (d, _) =>
        {
            waits.Add(d);
            clock.Advance(d);
            return Task.CompletedTask;
        });
        var fake = new FakeZoomHandler(api);
        var handler = new ZoomApiHandler(tokens, limiter, Microsoft.Extensions.Options.Options.Create(Options)) { InnerHandler = fake };
        return new Rig { Waits = waits, Api = fake, Client = new HttpClient(handler) { BaseAddress = new Uri("https://api.zoom.test/v2/") } };
    }

    private static HttpResponseMessage RateLimited(int seconds)
    {
        var response = FakeZoomHandler.Json((HttpStatusCode)429, "{}");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    [Fact]
    public async Task A_call_that_never_answers_times_out_as_a_provider_failure()
    {
        var options = new ZoomOptions { AccountId = "acct", ClientId = "cid", ClientSecret = "test-secret-value", Timeout = TimeSpan.FromMilliseconds(150) };
        var clock = new ManualClock(DateTime.UtcNow);
        var auth = new FakeZoomHandler((_, n) => Task.FromResult(FakeZoomHandler.Token("tok" + n)));
        var tokens = new ZoomTokenCache(new HttpClient(auth), Microsoft.Extensions.Options.Options.Create(options), clock);
        var hanging = new HangingZoomHandler();
        var handler = new ZoomApiHandler(tokens, new ZoomOutboundLimiter(clock), Microsoft.Extensions.Options.Options.Create(options)) { InnerHandler = hanging };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.zoom.test/v2/") };

        var ex = await Assert.ThrowsAsync<ZoomProviderException>(() => client.GetAsync("users/h1/meetings"));

        Assert.Equal(ZoomFailure.Unavailable, ex.Failure);
    }

    [Fact]
    public async Task Requests_carry_the_bearer_token()
    {
        var rig = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.OK, "{}")));

        await rig.Client.GetAsync("users/h1/meetings");

        Assert.Equal("Bearer tok1", Assert.Single(rig.Api.Requests).Authorization);
    }

    [Fact]
    public async Task Rate_limited_call_honours_retry_after_then_succeeds()
    {
        var rig = Build((_, n) => Task.FromResult(n == 1 ? RateLimited(7) : FakeZoomHandler.Json(HttpStatusCode.OK, "{}")));

        var response = await rig.Client.GetAsync("users/h1/meetings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, rig.Api.Calls);
        Assert.Contains(TimeSpan.FromSeconds(7), rig.Waits);
    }

    [Fact]
    public async Task A_retry_after_longer_than_the_cap_is_provider_rate_limited_without_waiting()
    {
        var rig = Build((_, _) => Task.FromResult(RateLimited(600)));

        var ex = await Assert.ThrowsAsync<ZoomProviderException>(() => rig.Client.GetAsync("users/h1/meetings"));

        Assert.Equal(ZoomFailure.RateLimited, ex.Failure);
        Assert.Equal(TimeSpan.FromSeconds(600), ex.RetryAfter);
        Assert.Equal(1, rig.Api.Calls);
    }

    [Fact]
    public async Task A_rejected_token_is_refreshed_once_and_the_call_repeated()
    {
        var rig = Build((r, n) => Task.FromResult(n == 1
            ? FakeZoomHandler.Json(HttpStatusCode.Unauthorized, "{}")
            : FakeZoomHandler.Json(HttpStatusCode.OK, "{}")));

        var response = await rig.Client.GetAsync("users/h1/meetings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bearer tok1", "Bearer tok2"], rig.Api.Requests.Select(r => r.Authorization));
    }

    [Fact]
    public async Task A_second_unauthorized_answer_is_returned_not_looped()
    {
        var rig = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.Unauthorized, "{}")));

        var response = await rig.Client.GetAsync("users/h1/meetings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, rig.Api.Calls);
    }

    [Fact]
    public async Task Server_errors_are_returned_untouched_for_the_provider_to_map()
    {
        var rig = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.BadGateway, "{}")));

        var response = await rig.Client.GetAsync("users/h1/meetings");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, rig.Api.Calls);
    }
}
