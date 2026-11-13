using System.Net;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Zoom;
using Lms.Shared;
using Microsoft.Extensions.Options;

namespace Lms.UnitTests.Zoom;

public sealed class VirtualClassesEnableGuardTests
{
    private static (VirtualClassesEnableGuard Guard, FakeZoomHandler Api) Build(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> zoom)
    {
        var options = Options.Create(new ZoomOptions
        {
            AccountId = "acct", ClientId = "cid", ClientSecret = "test-secret-value", HostPool = ["h1@pool.test", "h2@pool.test"],
        });
        var clock = new ManualClock(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var auth = new FakeZoomHandler((_, n) => Task.FromResult(FakeZoomHandler.Token("tok" + n)));
        var tokens = new ZoomTokenCache(new HttpClient(auth), options, clock);
        var api = new FakeZoomHandler(zoom);
        var client = new ZoomApiClient(new HttpClient(new ZoomApiHandler(tokens, new ZoomOutboundLimiter(clock), options) { InnerHandler = api })
        {
            BaseAddress = new Uri("https://api.zoom.test/v2/"),
        });
        var provider = new ZoomMeetingProvider(
            client, new ZoomHostAllocator(options, new InProcessJobKeyLock(), new NoSlots(), () => 100), new ZoomProviderStatus(tokens, options, clock), options);
        return (new VirtualClassesEnableGuard(provider, client, options), api);
    }

    private sealed class NoSlots : IHostSlotStore
    {
        public Task<IReadOnlyList<HostSlot>> SlotsAroundAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<HostSlot>>([]);
    }

    private static readonly OrgUnit Unit = null!; // the guard does not read the unit

    private static Task<HttpResponseMessage> User(string type, string status = "active") =>
        Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.OK, $$"""{"id":"u","type":{{type}},"status":"{{status}}"}"""));

    [Fact]
    public async Task Every_pool_host_licensed_and_active_lets_the_flag_be_enabled()
    {
        var (guard, api) = Build((_, _) => User("2"));

        Assert.Null(await guard.RefuseAsync(Unit, "true", CancellationToken.None));
        Assert.Equal(2, api.Calls); // one look-up per pool host
    }

    [Fact]
    public async Task One_unlicensed_or_missing_pool_host_refuses_with_provider_not_configured()
    {
        var (unlicensed, _) = Build((r, _) => r.RequestUri!.AbsolutePath.Contains("h2") ? User("1") : User("2"));
        var (missing, _) = Build((r, _) => r.RequestUri!.AbsolutePath.Contains("h2")
            ? Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.NotFound, "{}")) : User("2"));

        Assert.Equal(ProblemCodes.provider_not_configured, await unlicensed.RefuseAsync(Unit, "true", CancellationToken.None));
        Assert.Equal(ProblemCodes.provider_not_configured, await missing.RefuseAsync(Unit, "true", CancellationToken.None));
    }

    [Fact]
    public async Task A_zoom_that_cannot_answer_refuses_the_change()
    {
        var (guard, _) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.BadGateway, "{}")));

        Assert.Equal(ProblemCodes.provider_not_configured, await guard.RefuseAsync(Unit, "true", CancellationToken.None));
    }

    [Fact]
    public async Task Turning_the_flag_off_is_never_refused_and_asks_zoom_nothing()
    {
        var (guard, api) = Build((_, _) => Task.FromResult(FakeZoomHandler.Json(HttpStatusCode.BadGateway, "{}")));

        Assert.Null(await guard.RefuseAsync(Unit, "false", CancellationToken.None));
        Assert.Equal(0, api.Calls);
    }
}
