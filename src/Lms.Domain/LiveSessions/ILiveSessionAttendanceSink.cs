namespace Lms.Domain.LiveSessions;

/// <summary>One participant row handed to the attendance sink (M5 turns matched students into attendance).</summary>
public sealed record AttendanceParticipant(Guid? StudentId, string MatchState, DateTime JoinedAtUtc, DateTime? LeftAtUtc, int DurationSeconds);

/// <summary>
/// The seam M5 attendance plugs into: called once a session has ended and its participants were imported. M4b writes the
/// participant rows and registers a no-op implementation; it owns no attendance table.
/// </summary>
public interface ILiveSessionAttendanceSink
{
    Task SessionEndedAsync(
        Guid sessionId, Guid classCourseId, DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyList<AttendanceParticipant> participants,
        CancellationToken ct = default);
}
