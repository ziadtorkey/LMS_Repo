namespace Lms.Shared;

// M5 wave 2: periods, timetable, exceptions and timetable views (M5 plan Task 2; the "Route, permission and relationship table" is the oracle,
// Task 40 tests every row). A relationship miss is 404, a missing permission 403. Rooms are free text (D60); every date is a local date of the
// client time zone (D64).
public static partial class Routes
{
    /// <summary>
    /// GET (Timetable.View; the periods visible at the unit incl. its ancestors', read only) and POST (<c>SavePeriodRequest</c> → <c>PeriodDto</c>,
    /// <c>Timetable.Configure</c>, <c>UnitAccess</c>); teacher POST 403; an overlap with another period of the unit is 400 <c>period_overlap</c>.
    /// </summary>
    public const string OrgUnitPeriodsPattern = OrgUnitPattern + "/periods";

    /// <summary>GET (Timetable.View) → <c>PeriodDto</c>; PUT (<c>SavePeriodRequest</c>) and DELETE — Timetable.Configure, <c>UnitAccess</c>; an inherited period is read only (404 at the descendant); a period a slot uses cannot be deleted (409 <c>period_in_use</c>).</summary>
    public const string PeriodPattern = OrgUnitPeriodsPattern + "/{pid:guid}";

    /// <summary>GET <c>?termId=</c> → <c>ClassTimetableDto</c> — Timetable.View (drafts only with Timetable.Manage); <c>UnitAccess</c>; a supervisor only for supervised courses; other school 404.</summary>
    public const string ClassTimetablePattern = ClassPattern + "/timetable";

    /// <summary>POST (<c>SaveSlotRequest</c> → <c>SaveSlotResultDto</c>) — Timetable.Manage, <c>UnitAccess</c>; a clash in <c>Block</c> mode is 409 <c>timetable_conflict</c>; teacher 403.</summary>
    public const string ClassTimetableSlotsPattern = ClassTimetablePattern + "/slots";

    /// <summary>PUT (<c>SaveSlotRequest</c> with <c>Version</c> → <c>SaveSlotResultDto</c>) and DELETE — Timetable.Manage, <c>UnitAccess</c>; stale <c>Version</c> is 409 <c>concurrency_conflict</c>; teacher 403.</summary>
    public const string ClassTimetableSlotPattern = ClassTimetableSlotsPattern + "/{sid:guid}";

    /// <summary>GET <c>?termId=</c> → <c>TimetableValidationDto</c> — Timetable.Manage, <c>UnitAccess</c>.</summary>
    public const string ClassTimetableValidatePattern = ClassTimetablePattern + "/validate";

    /// <summary>POST (<c>PublishTimetableRequest</c> → <c>PublishTimetableResultDto</c>) — Timetable.Manage, <c>UnitAccess</c>; publishes the term's draft slots and queues attendance-session generation; with no draft slots it answers 0 published.</summary>
    public const string ClassTimetablePublishPattern = ClassTimetablePattern + "/publish";

    /// <summary>POST (<c>CopyTermRequest</c> → <c>CopyTermResultDto</c>) — Timetable.Manage, <c>UnitAccess</c>; copies as drafts and returns the skipped slots.</summary>
    public const string ClassTimetableCopyTermPattern = ClassTimetablePattern + "/copy-term";

    /// <summary>GET <c>?termId=</c> → an Excel file (.xlsx) of the grid — Timetable.View, <c>UnitAccess</c>; drafts only with Timetable.Manage; the export is audited.</summary>
    public const string ClassTimetableExportPattern = ClassTimetablePattern + "/export";

    /// <summary>GET (Timetable.View) → exceptions of the slot; POST (<c>SaveTimetableExceptionRequest</c> → <c>TimetableExceptionDto</c>) — Timetable.Manage, <c>UnitAccess</c>; a date off the slot's weekday or outside the term is 400 <c>exception_date_invalid</c>; an exception on a draft slot is 409 <c>timetable_not_published</c>; teacher POST 403.</summary>
    public const string TimetableSlotExceptionsPattern = "/api/timetable-slots/{id:guid}/exceptions";

    /// <summary>PUT (<c>SaveTimetableExceptionRequest</c>) and DELETE — Timetable.Manage, <c>UnitAccess</c>; a substitute is the teacher of record for that one session only (L15).</summary>
    public const string TimetableSlotExceptionPattern = TimetableSlotExceptionsPattern + "/{eid:guid}";

    /// <summary>GET <c>?from=&amp;to=</c> → <c>TimetableViewDto</c> — Timetable.ViewOwn: a teacher's own slots across classes (published only; substitutions of the range included) or a student's own class; at most 31 days.</summary>
    public const string MeTimetable = "/api/me/timetable";

    /// <summary>GET <c>?from=&amp;to=</c> → <c>TimetableViewDto</c> — Timetable.ViewOwn: a guardian's child through an active <c>StudentGuardian</c> link (a miss is 404, never the other child's data); published only.</summary>
    public const string StudentTimetablePattern = "/api/students/{id:guid}/timetable";

    public static string OrgUnitPeriods(Guid unitId) => $"/api/org-units/{unitId}/periods";

    public static string Period(Guid unitId, Guid periodId) => $"/api/org-units/{unitId}/periods/{periodId}";

    public static string ClassTimetable(Guid classId, Guid? termId = null) =>
        termId is { } t ? $"/api/classes/{classId}/timetable?termId={t}" : $"/api/classes/{classId}/timetable";

    public static string ClassTimetableSlots(Guid classId) => $"/api/classes/{classId}/timetable/slots";

    public static string ClassTimetableSlot(Guid classId, Guid slotId) => $"/api/classes/{classId}/timetable/slots/{slotId}";

    public static string ClassTimetableValidate(Guid classId, Guid termId) => $"/api/classes/{classId}/timetable/validate?termId={termId}";

    public static string ClassTimetablePublish(Guid classId) => $"/api/classes/{classId}/timetable/publish";

    public static string ClassTimetableCopyTerm(Guid classId) => $"/api/classes/{classId}/timetable/copy-term";

    public static string ClassTimetableExport(Guid classId, Guid termId) => $"/api/classes/{classId}/timetable/export?termId={termId}";

    public static string TimetableSlotExceptions(Guid slotId) => $"/api/timetable-slots/{slotId}/exceptions";

    public static string TimetableSlotException(Guid slotId, Guid exceptionId) => $"/api/timetable-slots/{slotId}/exceptions/{exceptionId}";

    public static string MyTimetable(DateOnly from, DateOnly to) => $"/api/me/timetable?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

    public static string StudentTimetable(Guid studentId, DateOnly from, DateOnly to) =>
        $"/api/students/{studentId}/timetable?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
}
