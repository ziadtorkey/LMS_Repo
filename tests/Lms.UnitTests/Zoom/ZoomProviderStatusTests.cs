using System.Net;
using Lms.Infrastructure.Zoom;
using Lms.Shared.LiveSessions;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomProviderStatusTests
{
    private static ZoomOptions Configured() =>
        new() { AccountId = "acct", ClientId = "cid", ClientSecret = "test-secret-value", HostPool = ["h1"] };

    private static (ZoomProviderStatus Status, FakeZoomHandler Auth, ManualClock Clock) Build(ZoomOptions options, Func<int, HttpResponseMessage> token)
    {
        var clock = new ManualClock(new DateTime(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc));
        var auth = new FakeZoomHandler((_, n) => Task.FromResult(token(n)));
        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        var tokens = new ZoomTokenCache(new HttpClient(auth), wrapped, clock);
        return (new ZoomProviderStatus(tokens, wrapped, clock), auth, clock);
    }

    [Fact]
    public async Task Without_credentials_it_is_not_configured_and_never_calls_zoom()
    {
        var (status, auth, _) = Build(new ZoomOptions(), _ => FakeZoomHandler.Token("t"));

        var dto = await status.GetAsync();

        Assert.Equal(new ProviderStatusDto(false, false), dto);
        Assert.Equal(0, auth.Calls);
    }

    [Fact]
    public async Task With_credentials_and_a_working_token_it_is_configured_and_healthy()
    {
        var (status, _, _) = Build(Configured(), _ => FakeZoomHandler.Token("t"));

        Assert.Equal(new ProviderStatusDto(true, true), await status.GetAsync());
    }

    [Fact]
    public async Task A_refused_token_is_configured_but_unhealthy()
    {
        var (status, _, _) = Build(Configured(), _ => FakeZoomHandler.Json(HttpStatusCode.Unauthorized, "{}"));

        Assert.Equal(new ProviderStatusDto(true, false), await status.GetAsync());
    }

    [Fact]
    public async Task A_zoom_that_never_answers_is_unhealthy_and_that_answer_is_cached()
    {
        var clock = new ManualClock(new DateTime(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc));
        var options = Configured();
        options.Timeout = TimeSpan.FromMilliseconds(150);
        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        var hanging = new HangingZoomHandler();
        var status = new ZoomProviderStatus(new ZoomTokenCache(new HttpClient(hanging), wrapped, clock), wrapped, clock);

        Assert.Equal(new ProviderStatusDto(true, false), await status.GetAsync());
        Assert.Equal(new ProviderStatusDto(true, false), await status.GetAsync()); // served from the cache: no second 150 ms wait
        Assert.Equal(1, hanging.Calls);
    }

    [Fact]
    public async Task The_answer_is_cached_briefly_so_the_status_screen_cannot_hammer_zoom()
    {
        var (status, auth, clock) = Build(Configured(), n => n == 1 ? FakeZoomHandler.Json(HttpStatusCode.ServiceUnavailable, "{}") : FakeZoomHandler.Token("t"));

        Assert.False((await status.GetAsync()).Healthy);
        Assert.False((await status.GetAsync()).Healthy);
        Assert.Equal(1, auth.Calls);

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True((await status.GetAsync()).Healthy);
    }

    [Fact]
    public async Task The_status_reveals_no_configuration_detail()
    {
        var (status, _, _) = Build(Configured(), _ => FakeZoomHandler.Token("t"));

        var json = System.Text.Json.JsonSerializer.Serialize(await status.GetAsync());

        Assert.DoesNotContain("test-secret-value", json);
        Assert.DoesNotContain("acct", json);
        Assert.DoesNotContain("h1", json);
        Assert.Equal(["Configured", "Healthy"], System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name));
    }
}
