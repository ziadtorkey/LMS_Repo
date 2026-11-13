using System.Globalization;
using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.LiveSessions;

namespace Lms.Client.Services.LiveSessions;

/// <summary>HTTP client of the live session routes.</summary>
public sealed class LiveSessionsApi(ApiHttp http) : ILiveSessionsApi
{
    public Task<PagedResult<LiveSessionListItemDto>> ListAsync(Guid classCourseId, LiveSessionQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<LiveSessionListItemDto>>(Routes.ClassCourseLiveSessions(classCourseId) + ToQueryString(query), ct);

    public async Task<IReadOnlyList<LiveSessionDto>> CreateAsync(Guid classCourseId, CreateLiveSessionRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<List<LiveSessionDto>>(HttpMethod.Post, Routes.ClassCourseLiveSessions(classCourseId), request, ct))!;

    public Task<LiveSessionDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<LiveSessionDto>(Routes.LiveSession(id), ct);

    public async Task<LiveSessionDto> UpdateAsync(Guid id, UpdateLiveSessionRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<LiveSessionDto>(HttpMethod.Put, Routes.LiveSession(id), request, ct))!;

    public Task DeleteAsync(Guid id, string scope = SeriesEditScopes.This, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.LiveSession(id, Uri.EscapeDataString(scope)), null, ct);

    public async Task<LiveSessionJoinDto> JoinAsync(Guid id, CancellationToken ct = default) =>
        (await http.SendAsync<LiveSessionJoinDto>(HttpMethod.Post, Routes.LiveSessionJoin(id), null, ct))!;

    public async Task<LiveSessionDto> SetRecordingAsync(Guid id, RecordingLinkRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<LiveSessionDto>(HttpMethod.Put, Routes.LiveSessionRecording(id), request, ct))!;

    public Task<PagedResult<LiveParticipantDto>> ListParticipantsAsync(Guid id, LiveParticipantQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<LiveParticipantDto>>(Routes.LiveSessionParticipants(id) + ToQueryString(query), ct);

    public Task<ProviderStatusDto> GetProviderStatusAsync(CancellationToken ct = default) =>
        http.GetAsync<ProviderStatusDto>(Routes.LiveSessionsProviderStatus, ct);

    public async Task<IReadOnlyList<UpcomingLiveSessionDto>> ListUpcomingAsync(CancellationToken ct = default) =>
        (await http.GetAsync<List<UpcomingLiveSessionDto>>(Routes.LiveSessionsUpcoming, ct)) ?? [];

    /// <summary>Query string of a session list request; unset filters are left out. Dates are UTC ISO 8601 without fractions.</summary>
    public static string ToQueryString(LiveSessionQuery q)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.FromUtc is { } from) parts.Add($"fromUtc={Iso(from)}");
        if (q.ToUtc is { } to) parts.Add($"toUtc={Iso(to)}");
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        return "?" + string.Join('&', parts);
    }

    /// <summary>Query string of a participant list request; an unset filter is left out.</summary>
    public static string ToQueryString(LiveParticipantQuery q)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (!string.IsNullOrWhiteSpace(q.MatchState)) parts.Add($"matchState={Uri.EscapeDataString(q.MatchState)}");
        return "?" + string.Join('&', parts);
    }

    private static string Iso(DateTime utc) =>
        Uri.EscapeDataString(utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
