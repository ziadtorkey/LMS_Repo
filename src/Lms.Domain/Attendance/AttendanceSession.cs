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
        SessionKey = source switch
        {
            TimetableSource when slotId is { } slot => $"{slot:N}:{date:yyyyMMdd}",
            AdHocSource => AdHocKey(classCourse.Id, date, periodId),
            _ => null,
        };
    }

    /// <summary>A proposal from a live session (L11): one per live session, by <see cref="LiveKey"/>. The records are the server proposal until the teacher confirms.</summary>
    public static AttendanceSession ForLive(ClassCourse classCourse, DateOnly date, Guid liveSessionId)
    {
        var session = new AttendanceSession(classCourse, date, null, null, LiveSource);
        session.LiveSessionId = liveSessionId;
        session.SessionKey = LiveKey(liveSessionId);
        return session;
    }

    /// <summary>The unique key of the proposal of a live session, so a second run (the job and the reconcile sweep) cannot create a second one.</summary>
    public static string LiveKey(Guid liveSessionId) => $"l:{liveSessionId:N}";

    /// <summary>A live proposal nobody confirmed yet: its records are the server's proposal, no guardian was told.</summary>
    public bool IsProposal => Source == LiveSource && State == OpenState;

    /// <summary>Saves edits to a proposal without taking it (L11 item 10): only the version moves; confirming takes it.</summary>
    public void RecordProposalEdit() => Version++;

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

    /// <summary>"slotId:yyyyMMdd" for a timetable session, "a:classCourse:yyyyMMdd:period" for an ad hoc one, while live; unique, so a second live session at the same place is refused by the database.</summary>
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

    /// <summary>The key of an ad hoc session: one per class course, date and period (a null period is its own token), so a double click creates one.</summary>
    public static string AdHocKey(Guid classCourseId, DateOnly date, Guid? periodId) => $"a:{classCourseId:N}:{date:yyyyMMdd}:{periodId?.ToString("N") ?? "-"}";

    public void ClearLiveKeys() => SessionKey = null;
}
