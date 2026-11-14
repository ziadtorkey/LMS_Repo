using System.Net;
using Lib.Net.Http.WebPush;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms.Push;
using Microsoft.Extensions.Options;

namespace Lms.UnitTests.Comms;

/// <summary>M6 Task 8 (SSRF guard): only the hosts of the real push services are ever called, and a redirect is never followed.</summary>
public sealed class PushEndpointPolicyTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://web.push.apple.com/abc")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc")]
    [InlineData("https://FCM.GoogleAPIs.com/x")]
    public void Real_push_service_endpoints_are_allowed(string endpoint) => Assert.True(PushEndpointPolicy.IsAllowed(endpoint));

    [Theory]
    [InlineData("https://localhost/x")]
    [InlineData("https://app.localhost/x")]
    [InlineData("https://10.0.0.1/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://[::ffff:10.0.0.1]/x")]
    [InlineData("https://evil.example/x")]
    [InlineData("https://fcm.googleapis.com.evil.example/x")]
    [InlineData("https://evilfcm.googleapis.com.example/x")]
    [InlineData("https://notfcm.googleapis.com/x")]
    [InlineData("https://fcm.googleapis.com:8443/x")]
    [InlineData("https://user:pw@fcm.googleapis.com/x")]
    [InlineData("http://fcm.googleapis.com/x")]
    [InlineData("ftp://fcm.googleapis.com/x")]
    [InlineData("//fcm.googleapis.com/x")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? endpoint) => Assert.False(PushEndpointPolicy.IsAllowed(endpoint));

    [Fact]
    public void The_allowed_hosts_can_be_set_by_configuration()
    {
        Assert.True(PushEndpointPolicy.IsAllowed("https://push.example.test/x", ["push.example.test"]));
        Assert.False(PushEndpointPolicy.IsAllowed("https://fcm.googleapis.com/x", ["push.example.test"]));
    }

    [Fact]
    public void The_push_client_never_follows_a_redirect()
    {
        using var handler = PushEndpointPolicy.CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task A_stored_endpoint_outside_the_allowed_hosts_is_not_called_at_send_time_and_is_reported_gone()
    {
        var calls = 0;
        using var http = new HttpClient(new CountingHandler(() => calls++));
        var options = Options.Create(new PushOptions { Subject = "mailto:a@b.test", PublicKey = "PUB", PrivateKey = "PRIV" });
        var provider = new VapidPushProvider(new PushServiceClient(http), options);

        var internalHost = await provider.SendAsync(new PushTarget("https://10.0.0.5/x", "k", "a"), [1], TimeSpan.FromHours(1), false, CancellationToken.None);
        var lookalike = await provider.SendAsync(new PushTarget("https://fcm.googleapis.com.evil.example/x", "k", "a"), [1], TimeSpan.FromHours(1), false, CancellationToken.None);

        Assert.Equal((PushResultKind.Gone, PushResultKind.Gone), (internalHost.Kind, lookalike.Kind));
        Assert.Equal("push_endpoint_refused", internalHost.ErrorCode);
        Assert.Equal(0, calls);
    }

    private sealed class CountingHandler(Action onCall) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onCall();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }
    }
}
