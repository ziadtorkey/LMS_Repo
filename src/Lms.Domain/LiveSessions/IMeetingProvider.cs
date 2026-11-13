namespace Lms.Domain.LiveSessions;

/// <summary>
/// What the provider needs to create or change one meeting. <c>Topic</c> is the teacher's session title and nothing else
/// (Decision 5). <c>TimeZoneId</c> is the IANA zone of the series (or <c>UTC</c> for a single session).
/// <para>
/// <c>HostUserKey</c> is the pool host an existing meeting was created under (null on create). <c>ReserveHostAsync</c> lets a
/// provider that allocates hosts from a shared pool (Zoom, D53) record its choice while its allocation lock is held: the
/// provider calls it with the chosen host before the lock is released, and the caller commits the slot (the host on the session
/// row) before it returns, so two callers never book one host twice. A provider with no pool never calls it.
/// </para>
/// </summary>
public sealed record MeetingRequest(
    Guid SessionId,
    string Topic,
    DateTime StartsAtUtc,
    int DurationMinutes,
    string TimeZoneId,
    Guid OrganizationId,
    Guid ClassCourseId,
    bool CloudRecording,
    string? HostUserKey = null,
    Func<string, Task>? ReserveHostAsync = null);

/// <summary>A created meeting. <c>HostUserKey</c> is the pool host it was created under (never exposed); the start URL grants host rights.</summary>
public sealed record MeetingResult(string ExternalMeetingId, string JoinUrl, string StartUrl, string? HostUserKey);

/// <summary>One attendee as the provider reports it. <c>Email</c> is set only for an authenticated participant (Decision 7).</summary>
public sealed record MeetingParticipant(string DisplayName, string? Email, DateTime JoinedAtUtc, DateTime? LeftAtUtc, int DurationSeconds);

/// <summary>A recording link the provider holds for a meeting (<c>Kind</c> is the provider's own label, e.g. <c>shared_screen_with_speaker_view</c>).</summary>
public sealed record MeetingRecording(string Url, string Kind);

public sealed record ProviderHealth(bool Configured, bool Healthy);

/// <summary>How a provider call failed (what the caller does about it).</summary>
public enum MeetingFailure
{
    /// <summary>The provider could not be reached or refused: the caller keeps its row in <c>SyncFailed</c> and a sweep retries.</summary>
    Failed,

    /// <summary>No host is free, the client's share is used up or the provider is rate limiting: the caller answers 409 <c>provider_unavailable</c> with <c>Retry-After</c>.</summary>
    CapacityExhausted,

    /// <summary>The provider is not configured: the caller answers 409 <c>provider_not_configured</c>.</summary>
    NotConfigured,
}

/// <summary>The provider refused or could not be reached. Never carries a response body, a credential or a URL.</summary>
public sealed class MeetingProviderException(
    MeetingFailure failure, string message, int? retryAfterSeconds = null, Exception? inner = null) : Exception(message, inner)
{
    public MeetingFailure Failure { get; } = failure;

    /// <summary>The hint for <c>Retry-After</c>, in seconds.</summary>
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// The meeting provider behind live sessions (Zoom today; the abstraction stays ready for another, D49). Calls reach an external
/// system and are never part of a database transaction: a failure surfaces as <see cref="MeetingProviderException"/>.
/// </summary>
public interface IMeetingProvider
{
    /// <summary>The value stored in <c>LiveSession.Provider</c> (<c>Zoom</c>, or <c>Fake</c> in tests).</summary>
    string Name { get; }

    Task<MeetingResult> CreateMeetingAsync(MeetingRequest request, CancellationToken ct = default);

    Task UpdateMeetingAsync(string externalMeetingId, MeetingRequest request, CancellationToken ct = default);

    /// <summary>Deletes a meeting; one that is already gone counts as deleted.</summary>
    Task DeleteMeetingAsync(string externalMeetingId, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingParticipant>> GetParticipantsAsync(string externalMeetingId, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingRecording>> GetRecordingsAsync(string externalMeetingId, CancellationToken ct = default);

    Task<ProviderHealth> GetHealthAsync(CancellationToken ct = default);
}
