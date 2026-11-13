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

/// <summary>
/// One row of Zoom's participant report. <c>ZoomUserId</c> is set only for a participant signed in to a Zoom account; a guest has none,
/// whatever email they typed. Only the fields the platform stores are read.
/// </summary>
public sealed record ZoomParticipant(
    string Name, string? Email, string? ZoomUserId, DateTime JoinedAtUtc, DateTime? LeftAtUtc, int DurationSeconds);

/// <summary>A recording file of a finished meeting that Zoom has finished processing.</summary>
public sealed record ZoomRecordingFile(string PlayUrl, string Kind);

/// <summary>What Zoom returned for a meeting. The start URL grants host rights: it never leaves the server except to staff.</summary>
public sealed record ZoomMeetingInfo(string MeetingId, string JoinUrl, string StartUrl);

/// <summary>
/// Typed client for the few Zoom REST calls the platform needs. The auth, limiting and retry pipeline is <see cref="ZoomApiHandler"/>;
/// failures surface as <see cref="ZoomProviderException"/> carrying the status code only, never the response body.
/// </summary>
public sealed class ZoomApiClient(HttpClient http)
{
    /// <summary>The only tracking field names sent to Zoom: the client and the class course, as ids.</summary>
    public static readonly IReadOnlySet<string> TrackingFields = new HashSet<string> { "client", "classCourse" };

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

    /// <summary>The participant report of a finished meeting, all pages (at most 20). A meeting Zoom has no report for yet is an empty list.</summary>
    public async Task<IReadOnlyList<ZoomParticipant>> GetParticipantsAsync(string meetingId, CancellationToken ct = default)
    {
        var all = new List<ZoomParticipant>();
        string? page = null;
        for (var i = 0; i < 20; i++)
        {
            var path = $"report/meetings/{Uri.EscapeDataString(meetingId)}/participants?page_size=300"
                       + (page is null ? "" : "&next_page_token=" + Uri.EscapeDataString(page));
            using var response = await SendAsync(HttpMethod.Get, path, null, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return all;
            }

            await EnsureSuccessAsync(response);
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = json.RootElement;
                foreach (var row in root.GetProperty("participants").EnumerateArray())
                {
                    var id = Text(row, "id");
                    var email = Text(row, "user_email");
                    var left = Text(row, "leave_time");
                    all.Add(new ZoomParticipant(
                        Text(row, "name") ?? "", email, id,
                        DateTime.Parse(row.GetProperty("join_time").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                        left is null ? null : DateTime.Parse(left, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                        row.TryGetProperty("duration", out var d) && d.TryGetInt32(out var seconds) ? seconds : 0));
                }

                page = Text(root, "next_page_token");
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                throw new ZoomProviderException(ZoomFailure.Unavailable, "Zoom participant report was not understood.");
            }

            if (page is null)
            {
                break;
            }
        }

        return all;
    }

    /// <summary>The finished recording files of a meeting (cloud recording only). A meeting with none is an empty list.</summary>
    public async Task<IReadOnlyList<ZoomRecordingFile>> GetRecordingsAsync(string meetingId, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"meetings/{Uri.EscapeDataString(meetingId)}/recordings", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        await EnsureSuccessAsync(response);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("recording_files", out var files))
            {
                return [];
            }

            return [.. files.EnumerateArray()
                .Where(f => Text(f, "status") == "completed" && Text(f, "play_url") is not null)
                .Select(f => new ZoomRecordingFile(Text(f, "play_url")!, Text(f, "recording_type") ?? "recording"))];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ZoomProviderException(ZoomFailure.Unavailable, "Zoom recordings response was not understood.");
        }
    }

    /// <summary>A string property, or null when it is missing, not a string or blank.</summary>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static JsonContent Body(ZoomMeetingRequest meeting)
    {
        var topic = meeting.Topic?.Trim() ?? "";
        if (topic.Length is < LiveSessionLimits.TitleMinLength or > LiveSessionLimits.TitleMaxLength || topic.IndexOfAny(['<', '>']) >= 0)
        {
            throw new ArgumentException("The meeting topic must be the plain session title.", nameof(meeting));
        }

        // Tracking fields are for support only and fixed by the platform: unknown names or odd values are a programming error, never data to forward.
        foreach (var (key, value) in meeting.Tracking)
        {
            if (!TrackingFields.Contains(key) || value.Length is < 1 or > 36 || !value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                throw new ArgumentException("Only the platform tracking fields with plain ids are allowed.", nameof(meeting));
            }
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
