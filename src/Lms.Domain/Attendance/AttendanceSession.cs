using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Attendance;

/// <summary>
/// One attendance session of a ClassCourse on a local date (FR-ATT-01, M5 Task 16): generated from a published timetable slot, created ad hoc by a teacher,
/// or proposed from a live session. Unit fields are the class course's. A timetable session is unique per (slot, date) through <see cref="SessionKey"/>,
/// so the generator is idempotent. <c>Version</c> is the row version (L12). The roster is not stored: it is the enrollments active on the date.
/// </summary>
public sealed class AttendanceSession : UnitOwnedEntity, ILiveKeyed
{
    // The same values as Lms.Shared.Attendance (the domain does not reference the contract project; a unit test pins them).
    public const string TimetableSource = "Timetable";
    public const string AdHocSource = "AdHoc";
    public const string LiveSource = "Live";
    public const string OpenState = "Open";
    public const string TakenState = "Taken";

    private AttendanceSession()
    {
        Source = State = string.Empty;
    }

    public AttendanceSession(ClassCourse classCourse, DateOnly date, Guid? slotId, Guid? periodId, string source)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        ClassId = classCourse.ClassId;
        Date = date;
        SlotId = slotId;
        PeriodId = periodId;
        Source = source;
        State = OpenState;
        Version = 1;
        SessionKey = slotId is { } slot && source == TimetableSource ? $"{slot:N}:{date:yyyyMMdd}" : null;
    }

    public Guid ClassCourseId { get; private set; }

    public Guid ClassId { get; private set; }

    /// <summary>The client-local school date.</summary>
    public DateOnly Date { get; private set; }

    public Guid? SlotId { get; private set; }

    public Guid? PeriodId { get; private set; }

    /// <summary>An <c>AttendanceSources</c> value.</summary>
    public string Source { get; private set; }

    /// <summary>An <c>AttendanceSessionStates</c> value.</summary>
    public string State { get; private set; }

    public Guid? TakenByUserId { get; private set; }

    public DateTime? TakenAtUtc { get; private set; }

    /// <summary>The live session a <c>Live</c> proposal comes from (no foreign key: the live-session table belongs to M4b).</summary>
    public Guid? LiveSessionId { get; private set; }

    public int Version { get; private set; }

    /// <summary>"slotId:yyyyMMdd" while live and generated from a slot; unique, so a second live session for the same slot and date is refused by the database.</summary>
    public string? SessionKey { get; private set; }

    /// <summary>Marks the session Taken the first time attendance is saved; later saves only advance the version.</summary>
    public void RecordSave(Guid userId, DateTime atUtc)
    {
        if (State != TakenState)
        {
            State = TakenState;
            TakenByUserId = userId;
            TakenAtUtc = atUtc;
        }

        Version++;
    }

    public void ClearLiveKeys() => SessionKey = null;
}
