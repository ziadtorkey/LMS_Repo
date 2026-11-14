namespace Lms.Shared;

// M5 wave 3: attendance (M5 plan Task 3; the "Route, permission and relationship table" is the oracle, Task 40 tests every row). A relationship miss is 404, a missing
// permission 403. Teacher: own ClassCourses with an assignment active today (L15), or the substitute of one session (IsSubstituteOn, only that date); Supervisor: supervised
// courses; Counselor and Manager: unit subtree; Student: self; Guardian: an active child link.
public static partial class Routes
{
    /// <summary>
    /// GET <c>?date=</c> (<c>AttendanceSessionsQuery</c> → <c>AttendanceSessionListItemDto[]</c>) — Attendance.Take; the caller's own sessions of the local day, substitute sessions
    /// included; no session of another teacher; counselor and student 403.
    /// </summary>
    public const string AttendanceSessionsPattern = "/api/attendance/sessions";

    /// <summary>POST (<c>CreateAdHocAttendanceSessionRequest</c> → <c>AttendanceSessionListItemDto</c>) — Attendance.Take; Rel; PlacementGuard; a future date is 409 <c>attendance_session_future</c>; another teacher 404, counselor 403.</summary>
    public const string ClassCourseAttendanceSessionsPattern = ClassCoursePattern + "/attendance-sessions";

    /// <summary>
    /// GET → <c>AttendanceRecordsDto</c> (Attendance.Take, Attendance.Edit or Attendance.View; Rel, IsSubstituteOn or subtree); PUT (<c>TakeAttendanceRequest</c> → <c>AttendanceRecordsDto</c>) —
    /// Attendance.Take inside the edit window, Attendance.Edit after it (a reason is required: 422 <c>attendance_reason_required</c>); a future session 409 <c>attendance_session_future</c>, a
    /// Taken session with unmarked rows 422 <c>attendance_unmarked_rows</c>, after the window for a teacher 409 <c>attendance_edit_window_closed</c>, a stale <c>Version</c> 409
    /// <c>concurrency_conflict</c>; PlacementGuard; a substitute for another session 404.
    /// </summary>
    public const string AttendanceSessionRecordsPattern = "/api/attendance-sessions/{id:guid}/records";

    /// <summary>GET (<c>AttendanceChangeDto[]</c>) — Attendance.Take, Attendance.Edit or Attendance.View; Rel or subtree; student and guardian 403.</summary>
    public const string AttendanceSessionHistoryPattern = "/api/attendance-sessions/{id:guid}/history";

    /// <summary>POST (<c>ConfirmAttendanceRequest</c> → <c>AttendanceRecordsDto</c>) — Attendance.Take; Rel or IsSubstituteOn; only for <c>Source = Live</c> (otherwise 409 <c>attendance_not_a_proposal</c>); guardian notices fire now, never on the proposal (L11).</summary>
    public const string AttendanceSessionConfirmPattern = "/api/attendance-sessions/{id:guid}/confirm";

    /// <summary>PUT (<c>LiveMappingRequest</c> → <c>AttendanceRecordsDto</c>) — Attendance.Take; Rel; <c>Source = Live</c> only; another teacher 404.</summary>
    public const string AttendanceSessionLiveMappingsPattern = "/api/attendance-sessions/{id:guid}/live-mappings";

    /// <summary>GET <c>{kind}</c> from <c>AttendanceReportKinds</c> (<c>AttendanceReportQuery</c> → <c>AttendanceReportDto</c>) — Attendance.View; Supervisor their courses, Counselor and Manager subtree; teacher 403.</summary>
    public const string AttendanceReportPattern = "/api/attendance-reports/{kind}";

    /// <summary>GET <c>{kind}</c> (<c>AttendanceReportQuery</c>) → the report as <c>.xlsx</c> (cells escaped against formula injection, L9) — Attendance.View; audited.</summary>
    public const string AttendanceReportExportPattern = AttendanceReportPattern + "/export";

    /// <summary>GET (<c>OwnAttendanceQuery</c> → <c>OwnAttendanceDto</c>) — Attendance.ViewOwn; the caller is a student; <c>CanSubmitExcuse</c> from the server; guardian and staff 403.</summary>
    public const string MeAttendance = "/api/me/attendance";

    /// <summary>GET (<c>OwnAttendanceQuery</c> → <c>OwnAttendanceDto</c>) — Attendance.ViewOwn; an active guardian link (another guardian's child is 404, one child never reads under another's id).</summary>
    public const string StudentAttendancePattern = StudentPattern + "/attendance";

    /// <summary>GET (<c>AttendanceAlertsQuery</c> → <c>PagedResult&lt;AttendanceAlertDto&gt;</c>) — Excuses.Review or Attendance.View (Counselor, Manager); unit subtree; teacher 403.</summary>
    public const string AttendanceAlertsPattern = "/api/attendance/alerts";

    /// <summary>
    /// GET (<c>AttendanceSessionSearchQuery</c> → <c>PagedResult&lt;AttendanceSessionListItemDto&gt;</c>) — Attendance.View; <c>UnitAccess</c> at the unit and its subtree (Counselor, Manager) or the supervised courses
    /// (Supervisor); finds taken and open sessions of a date range, closed ones included, so they can be corrected with Attendance.Edit; a range over 92 days is 400; another school 404, teacher and student 403.
    /// </summary>
    public const string OrgUnitAttendanceSessionsPattern = OrgUnitPattern + "/attendance-sessions";

    public static string OrgUnitAttendanceSessions(Guid unitId, DateOnly from, DateOnly to, Guid? classId = null, Guid? classCourseId = null) =>
        $"{OrgUnit(unitId)}/attendance-sessions?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"
        + (classId is { } c ? $"&classId={c}" : string.Empty) + (classCourseId is { } cc ? $"&classCourseId={cc}" : string.Empty);

    public static string AttendanceSessions(DateOnly? date = null) =>
        date is { } d ? $"{AttendanceSessionsPattern}?date={d:yyyy-MM-dd}" : AttendanceSessionsPattern;

    public static string ClassCourseAttendanceSessions(Guid classCourseId) => $"{ClassCourse(classCourseId)}/attendance-sessions";
    public static string AttendanceSessionRecords(Guid sessionId) => $"/api/attendance-sessions/{sessionId}/records";
    public static string AttendanceSessionHistory(Guid sessionId) => $"/api/attendance-sessions/{sessionId}/history";
    public static string AttendanceSessionConfirm(Guid sessionId) => $"/api/attendance-sessions/{sessionId}/confirm";
    public static string AttendanceSessionLiveMappings(Guid sessionId) => $"/api/attendance-sessions/{sessionId}/live-mappings";
    public static string AttendanceReport(string kind) => $"/api/attendance-reports/{kind}";
    public static string AttendanceReportExport(string kind) => $"{AttendanceReport(kind)}/export";

    public static string MyAttendance(DateOnly? from = null, DateOnly? to = null) => WithRange(MeAttendance, from, to);
    public static string StudentAttendance(Guid studentId, DateOnly? from = null, DateOnly? to = null) => WithRange($"{Student(studentId)}/attendance", from, to);

    private static string WithRange(string path, DateOnly? from, DateOnly? to)
    {
        var query = string.Join('&', new[] { from is { } f ? $"from={f:yyyy-MM-dd}" : null, to is { } t ? $"to={t:yyyy-MM-dd}" : null }.Where(q => q is not null));
        return query.Length == 0 ? path : $"{path}?{query}";
    }
}
