namespace Lms.Domain.LiveSessions;

/// <summary>What the provider needs to create or change one meeting. The topic is the teacher's title only (Decision 5).</summary>
public sealed record MeetingRequest(
    string Topic, DateTime StartsAtUtc, int DurationMinutes, Guid OrganizationId, Guid ClassCourseId, bool CloudRecording);

/// <summary>A created meeting. <c>HostUserKey</c> is the pool host it was created under (never exposed).</summary>
public sealed record MeetingResult(string ExternalMeetingId, string JoinUrl, string StartUrl, string? HostUserKey);

/// <summary>One attendee as the provider reports it. <c>Email</c> is set only for an authenticated participant (Decision 7).</summary>
public sealed record MeetingParticipant(string DisplayName, string? Email, DateTime JoinedAtUtc, DateTime? LeftAtUtc, int DurationSeconds);

public sealed record MeetingRecording(string Url, string Kind);

public sealed record ProviderHealth(bool Configured, bool Healthy);

/// <summary>The provider refused or could not be reached. <c>RetryAfterSeconds</c> is the hint for <c>Retry-After</c>.</summary>
public sealed class MeetingProviderException(string message, int? retryAfterSeconds = null, Exception? inner = null)
    : Exception(message, inner)
{
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// The meeting provider behind live sessions (Zoom today; the abstraction stays ready for another, D49). Calls reach an external
/// system and are never part of a database transaction: a failure surfaces as <see cref="MeetingProviderException"/> and the
/// caller keeps its row in <c>SyncFailed</c>.
/// </summary>
public interface IMeetingProvider
{
    /// <summary>The value stored in <c>LiveSession.Provider</c>.</summary>
    string Name { get; }

    Task<MeetingResult> CreateMeetingAsync(MeetingRequest request, CancellationToken ct = default);

    Task UpdateMeetingAsync(string externalMeetingId, MeetingRequest request, CancellationToken ct = default);

    Task DeleteMeetingAsync(string externalMeetingId, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingParticipant>> GetParticipantsAsync(string externalMeetingId, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingRecording>> GetRecordingsAsync(string externalMeetingId, CancellationToken ct = default);

    Task<ProviderHealth> GetHealthAsync(CancellationToken ct = default);
}
