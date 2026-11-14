using Lms.Domain.Common;

namespace Lms.Domain.Attendance;

/// <summary>
/// The teacher's manual mapping of one live participant to a student of the roster (L11 item 8). It lives on the attendance side and never in
/// <c>LiveSessionParticipant</c> (the live module owns that row). A null student is "skip": the participant is not counted for anyone. Unit fields are the
/// attendance session's. One row per (attendance session, participant).
/// </summary>
public sealed class LiveAttendanceMapping : UnitOwnedEntity
{
    private LiveAttendanceMapping()
    {
    }

    public LiveAttendanceMapping(AttendanceSession session, Guid participantId, Guid? studentProfileId)
    {
        PlaceLike(session);
        AttendanceSessionId = session.Id;
        ParticipantId = participantId;
        StudentProfileId = studentProfileId;
    }

    public Guid AttendanceSessionId { get; private set; }

    public Guid ParticipantId { get; private set; }

    public Guid? StudentProfileId { get; private set; }

    public void MapTo(Guid? studentProfileId) => StudentProfileId = studentProfileId;
}
