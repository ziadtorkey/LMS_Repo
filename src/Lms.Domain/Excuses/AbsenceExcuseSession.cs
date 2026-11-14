using Lms.Domain.Common;

namespace Lms.Domain.Excuses;

/// <summary>
/// One attendance session an excuse covers, frozen at submission (only <c>Absent</c> or <c>Late</c> records are covered). Unit fields are the excuse's.
/// <see cref="AttendanceSessionId"/> is a foreign key to the attendance session.
/// </summary>
public sealed class AbsenceExcuseSession : UnitOwnedEntity
{
    private AbsenceExcuseSession()
    {
    }

    public AbsenceExcuseSession(AbsenceExcuse excuse, Guid attendanceSessionId)
    {
        PlaceLike(excuse);
        AbsenceExcuseId = excuse.Id;
        AttendanceSessionId = attendanceSessionId;
    }

    public Guid AbsenceExcuseId { get; private set; }

    public Guid AttendanceSessionId { get; private set; }
}
