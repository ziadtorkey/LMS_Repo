using System.Text.Json;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Applies one verified Zoom webhook event to the session it names. The meeting id is the only key: it is resolved to the owning client
/// and session by <see cref="ResolveAsync"/> (the single cross-client lookup of the webhook, the id and the client only), and everything
/// after that is filtered to that client. An event for a meeting the platform does not know is a no-op. Runs with the platform context
/// inside the caller's transaction, so a failure rolls the event back and Zoom retries it.
/// </summary>
public sealed class ZoomEventProcessor(LmsDbContext db, LiveSessionLifecycle lifecycle, ISettingsResolver settings)
{
    public const string MeetingStarted = "meeting.started";
    public const string MeetingEnded = "meeting.ended";
    public const string RecordingCompleted = "recording.completed";

    /// <returns>True when a session was found and the event applied.</returns>
    public async Task<bool> ProcessAsync(string eventName, JsonElement payload, CancellationToken ct)
    {
        if (eventName is not (MeetingStarted or MeetingEnded or RecordingCompleted))
        {
            return false;
        }

        if (MeetingIdOf(payload) is not { } meetingId || await ResolveAsync(meetingId, ct) is not { } target)
        {
            return false;
        }

        switch (eventName)
        {
            case MeetingStarted:
                await lifecycle.StartedAsync(target.SessionId, ct);
                break;
            case MeetingEnded:
                await lifecycle.EndedAsync(target.SessionId, ct);
                break;
            default:
                await AttachRecordingAsync(target, payload, ct);
                break;
        }

        return true;
    }

    /// <summary>A recording link from Zoom, only when cloud recording is enabled for the unit; a link the teacher pasted is never overwritten.</summary>
    private async Task AttachRecordingAsync(SessionTarget target, JsonElement payload, CancellationToken ct)
    {
        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == target.SessionId && s.OrganizationId == target.OrganizationId, ct);
        if (session is null
            || !(await settings.ResolveAsync(SettingKeys.LiveCloudRecordingEnabled, session.OrgUnitPath, ct)).Value
            || session.RecordingSource == LiveSessionRules.RecordingManual)
        {
            return;
        }

        var url = RecordingUrlOf(payload);
        if (url is null)
        {
            return;
        }

        session.SetRecording(url, LiveSessionRules.RecordingProvider);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ISOLATION-BYPASS lookup: the webhook is anonymous and names only the provider's meeting id, so the owning client cannot come from
    /// the caller. The projection returns the session id and the client id and nothing else.
    /// </summary>
    public async Task<SessionTarget?> ResolveAsync(string meetingId, CancellationToken ct) =>
        // ISOLATION-BYPASS: anonymous provider event; the meeting id is the only key, so the lookup crosses clients; id and client id only.
        await db.Set<LiveSession>().IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.ExternalMeetingId == meetingId)
            .Select(s => new SessionTarget(s.Id, s.OrganizationId))
            .FirstOrDefaultAsync(ct);

    public sealed record SessionTarget(Guid SessionId, Guid OrganizationId);

    /// <summary>The meeting id of an event: <c>payload.object.id</c>, a number or a string in Zoom's JSON.</summary>
    public static string? MeetingIdOf(JsonElement payload)
    {
        if (!payload.TryGetProperty("object", out var obj) || !obj.TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.ValueKind switch
        {
            JsonValueKind.Number => id.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => id.GetString(),
            _ => null,
        };
    }

    /// <summary>The first finished recording file with an http(s) play link.</summary>
    private static string? RecordingUrlOf(JsonElement payload)
    {
        if (!payload.TryGetProperty("object", out var obj) || !obj.TryGetProperty("recording_files", out var files)
            || files.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var file in files.EnumerateArray())
        {
            if (file.TryGetProperty("play_url", out var play) && play.ValueKind == JsonValueKind.String
                && Uri.TryCreate(play.GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && uri.OriginalString.Length <= LiveSessionLimits.RecordingUrlMaxLength
                && (!file.TryGetProperty("status", out var status) || status.GetString() == "completed"))
            {
                return uri.OriginalString;
            }
        }

        return null;
    }
}
