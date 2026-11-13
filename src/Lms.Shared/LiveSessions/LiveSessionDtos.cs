using Lms.Shared.Content;

namespace Lms.Shared.LiveSessions;

/// <summary>
/// A weekly recurrence (Decision 6). Occurrences fall on <c>DaysOfWeek</c> (<see cref="RecurrenceDays"/>) every
/// <c>IntervalWeeks</c> weeks at the wall-clock time of <c>StartsAtUtc</c> in <c>TimeZoneId</c> (IANA, DST-aware; the
/// browser's zone) and are stored in UTC. Exactly one of <c>UntilUtc</c> / <c>Count</c>; at most
/// <see cref="LiveSessionLimits.MaxOccurrences"/> occurrences. Otherwise 400 <c>recurrence_invalid</c>.
/// </summary>
public sealed record LiveRecurrenceInput(
    int IntervalWeeks,
    IReadOnlyList<string> DaysOfWeek,
    DateTime? UntilUtc,
    int? Count,
    string TimeZoneId);

/// <summary>
/// POST /api/class-courses/{id}/live-sessions (LiveSessions.Manage + Modules.Manage on the ClassCourse;
/// <c>Features.VirtualClasses</c> on, else 403 <c>feature_disabled</c>). Creates the session (or one session per
/// occurrence of the series) and one module per occurrence under <c>LessonKey</c>, kind <c>LiveSession</c>, with the
/// module settings of <c>Module</c>. The title is <c>Module.TitleAr</c> / <c>Module.TitleEn</c>
/// (<see cref="LiveSessionLimits"/>) and is the only text sent to Zoom as the topic (Decision 5). Completion rule from
/// <c>CompletionRules.ForActivity(LiveSession)</c>. Duration 15–480 minutes. The daily create cap counts every
/// occurrence and refuses the whole series (409 <c>provider_unavailable</c>).
/// </summary>
public sealed record CreateLiveSessionRequest(
    Guid LessonKey,
    ModuleSettingsInput Module,
    DateTime StartsAtUtc,
    int DurationMinutes,
    LiveRecurrenceInput? Recurrence = null);

/// <summary>
/// PUT /api/live-sessions/{id} (LiveSessions.Manage). <c>Scope</c> from <see cref="SeriesEditScopes"/>; a single
/// session accepts only <c>This</c>. <c>Recurrence</c> is accepted only with <c>ThisAndFollowing</c> on a series and
/// re-creates the following occurrences from this one; earlier occurrences never change. The lesson never changes here
/// (move the module).
/// </summary>
public sealed record UpdateLiveSessionRequest(
    ModuleSettingsInput Module,
    DateTime StartsAtUtc,
    int DurationMinutes,
    string Scope = SeriesEditScopes.This,
    LiveRecurrenceInput? Recurrence = null);

/// <summary>The recurrence a series was created with (null on a single session).</summary>
public sealed record LiveRecurrenceDto(
    int IntervalWeeks,
    IReadOnlyList<string> DaysOfWeek,
    DateTime? UntilUtc,
    int? Count,
    string TimeZoneId);

/// <summary>
/// One live session occurrence (GET /api/live-sessions/{id}). Never carries the join or the start URL (only
/// POST …/join returns one); <c>RecordingUrl</c> is the http/https recording link once attached. Students receive it
/// only while its module is visible to them (else 404). <c>JoinOpensAtUtc</c> = start − <c>Live.JoinWindowMinutes</c>;
/// <c>CanEdit</c> for staff with LiveSessions.Manage on the ClassCourse.
/// </summary>
public sealed record LiveSessionDto(
    Guid Id,
    Guid ClassCourseId,
    Guid LessonKey,
    Guid? SeriesId,
    int? OccurrenceIndex,
    int? OccurrenceCount,
    ModuleNodeDto Module,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    int DurationMinutes,
    string Status,
    LiveRecurrenceDto? Recurrence,
    string? RecordingUrl,
    string? RecordingSource,
    DateTime JoinOpensAtUtc,
    bool CanEdit,
    DateTime ServerNowUtc);

/// <summary>
/// One row of GET /api/class-courses/{id}/live-sessions (paged, ordered by start). Students get only sessions whose
/// module is visible to them. No URL of any kind.
/// </summary>
public sealed record LiveSessionListItemDto(
    Guid Id,
    Guid ModuleId,
    Guid? SeriesId,
    int? OccurrenceIndex,
    string TitleAr,
    string TitleEn,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Status,
    bool HasRecording,
    DateTime ServerNowUtc);

/// <summary>Query of GET /api/class-courses/{id}/live-sessions. <c>Status</c> from <see cref="LiveSessionStatuses"/>.</summary>
public sealed record LiveSessionQuery(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Status = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// POST /api/live-sessions/{id}/join (LiveSessions.View). Students get the join URL inside
/// [start − <c>Live.JoinWindowMinutes</c>, end] by the server clock (else 409 <c>live_session_not_open</c>); staff with
/// LiveSessions.Manage on the ClassCourse get the start URL, other staff the join URL. <c>ExpiresAtUtc</c> = the
/// scheduled end. The URL is never stored on the client.
/// </summary>
public sealed record LiveSessionJoinDto(string Url, DateTime ExpiresAtUtc, DateTime ServerNowUtc);

/// <summary>PUT /api/live-sessions/{id}/recording (LiveSessions.Manage). http/https only, max 2048 characters; source <c>Manual</c>.</summary>
public sealed record RecordingLinkRequest(string Url);

/// <summary>
/// One row of GET /api/live-sessions/{id}/participants (LiveSessions.Manage, paged). <c>StudentId</c> and the student
/// names only for <c>Matched</c>; a guest shows only the display name Zoom reported. No email is returned.
/// </summary>
public sealed record LiveParticipantDto(
    Guid Id,
    string DisplayName,
    string MatchState,
    Guid? StudentId,
    string? StudentNameAr,
    string? StudentNameEn,
    DateTime JoinedAtUtc,
    DateTime? LeftAtUtc,
    int DurationMinutes);

/// <summary>Query of GET /api/live-sessions/{id}/participants. <c>MatchState</c> from <see cref="ParticipantMatchStates"/>.</summary>
public sealed record LiveParticipantQuery(
    string? MatchState = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// GET /api/live-sessions/provider-status (LiveSessions.Manage): whether the platform Zoom app is configured and
/// answering. Never any configuration detail (account, host pool, secrets).
/// </summary>
public sealed record ProviderStatusDto(bool Configured, bool Healthy);

/// <summary>
/// One row of GET /api/live-sessions/upcoming (LiveSessions.View): the caller's next sessions not yet ended, at most
/// <see cref="LiveSessionLimits.UpcomingMax"/>, ordered by start. Students: own enrollments, visible modules only;
/// teachers: own ClassCourses; other staff: their unit scope. Never a URL.
/// </summary>
public sealed record UpcomingLiveSessionDto(
    Guid SessionId,
    Guid ModuleId,
    Guid ClassCourseId,
    string CourseNameAr,
    string CourseNameEn,
    string TitleAr,
    string TitleEn,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Status,
    DateTime JoinOpensAtUtc,
    DateTime ServerNowUtc);
