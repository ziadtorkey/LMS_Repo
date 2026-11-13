using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.LiveSessions;

namespace Lms.Client.Services.LiveSessions;

/// <summary>
/// Live sessions (FR-VC-01): schedule, edit and cancel sessions and series, join, recordings, participants and the
/// platform provider status. Routes: <see cref="Routes.LiveSessions"/> and the class-course live-session route. No call
/// ever returns a join or start URL except <see cref="JoinAsync"/>; the client never stores that URL.
/// </summary>
public interface ILiveSessionsApi
{
    /// <summary>GET /api/class-courses/{id}/live-sessions — paged, ordered by start.</summary>
    Task<PagedResult<LiveSessionListItemDto>> ListAsync(Guid classCourseId, LiveSessionQuery query, CancellationToken ct = default);

    /// <summary>POST /api/class-courses/{id}/live-sessions — one session, or one per occurrence of a series (in start order).</summary>
    Task<IReadOnlyList<LiveSessionDto>> CreateAsync(Guid classCourseId, CreateLiveSessionRequest request, CancellationToken ct = default);

    /// <summary>GET /api/live-sessions/{id}.</summary>
    Task<LiveSessionDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>PUT /api/live-sessions/{id} — the edited session as stored (the scope decides which other occurrences change).</summary>
    Task<LiveSessionDto> UpdateAsync(Guid id, UpdateLiveSessionRequest request, CancellationToken ct = default);

    /// <summary>DELETE /api/live-sessions/{id}?scope= — cancels the session (the row stays); <see cref="SeriesEditScopes"/>.</summary>
    Task DeleteAsync(Guid id, string scope = SeriesEditScopes.This, CancellationToken ct = default);

    /// <summary>POST /api/live-sessions/{id}/join — the URL to open; 409 <c>live_session_not_open</c> outside the join window.</summary>
    Task<LiveSessionJoinDto> JoinAsync(Guid id, CancellationToken ct = default);

    /// <summary>PUT /api/live-sessions/{id}/recording — a manual http/https recording link.</summary>
    Task<LiveSessionDto> SetRecordingAsync(Guid id, RecordingLinkRequest request, CancellationToken ct = default);

    /// <summary>GET /api/live-sessions/{id}/participants — staff only, paged.</summary>
    Task<PagedResult<LiveParticipantDto>> ListParticipantsAsync(Guid id, LiveParticipantQuery query, CancellationToken ct = default);

    /// <summary>GET /api/live-sessions/provider-status — whether the platform Zoom app is configured and answering.</summary>
    Task<ProviderStatusDto> GetProviderStatusAsync(CancellationToken ct = default);

    /// <summary>GET /api/live-sessions/upcoming — the caller's next sessions (at most <see cref="LiveSessionLimits.UpcomingMax"/>).</summary>
    Task<IReadOnlyList<UpcomingLiveSessionDto>> ListUpcomingAsync(CancellationToken ct = default);
}
