using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Api.Features.LiveSessions;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.LiveSessions.LiveSessionTestKit;

namespace Lms.IntegrationTests.LiveSessions;

/// <summary>M4b Task 22 support: the Development-only signed Zoom event trigger for the E2E demo.</summary>
[Collection(ApiCollection.Name)]
public sealed class DevZoomEventsTests(LmsApiFactory factory) : IDisposable
{
    private const string Secret = "dev-secret-token";

    private sealed record Sent(string Path, string Body, string Timestamp, string Signature);

    private sealed class Capture(ConcurrentQueue<Sent> sent) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent.Enqueue(new Sent(
                request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken),
                request.Headers.GetValues(DevZoomEventBuilder.TimestampHeader).Single(), request.Headers.GetValues(DevZoomEventBuilder.SignatureHeader).Single()));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private readonly List<ContentTestHost> _hosts = [];
    private readonly ConcurrentQueue<Sent> _sent = new();

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private sealed record Setup(ContentTestHost Host, Guid SessionId, string MeetingId);

    private async Task<Setup> SetUpAsync(string name, string? secret = Secret)
    {
        var host = new ContentTestHost(factory, services =>
        {
            services.Configure<DevZoomOptions>(o => o.WebhookSecretToken = secret);
            services.AddHttpClient(DevZoomEvents.LoopbackClient).ConfigurePrimaryHttpMessageHandler(() => new Capture(_sent));
        });
        _hosts.Add(host);
        var w = await host.SetUpAsync(name);
        await EnableAsync(factory, w);
        var session = Assert.Single(await CreateAsync(host, w.Teacher, w, Single()));
        return new Setup(host, session.Id, Provider(host).Meetings.Keys.Single());
    }

    private static HttpRequestMessage Post(object body, bool admin = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Routes.DevZoomEvents) { Content = JsonContent.Create(body) };
        request.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        if (admin)
        {
            request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        }

        return request;
    }

    [Theory]
    [InlineData("started", "meeting.started")]
    [InlineData("ended", "meeting.ended")]
    [InlineData("recording_ready", "recording.completed")]
    [InlineData("participant_joined", "meeting.participant_joined")]
    public async Task A_platform_admin_sends_a_signed_zoom_shaped_event_to_the_webhook_route(string kind, string zoomName)
    {
        var s = await SetUpAsync("DZ-" + kind);

        var response = await s.Host.CreateClient().SendAsync(Post(new DevZoomEventRequest(kind, s.SessionId, "Sara", "sara@example.test", "https://rec.example.test/a")));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(204, (await response.Content.ReadFromJsonAsync<DevZoomEventResult>())!.WebhookStatus);
        var sent = Assert.Single(_sent);
        Assert.Equal(Routes.ZoomWebhook, sent.Path);
        // Signed the way Zoom signs: v0= + hex HMAC-SHA256 of "v0:{timestamp}:{raw body}" under the secret token.
        Assert.Equal(DevZoomEventBuilder.Sign(Secret, long.Parse(sent.Timestamp), sent.Body), sent.Signature);
        Assert.StartsWith("v0=", sent.Signature);
        Assert.InRange(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - long.Parse(sent.Timestamp)), 0, 30);
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal(zoomName, json.RootElement.GetProperty("event").GetString());
        var obj = json.RootElement.GetProperty("payload").GetProperty("object");
        Assert.Equal(s.MeetingId, obj.GetProperty("id").GetString());
        if (kind == "participant_joined")
        {
            Assert.Equal("Sara", obj.GetProperty("participant").GetProperty("user_name").GetString());
            Assert.Equal("sara@example.test", obj.GetProperty("participant").GetProperty("email").GetString());
            Assert.Equal("Sara", Provider(s.Host).ParticipantsOf[s.MeetingId].Single().DisplayName); // the fake reports them when the session ends
        }

        if (kind == "recording_ready")
        {
            Assert.Equal("https://rec.example.test/a", obj.GetProperty("recording_files")[0].GetProperty("play_url").GetString());
        }
    }

    [Fact]
    public async Task Only_platform_admins_known_events_known_sessions_and_a_configured_secret_are_accepted()
    {
        var s = await SetUpAsync("DZ-refuse");
        var noSecret = await SetUpAsync("DZ-nosecret", secret: null);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Host.CreateClient().SendAsync(Post(new DevZoomEventRequest("started", s.SessionId), admin: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Host.CreateClient().SendAsync(Post(new DevZoomEventRequest("exploded", s.SessionId)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Host.CreateClient().SendAsync(Post(new DevZoomEventRequest("started", Guid.NewGuid())))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await noSecret.Host.CreateClient().SendAsync(Post(new DevZoomEventRequest("started", noSecret.SessionId)))).StatusCode);
        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_route_is_not_mapped_outside_Development(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        using var app = builder.Build();

        LiveSessionsModule.MapDevEndpoints(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText).ToList();
        Assert.DoesNotContain(Routes.DevZoomEvents, routes); // the Development case is the accepted-event test above
    }
}
