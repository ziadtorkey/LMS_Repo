namespace Lms.Shared.Timetable;

/// <summary>
/// One slot of a class's weekly timetable (FR-TT-02): a ClassCourse taught by a teacher on a weekday in a period of a term. <c>RoomName</c> is
/// free text (D60). <c>Version</c> is the row version (a stale save is 409 <c>concurrency_conflict</c>, L12). <c>Badges</c> are <c>SlotBadges</c> values.
/// </summary>
public sealed record SlotDto(
    Guid Id,
    Guid ClassId,
    Guid ClassCourseId,
    Guid TermId,
    string CourseNameAr,
    string CourseNameEn,
    Guid TeacherUserId,
    string TeacherName,
    DayOfWeek DayOfWeek,
    Guid PeriodId,
    string? RoomName,
    string State,
    int Version,
    IReadOnlyList<string> Badges);

/// <summary>Body of POST (create a slot) and PUT (edit or move it) of a class timetable. <c>Version</c> is required on PUT and ignored on POST.</summary>
public sealed record SaveSlotRequest(
    Guid ClassCourseId,
    Guid TermId,
    Guid TeacherUserId,
    DayOfWeek DayOfWeek,
    Guid PeriodId,
    string? RoomName,
    int? Version);

/// <summary>
/// A clash of a saved or validated slot with another slot of the term. Clashes are <b>computed across the whole client</b> (a teacher can teach in two schools, so
/// being busy there is a clash), but the <b>details are disclosed only for slots inside the caller's unit subtree</b> (<c>Timetable.Manage</c> is unit-scoped): for a
/// slot outside it the entry carries <c>Kind</c> only and <c>SlotId</c>, <c>CourseNameAr</c>, <c>CourseNameEn</c> and <c>TeacherName</c> are null, so an admin of one
/// school never reads another school's course, class or timetable through a warning, the 409 <c>timetable_conflict</c> body or <c>GET .../validate</c>.
/// </summary>
public sealed record SlotConflictDto(
    string Kind,
    Guid? SlotId,
    string? CourseNameAr,
    string? CourseNameEn,
    string? TeacherName);

/// <summary>The answer of a slot save: the slot and, in <c>Warn</c> mode, the clashes it was saved with (empty in <c>Block</c> mode, where a clash is 409).</summary>
public sealed record SaveSlotResultDto(SlotDto Slot, IReadOnlyList<SlotConflictDto> Warnings);

/// <summary>The weekly grid of one class and term for staff (drafts included for <c>Timetable.Manage</c>).</summary>
public sealed record ClassTimetableDto(
    Guid ClassId,
    Guid TermId,
    IReadOnlyList<string> WorkingDays,
    IReadOnlyList<PeriodDto> Periods,
    IReadOnlyList<SlotDto> Slots,
    string ConflictMode,
    int DraftCount,
    DateTime ServerNowUtc);

/// <summary>GET <c>.../validate</c>: every clash among the term's slots of the class (also those saved in <c>Warn</c> mode).</summary>
public sealed record TimetableValidationDto(IReadOnlyList<SlotConflictDto> Conflicts);

/// <summary>POST <c>.../publish</c> for a term of the class: publishes every draft slot and queues attendance-session generation.</summary>
public sealed record PublishTimetableRequest(Guid TermId);

public sealed record PublishTimetableResultDto(int Published, bool GenerationQueued);

/// <summary>POST <c>.../copy-term</c>: copies the slots of the source term as drafts into the target term.</summary>
public sealed record CopyTermRequest(Guid SourceTermId, Guid TargetTermId);

/// <summary>A slot that was not copied and why (<c>CopySkipReasons</c>).</summary>
public sealed record CopySkippedDto(Guid SourceSlotId, string Reason, string CourseNameAr, string CourseNameEn);

public sealed record CopyTermResultDto(int Copied, IReadOnlyList<CopySkippedDto> Skipped);

/// <summary>
/// A dated exception of a slot (FR-TT-05, L15). <c>Date</c> is a local school date inside the term and must fall on the slot's weekday
/// (<c>exception_date_invalid</c> otherwise). <c>SubstituteUserId</c> is required for <c>Substitute</c>, <c>NewPeriodId</c> for <c>Moved</c>.
/// </summary>
public sealed record TimetableExceptionDto(
    Guid Id,
    Guid SlotId,
    DateOnly Date,
    string Kind,
    Guid? SubstituteUserId,
    string? SubstituteName,
    Guid? NewPeriodId,
    string? Note);

public sealed record SaveTimetableExceptionRequest(
    DateOnly Date,
    string Kind,
    Guid? SubstituteUserId,
    Guid? NewPeriodId,
    string? Note);

/// <summary>
/// One lesson of a view on one date, with its exception applied (a cancelled lesson is kept so the view can strike it through). <c>LiveSessionId</c> and
/// <c>JoinOpensAtUtc</c> are null unless a live session is scheduled for this lesson; the server fills them from the live sessions of the class course on the date,
/// so the card can show Join (the join window opens at <c>JoinOpensAtUtc</c>; joining itself goes through the live-session routes and their own checks).
/// </summary>
public sealed record TimetableCardDto(
    Guid SlotId,
    DateOnly Date,
    Guid PeriodId,
    string CourseNameAr,
    string CourseNameEn,
    string TeacherName,
    string? RoomName,
    string? ExceptionKind,
    string? SubstituteName,
    string ClassNameAr,
    string ClassNameEn,
    Guid? LiveSessionId = null,
    DateTime? JoinOpensAtUtc = null);

/// <summary>
/// The answer of every timetable view (class, teacher, student, guardian): published slots only for a student or guardian, a range of at most 31 days,
/// local dates of the client time zone (D64). Teachers also see their drafts of the range only through <c>Timetable.Manage</c> views, never here.
/// </summary>
public sealed record TimetableViewDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> Days,
    IReadOnlyList<PeriodDto> Periods,
    IReadOnlyList<TimetableCardDto> Cards,
    DateTime ServerNowUtc);
