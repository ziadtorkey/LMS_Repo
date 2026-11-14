using Lms.Domain.Common;

namespace Lms.Domain.Attendance;

/// <summary>
/// One student attendance entry in one session (M5 Task 16 creates the table; Task 17 writes the rows). <c>Status</c> is null while unmarked, which only an
/// Open session may have. Unit fields are the session ones. At most one live record per (session, student).
/// </summary>
public sealed class AttendanceRecord : UnitOwnedEntity, ILiveKeyed
{
    private AttendanceRecord()
    {
    }

    public AttendanceRecord(AttendanceSession session, Guid studentProfileId, string? status, string? note)
    {
        PlaceLike(session);
        AttendanceSessionId = session.Id;
        StudentProfileId = studentProfileId;
        Status = status;
        Note = note;
        RecordKey = $"{session.Id:N}:{studentProfileId:N}";
    }

    public Guid AttendanceSessionId { get; private set; }

    public Guid StudentProfileId { get; private set; }

    /// <summary>An <c>AttendanceStatuses</c> value, or null while unmarked.</summary>
    public string? Status { get; private set; }

    public string? Note { get; private set; }

    /// <summary>"sessionId:studentId" while live; unique per session and student.</summary>
    public string? RecordKey { get; private set; }

    public void ClearLiveKeys() => RecordKey = null;
}
