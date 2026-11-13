using System.Net;
using Lms.Infrastructure.Zoom;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomTokenCacheTests
{
    private const string Secret = "test-secret-value";
    private static readonly ZoomOptions Options = new() { AccountId = "acct", ClientId = "cid", ClientSecret = Secret };

    private static ZoomTokenCache Cache(FakeZoomHandler handler, ManualClock clock) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options), clock);

    [Fact]
    public async Task Token_is_requested_with_the_account_credentials_grant_and_basic_auth()
    {
        var handler = new FakeZoomHandler((_, _) => Task.FromResult(FakeZoomHandler.Token("t1")));

        var token = await Cache(handler, new ManualClock(DateTime.UtcNow)).GetTokenAsync();

        Assert.Equal("t1", token);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.DoesNotContain("acct", request.Url); // the account id is in the body, not in a URL that gets logged
        Assert.Contains("grant_type=account_credentials", request.Body);
        Assert.Contains("account_id=acct", request.Body);
        Assert.Equal("Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("cid:" + Secret)), request.Authorization);
    }

    [Fact]
    public async Task Token_is_cached_and_refreshed_once_under_concurrency()
    {
        var clock = new ManualClock(DateTime.UtcNow);
        var handler = new FakeZoomHandler(async (_, n) =>
        {
            await Task.Delay(50);
            return FakeZoomHandler.Token("t" + n, 3600);
        });
        var cache = Cache(handler, clock);

        var first = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetTokenAsync()));
        Assert.All(first, t => Assert.Equal("t1", t));
        Assert.Equal(1, handler.Calls);

        clock.Advance(TimeSpan.FromMinutes(30)); // still valid
        Assert.Equal("t1", await cache.GetTokenAsync());

        clock.Advance(TimeSpan.FromMinutes(30)); // inside the safety margin before expiry: refreshed, once
        var second = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetTokenAsync()));
        Assert.All(second, t => Assert.Equal("t2", t));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Invalidate_forces_a_new_token_only_when_the_rejected_token_is_still_the_cached_one()
    {
        var handler = new FakeZoomHandler((_, n) => Task.FromResult(FakeZoomHandler.Token("t" + n)));
        var cache = Cache(handler, new ManualClock(DateTime.UtcNow));

        var rejected = await cache.GetTokenAsync();
        cache.Invalidate(rejected);
        Assert.Equal("t2", await cache.GetTokenAsync());

        cache.Invalidate(rejected); // a second caller reporting the same stale token must not throw away the fresh one
        Assert.Equal("t2", await cache.GetTokenAsync());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_failed_token_call_is_a_provider_failure_and_never_echoes_the_secret()
    {
        var handler = new FakeZoomHandler((_, _) => Task.FromResult(
            FakeZoomHandler.Json(HttpStatusCode.Unauthorized, "{\"reason\":\"Invalid client_secret " + Secret + "\"}")));
        var cache = Cache(handler, new ManualClock(DateTime.UtcNow));

        var ex = await Assert.ThrowsAsync<ZoomProviderException>(() => cache.GetTokenAsync());

        Assert.Equal(ZoomFailure.Unauthorized, ex.Failure);
        Assert.DoesNotContain(Secret, ex.ToString());
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("cid:" + Secret)), ex.ToString());
    }

    [Fact]
    public async Task A_token_call_that_never_answers_times_out_as_a_provider_failure()
    {
        var hanging = new HangingZoomHandler();
        var options = new ZoomOptions { AccountId = "acct", ClientId = "cid", ClientSecret = Secret, Timeout = TimeSpan.FromMilliseconds(150) };
        var cache = new ZoomTokenCache(new HttpClient(hanging), Microsoft.Extensions.Options.Options.Create(options), new ManualClock(DateTime.UtcNow));

        var ex = await Assert.ThrowsAsync<ZoomProviderException>(() => cache.GetTokenAsync());

        Assert.Equal(ZoomFailure.Unavailable, ex.Failure);
        Assert.Equal(1, hanging.Calls);
    }

    [Fact]
    public async Task A_caller_that_cancels_gets_its_own_cancellation_not_a_provider_failure()
    {
        var options = new ZoomOptions { AccountId = "acct", ClientId = "cid", ClientSecret = Secret, Timeout = TimeSpan.FromSeconds(30) };
        var cache = new ZoomTokenCache(new HttpClient(new HangingZoomHandler()), Microsoft.Extensions.Options.Options.Create(options), new ManualClock(DateTime.UtcNow));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetTokenAsync(cts.Token));
    }

    [Fact]
    public async Task After_a_failure_the_next_call_tries_again()
    {
        var handler = new FakeZoomHandler((_, n) => Task.FromResult(n == 1
            ? FakeZoomHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
            : FakeZoomHandler.Token("ok")));
        var cache = Cache(handler, new ManualClock(DateTime.UtcNow));

        await Assert.ThrowsAsync<ZoomProviderException>(() => cache.GetTokenAsync());
        Assert.Equal("ok", await cache.GetTokenAsync());
    }
}
