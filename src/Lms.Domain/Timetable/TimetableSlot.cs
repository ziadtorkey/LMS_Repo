using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Timetable;

/// <summary>
/// One slot of a class's weekly timetable (FR-TT-02): a ClassCourse taught by a teacher on a weekday in a period of a term. Unit fields are copied from
/// the class. The room is free text (D60, never checked). A slot is a <c>Draft</c> (staff with Timetable.Manage only) until published. <c>Version</c> is the
/// row version for optimistic concurrency (L12). At most one live slot per (class course, term, day, period).
/// </summary>
[Audited]
public sealed class TimetableSlot : UnitOwnedEntity, ILiveKeyed
{
    public const string DraftState = "Draft";
    public const string PublishedState = "Published";

    private TimetableSlot()
    {
        State = DraftState;
    }

    public TimetableSlot(Class @class, Guid classCourseId, Guid termId, Guid teacherUserId, DayOfWeek day, Guid periodId, string? roomName)
    {
        PlaceLike(@class);
        ClassId = @class.Id;
        ClassCourseId = classCourseId;
        TermId = termId;
        TeacherUserId = teacherUserId;
        Day = day;
        PeriodId = periodId;
        RoomName = roomName;
        State = DraftState;
        Version = 1;
        SlotKey = BuildKey(classCourseId, termId, day, periodId);
    }

    public Guid ClassId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid TermId { get; private set; }
    public Guid TeacherUserId { get; private set; }
    public DayOfWeek Day { get; private set; }
    public Guid PeriodId { get; private set; }
    public string? RoomName { get; private set; }
    public string State { get; private set; }
    public int Version { get; private set; }

    /// <summary>"classCourse:term:day:period" while live; unique, so a second live slot at the same place is refused by the database.</summary>
    public string? SlotKey { get; private set; }

    public bool IsPublished => State == PublishedState;

    public void Move(Guid classCourseId, Guid teacherUserId, DayOfWeek day, Guid periodId, string? roomName)
    {
        ClassCourseId = classCourseId;
        TeacherUserId = teacherUserId;
        Day = day;
        PeriodId = periodId;
        RoomName = roomName;
        SlotKey = BuildKey(classCourseId, TermId, day, periodId);
        State = DraftState; // an edit of a published slot returns it to Draft until the week is published again
        Version++;
    }

    public void Publish()
    {
        State = PublishedState;
        Version++;
    }

    public void ClearLiveKeys() => SlotKey = null;

    private static string BuildKey(Guid classCourseId, Guid termId, DayOfWeek day, Guid periodId) =>
        $"{classCourseId:N}:{termId:N}:{(int)day}:{periodId:N}";
}
