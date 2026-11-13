using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Shared.LiveSessions;

namespace Lms.Infrastructure.Zoom;

/// <summary>One scheduled meeting to create or change. <c>Topic</c> is the teacher's session title and nothing else (Decision 5).</summary>
public sealed record ZoomMeetingRequest(
    string HostId, string Topic, DateTime StartUtc, int DurationMinutes, string TimeZoneId, bool CloudRecording,
    IReadOnlyDictionary<string, string> Tracking);

/// <summary>What Zoom returned for a meeting. The start URL grants host rights: it never leaves the server except to staff.</summary>
public sealed record ZoomMeetingInfo(string MeetingId, string JoinUrl, string StartUrl);

/// <summary>
/// Typed client for the few Zoom REST calls the platform needs. The auth, limiting and retry pipeline is <see cref="ZoomApiHandler"/>;
/// failures surface as <see cref="ZoomProviderException"/> carrying the status code only, never the response body.
/// </summary>
public sealed class ZoomApiClient(HttpClient http)
{
    public async Task<ZoomMeetingInfo> CreateMeetingAsync(ZoomMeetingRequest meeting, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post, $"users/{Uri.EscapeDataString(meeting.HostId)}/meetings", Body(meeting), ct);
        await EnsureSuccessAsync(response);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var id = root.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
            var join = root.GetProperty("join_url").GetString();
            var start = root.GetProperty("start_url").GetString();
            return join is null || start is null ? throw new JsonException() : new ZoomMeetingInfo(id, join, start);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ZoomProviderException(ZoomFailure.Unavailable, "Zoom meeting response was not understood.");
        }
    }

    public async Task UpdateMeetingAsync(string meetingId, ZoomMeetingRequest meeting, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"meetings/{Uri.EscapeDataString(meetingId)}", Body(meeting), ct);
        await EnsureSuccessAsync(response);
    }

    /// <summary>Deletes a meeting; one that is already gone counts as deleted.</summary>
    public async Task DeleteMeetingAsync(string meetingId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"meetings/{Uri.EscapeDataString(meetingId)}", null, ct);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response);
        }
    }

    /// <summary>True when the pool user exists, is active and holds a licensed seat (used when <c>VirtualClasses</c> is enabled).</summary>
    public async Task<bool> IsLicensedHostAsync(string hostId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(hostId)}", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            return root.GetProperty("status").GetString() == "active" && root.GetProperty("type").GetInt32() >= 2;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ZoomProviderException(ZoomFailure.Unavailable, "Zoom user response was not understood.");
        }
    }

    private static JsonContent Body(ZoomMeetingRequest meeting)
    {
        var topic = meeting.Topic?.Trim() ?? "";
        if (topic.Length is < LiveSessionLimits.TitleMinLength or > LiveSessionLimits.TitleMaxLength || topic.IndexOfAny(['<', '>']) >= 0)
        {
            throw new ArgumentException("The meeting topic must be the plain session title.", nameof(meeting));
        }

        return JsonContent.Create(new
        {
            topic,
            type = 2,
            start_time = meeting.StartUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            duration = meeting.DurationMinutes,
            timezone = meeting.TimeZoneId,
            settings = new
            {
                waiting_room = true,
                join_before_host = false,
                auto_recording = meeting.CloudRecording ? "cloud" : "none",
            },
            tracking_fields = meeting.Tracking.Select(kv => new { field = kv.Key, value = kv.Value }).ToArray(),
        });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        return await http.SendAsync(request, ct);
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Status only: the body can carry host links or account details.
            throw new ZoomProviderException(ZoomFailure.Unavailable, $"Zoom answered {(int)response.StatusCode}.");
        }

        return Task.CompletedTask;
    }
}
