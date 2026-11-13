using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.LiveSessions;

/// <summary>The webhook secret token the dev trigger signs with (<c>Zoom:WebhookSecretToken</c>, the same key the verifier uses).</summary>
public sealed class DevZoomOptions
{
    public string? WebhookSecretToken { get; set; }
}

/// <summary>What a Development caller asks the dev trigger to send as if Zoom had sent it. Private to this endpoint, not part of Lms.Shared.</summary>
public sealed record DevZoomEventRequest(string Event, Guid SessionId, string? Name = null, string? Email = null, string? RecordingUrl = null);

/// <summary>The answer of the dev trigger: what the real webhook route said to the signed event.</summary>
public sealed record DevZoomEventResult(string Event, int WebhookStatus);

/// <summary>The kinds of event the dev trigger can send, as Zoom names them on the wire.</summary>
public static class DevZoomEventKinds
{
    public const string Started = "started";
    public const string Ended = "ended";
    public const string RecordingReady = "recording_ready";
    public const string ParticipantJoined = "participant_joined";

    public static IReadOnlyList<string> All { get; } = [Started, Ended, RecordingReady, ParticipantJoined];

    public static string ZoomName(string kind) => kind switch
    {
        Started => "meeting.started",
        Ended => "meeting.ended",
        RecordingReady => "recording.completed",
        ParticipantJoined => "meeting.participant_joined",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>Builds the Zoom-shaped body of a fake event and signs it the way Zoom does (<c>v0=</c> + hex HMAC-SHA256 of <c>v0:{timestamp}:{body}</c>).</summary>
public static class DevZoomEventBuilder
{
    public const string SignatureHeader = "x-zm-signature";
    public const string TimestampHeader = "x-zm-request-timestamp";

    public static string Body(DevZoomEventRequest request, string meetingId, string topic, DateTime nowUtc)
    {
        var at = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        object obj = request.Event switch
        {
            DevZoomEventKinds.RecordingReady => new
            {
                id = meetingId, topic,
                recording_files = new[]
                {
                    new { file_type = "MP4", recording_type = "shared_screen_with_speaker_view", play_url = request.RecordingUrl ?? "https://recordings.fake.zoom.test/play/" + meetingId },
                },
            },
            DevZoomEventKinds.ParticipantJoined => new
            {
                id = meetingId, topic,
                participant = new { user_name = request.Name ?? "Guest", email = request.Email ?? string.Empty, join_time = at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) },
            },
            _ => new { id = meetingId, topic },
        };
        return JsonSerializer.Serialize(new
        {
            @event = DevZoomEventKinds.ZoomName(request.Event),
            event_ts = at.ToUnixTimeMilliseconds(),
            payload = new { account_id = "dev-account", @object = obj },
        });
    }

    public static string Sign(string secretToken, long timestampSeconds, string body) =>
        "v0=" + Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secretToken), Encoding.UTF8.GetBytes($"v0:{timestampSeconds.ToString(CultureInfo.InvariantCulture)}:{body}"))).ToLowerInvariant();
}

/// <summary>
/// <c>POST /api/dev/zoom/events</c> (M4b plan Task 22 support): Development only, platform admins only. It signs a fake Zoom event for a live
/// session with the configured webhook secret and posts it to the real webhook route (<see cref="Routes.ZoomWebhook"/>) over the named
/// <c>DevZoomLoopback</c> client, so the E2E demo drives exactly the code Zoom would. For a participant it also tells the fake provider, so
/// the participants the ended event imports are there.
/// </summary>
public static class DevZoomEvents
{
    public const string LoopbackClient = "DevZoomLoopback";

    public static async Task<IResult> HandleAsync(
        DevZoomEventRequest? request, HttpContext http, LmsDbContext db, Microsoft.Extensions.Options.IOptions<DevZoomOptions> options, IClock clock,
        IHttpClientFactory clients, IMeetingProvider provider, CancellationToken ct)
    {
        if (request is null || !DevZoomEventKinds.All.Contains(request.Event))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Unknown event.");
        }

        var secret = options.Value.WebhookSecretToken;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return ApiProblems.Conflict(ProblemCodes.provider_not_configured, "Set Zoom:WebhookSecretToken to send signed test events.");
        }

        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);
        if (session is null)
        {
            return ApiProblems.NotFound();
        }

        if (session.ExternalMeetingId is null)
        {
            return ApiProblems.Conflict(ProblemCodes.live_session_not_open, "The session has no meeting at the provider yet.");
        }

        if (request.Event == DevZoomEventKinds.ParticipantJoined && provider is FakeMeetingProvider fake)
        {
            var joined = clock.UtcNow;
            fake.ParticipantsOf.AddOrUpdate(
                session.ExternalMeetingId,
                _ => [new MeetingParticipant(request.Name ?? "Guest", request.Email, joined, null, 0)],
                (_, list) => { list.Add(new MeetingParticipant(request.Name ?? "Guest", request.Email, joined, null, 0)); return list; });
        }

        var now = clock.UtcNow;
        var body = DevZoomEventBuilder.Body(request, session.ExternalMeetingId, "Dev event", now);
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeSeconds();
        using var message = new HttpRequestMessage(HttpMethod.Post, Routes.ZoomWebhook) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        message.Headers.Add(DevZoomEventBuilder.TimestampHeader, seconds.ToString(CultureInfo.InvariantCulture));
        message.Headers.Add(DevZoomEventBuilder.SignatureHeader, DevZoomEventBuilder.Sign(secret, seconds, body));

        var client = clients.CreateClient(LoopbackClient);
        client.BaseAddress ??= new Uri($"{http.Request.Scheme}://{http.Request.Host}");
        using var response = await client.SendAsync(message, ct);
        return Results.Json(new DevZoomEventResult(request.Event, (int)response.StatusCode), statusCode: (int)HttpStatusCode.Accepted);
    }
}
