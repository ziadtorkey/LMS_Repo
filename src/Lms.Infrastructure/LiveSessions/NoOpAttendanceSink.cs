using Lms.Domain.LiveSessions;

namespace Lms.Infrastructure.LiveSessions;

/// <summary>The registration until M5 attendance exists: the participant rows are already written, nothing else happens.</summary>
public sealed class NoOpAttendanceSink : ILiveSessionAttendanceSink
{
    public Task SessionEndedAsync(
        Guid sessionId, Guid classCourseId, DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyList<AttendanceParticipant> participants,
        CancellationToken ct = default) => Task.CompletedTask;
}
