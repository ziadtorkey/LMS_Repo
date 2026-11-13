using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Zoom;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.LiveSessions.LiveSessionTestKit;

namespace Lms.IntegrationTests.Zoom;

/// <summary>M4b Task 7 (#474, D54): the anonymous Zoom webhook, its checks in order, its receipts and its effect on one client only.</summary>
[Collection(ApiCollection.Name)]
public sealed class ZoomWebhookTests(LmsApiFactory factory) : IDisposable
{
    private const string Secret = "webhook-test-secret";

    private readonly List<ContentTestHost> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private sealed class FailingOnceSink : ILiveSessionAttendanceSink
    {
        private int _calls;

        public Task SessionEndedAsync(
            Guid sessionId, Guid classCourseId, DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyList<AttendanceParticipant> participants,
            CancellationToken ct = default) =>
            Interlocked.Increment(ref _calls) == 1 ? throw new InvalidOperationException("sink failed once") : Task.CompletedTask;
    }

    private ContentTestHost NewHost(bool failingSink = false)
    {
        var host = new ContentTestHost(factory, services =>
        {
            services.Configure<ZoomOptions>(o => o.WebhookSecretToken = Secret);
            if (failingSink)
            {
                services.AddSingleton<ILiveSessionAttendanceSink>(new FailingOnceSink());
            }
        });
        _hosts.Add(host);
        return host;
    }

    private static string Timestamp(DateTime at) => new DateTimeOffset(at).ToUnixTimeSeconds().ToString();

    private static HttpRequestMessage Signed(string json, DateTime? at = null, string secret = Secret, string? timestamp = null)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var ts = timestamp ?? Timestamp(at ?? DateTime.UtcNow);
        var message = Encoding.UTF8.GetBytes($"v0:{ts}:").Concat(body).ToArray();
        var signature = "v0=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message)).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, Routes.ZoomWebhook) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("x-zm-request-timestamp", ts);
        request.Headers.Add("x-zm-signature", signature);
        return request;
    }

    private static string Event(string name, string meetingId, string extra = "") =>
        "{\"event\":\"" + name + "\",\"event_ts\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        + ",\"payload\":{\"account_id\":\"a\",\"object\":{\"id\":\"" + meetingId + "\"" + extra + "}}}";

    private async Task<(ContentWorld World, string MeetingId, Guid SessionId)> SessionAsync(ContentTestHost host, string name, bool cloud = false)
    {
        var w = await host.SetUpAsync(name);
        await EnableAsync(factory, w);
        if (cloud)
        {
            await SetAsync(factory, w, SettingKeys.LiveCloudRecordingEnabled.Name, "true");
        }

        var created = Assert.Single(await CreateAsync(host, w.Teacher, w, Single(name)));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var session = await db.Set<LiveSession>().SingleAsync(s => s.Id == created.Id);
        return (w, session.ExternalMeetingId!, session.Id);
    }

    private async Task<string> StatusAsync(Guid sessionId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<LiveSession>().SingleAsync(s => s.Id == sessionId)).Status;
    }

    private async Task<bool> ReceiptExistsAsync(string json)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<ZoomWebhookReceipt>().AnyAsync(r => r.EventId == id);
    }

    [Fact]
    public async Task Webhook_with_bad_missing_or_stale_signature_is_401()
    {
        var host = NewHost();
        var json = Event("meeting.ended", "123");

        var unsigned = await host.SendAnonymousAsync(new HttpRequestMessage(HttpMethod.Post, Routes.ZoomWebhook) { Content = new StringContent(json) });
        var wrongSecret = await host.SendAnonymousAsync(Signed(json, secret: "someone-elses"));
        var stale = await host.SendAnonymousAsync(Signed(json, at: DateTime.UtcNow.AddMinutes(-6)));

        Assert.All([unsigned, wrongSecret, stale], r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Empty(await unsigned.Content.ReadAsStringAsync()); // no hint which check failed
    }

    [Fact]
    public async Task Webhook_signature_is_checked_over_the_raw_body()
    {
        var host = NewHost();
        var signedJson = Event("meeting.ended", "123");
        var request = Signed(signedJson);
        request.Content = new StringContent(signedJson.Replace(",\"event_ts\"", ", \"event_ts\""), Encoding.UTF8, "application/json"); // same JSON, other bytes

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAnonymousAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Webhook_oversize_body_is_413()
    {
        var host = NewHost();
        var huge = new string('x', ZoomWebhookLimit + 1);

        var response = await host.SendAnonymousAsync(Signed(Event("meeting.ended", "123", $",\"pad\":\"{huge}\"")));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private const int ZoomWebhookLimit = 256 * 1024;

    [Fact]
    public async Task Webhook_url_validation_challenge_is_answered_when_signed()
    {
        var host = NewHost();
        var json = """{"event":"endpoint.url_validation","payload":{"plainToken":"abc123"}}""";

        var response = await host.SendAnonymousAsync(Signed(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes("abc123"))).ToLowerInvariant();
        Assert.Equal(("abc123", expected), (body.GetProperty("plainToken").GetString(), body.GetProperty("encryptedToken").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAnonymousAsync(Signed(json, secret: "other"))).StatusCode);
    }

    [Fact]
    public async Task Started_then_ended_events_move_the_session_and_a_replay_is_processed_once()
    {
        var host = NewHost();
        var (_, meeting, id) = await SessionAsync(host, "ZW1");
        var started = Event("meeting.started", meeting);
        var ended = Event("meeting.ended", meeting);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAnonymousAsync(Signed(started))).StatusCode);
        Assert.Equal(LiveSessionStatuses.Live, await StatusAsync(id));
        Assert.Equal(HttpStatusCode.OK, (await host.SendAnonymousAsync(Signed(ended))).StatusCode);
        Assert.Equal(LiveSessionStatuses.Ended, await StatusAsync(id));
        Assert.True(await ReceiptExistsAsync(ended));

        var replay = await host.SendAnonymousAsync(Signed(ended)); // fresh timestamp and signature, same body: the same event again
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ended))).ToLowerInvariant();
        Assert.Equal(1, await db.Set<ZoomWebhookReceipt>().CountAsync(r => r.EventId == hash));
    }

    [Fact]
    public async Task Failure_after_the_effect_rolls_back_so_the_retry_is_processed()
    {
        var host = NewHost(failingSink: true);
        var (_, meeting, id) = await SessionAsync(host, "ZW2");
        var ended = Event("meeting.ended", meeting);

        var first = await host.SendAnonymousAsync(Signed(ended));

        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.NotEqual(LiveSessionStatuses.Ended, await StatusAsync(id)); // the status change was rolled back with the receipt
        Assert.False(await ReceiptExistsAsync(ended));

        var retry = await host.SendAnonymousAsync(Signed(ended));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(LiveSessionStatuses.Ended, await StatusAsync(id));
        Assert.True(await ReceiptExistsAsync(ended));
    }

    [Fact]
    public async Task Webhook_for_client_a_meeting_cannot_touch_client_b_rows()
    {
        var host = NewHost();
        var a = await SessionAsync(host, "ZW3A");
        var b = await SessionAsync(host, "ZW3B");

        await host.SendAnonymousAsync(Signed(Event("meeting.ended", a.MeetingId)));

        Assert.Equal(LiveSessionStatuses.Ended, await StatusAsync(a.SessionId));
        Assert.Equal(LiveSessionStatuses.Scheduled, await StatusAsync(b.SessionId));
    }

    [Fact]
    public async Task Unknown_meeting_id_is_200_noop_and_the_receipt_holds_no_client_data()
    {
        var host = NewHost();
        var json = Event("meeting.ended", "999999999999");

        Assert.Equal(HttpStatusCode.OK, (await host.SendAnonymousAsync(Signed(json))).StatusCode);

        Assert.True(await ReceiptExistsAsync(json));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(["EventId", "Id", "ReceivedAtUtc"], db.Model.FindEntityType(typeof(ZoomWebhookReceipt))!.GetProperties().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Recording_completed_attaches_the_provider_link_only_when_cloud_recording_is_enabled()
    {
        var host = NewHost();
        var on = await SessionAsync(host, "ZW4A", cloud: true);
        var off = await SessionAsync(host, "ZW4B");
        const string files = ",\"recording_files\":[{\"status\":\"completed\",\"play_url\":\"https://zoom.test/rec/play/1\"}]";

        await host.SendAnonymousAsync(Signed(Event("recording.completed", on.MeetingId, files)));
        await host.SendAnonymousAsync(Signed(Event("recording.completed", off.MeetingId, files)));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var enabled = await db.Set<LiveSession>().SingleAsync(s => s.Id == on.SessionId);
        Assert.Equal(("https://zoom.test/rec/play/1", RecordingSources.Provider), (enabled.RecordingUrl, enabled.RecordingSource));
        Assert.Null((await db.Set<LiveSession>().SingleAsync(s => s.Id == off.SessionId)).RecordingUrl);
    }

    [Fact]
    public async Task A_valid_burst_is_never_429_and_repeated_failures_from_one_address_are()
    {
        var host = NewHost();
        var (_, meeting, _) = await SessionAsync(host, "ZW5");

        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.SendAnonymousAsync(Signed(Event("meeting.started", meeting, $",\"n\":{i}")))).StatusCode);
        }

        HttpStatusCode last = default;
        for (var i = 0; i < 25; i++)
        {
            last = (await host.SendAnonymousAsync(Signed("{}", secret: "bad"))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
        // The address is blocked for bad requests, but Zoom's validly signed ones still get through (a shared proxy address must not lock Zoom out).
        Assert.Equal(HttpStatusCode.OK, (await host.SendAnonymousAsync(Signed(Event("meeting.started", meeting, ",\"after\":1")))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.SendAnonymousAsync(Signed("{}", secret: "bad"))).StatusCode);
    }
}
