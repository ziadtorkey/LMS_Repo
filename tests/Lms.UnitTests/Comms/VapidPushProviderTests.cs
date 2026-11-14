using System.Net;
using Lib.Net.Http.WebPush;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms.Push;
using Microsoft.Extensions.Options;

namespace Lms.UnitTests.Comms;

/// <summary>M6 Task 8: the push service's answers map to retry, gone or permanent; without a key nothing is sent and the key is never exposed.</summary>
public sealed class VapidPushProviderTests
{
    [Theory]
    [InlineData(404, PushResultKind.Gone)]
    [InlineData(410, PushResultKind.Gone)]
    [InlineData(429, PushResultKind.Transient)]
    [InlineData(500, PushResultKind.Transient)]
    [InlineData(503, PushResultKind.Transient)]
    [InlineData(400, PushResultKind.Permanent)]
    [InlineData(401, PushResultKind.Permanent)]
    [InlineData(403, PushResultKind.Permanent)]
    [InlineData(413, PushResultKind.Permanent)]
    public void Status_codes_map_to_a_result_kind(int status, PushResultKind kind)
    {
        Assert.Equal(kind, VapidPushProvider.Map((HttpStatusCode)status, null).Kind);
    }

    [Fact]
    public void Retry_after_of_the_push_service_is_kept_and_only_a_code_is_returned()
    {
        var result = VapidPushProvider.Map(HttpStatusCode.TooManyRequests, TimeSpan.FromMinutes(2));

        Assert.Equal((PushResultKind.Transient, "push_429", (TimeSpan?)TimeSpan.FromMinutes(2)), (result.Kind, result.ErrorCode, result.RetryAfter));
    }

    [Fact]
    public async Task Without_a_vapid_key_nothing_is_sent_and_no_public_key_is_offered()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var provider = new VapidPushProvider(new PushServiceClient(http), Options.Create(new PushOptions { Subject = "mailto:dev@lms.test" }));

        var result = await provider.SendAsync(new PushTarget("https://push.example.test/x", "k", "a"), [1], TimeSpan.FromHours(1), false, CancellationToken.None);

        Assert.Equal(PushResultKind.NotConfigured, result.Kind);
        Assert.Null(provider.PublicKey);
    }

    [Fact]
    public void The_private_key_is_never_part_of_the_public_surface()
    {
        var options = new PushOptions { Subject = "mailto:a@b.test", PublicKey = "PUB", PrivateKey = "PRIVATE-SECRET" };
        var provider = new VapidPushProvider(new PushServiceClient(new HttpClient(new ThrowingHandler())), Options.Create(options));

        Assert.Equal("PUB", provider.PublicKey);
        Assert.DoesNotContain("PRIVATE-SECRET", provider.PublicKey);
        Assert.DoesNotContain(typeof(IPushProvider).GetProperties(), p => p.Name.Contains("Private", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("no request may be made");
    }
}
