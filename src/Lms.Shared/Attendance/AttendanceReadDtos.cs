namespace Lms.Shared.Attendance;

// M5 contract, wave 3: summaries, own and guardian reads, reports and absence alerts.

/// <summary>Counts per status over a date range. <c>AttendancePercent</c> = (Present + Late + Excused) / Total, two decimals, null when there is nothing taken.</summary>
public sealed record AttendanceSummaryDto(int Present, int Absent, int Late, int Excused, int Total, decimal? AttendancePercent);

/// <summary>Query of the own and guardian attendance reads. <c>From</c>/<c>To</c> are local dates; null = the current term.</summary>
public sealed record OwnAttendanceQuery(DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

/// <summary>
/// One attendance record of a student in the own and guardian view. <c>Excusable</c> is the server's answer (an Absent or Late record inside the policy, the caller allowed
/// to submit); <c>ExcuseId</c>/<c>ExcuseStatus</c> show an excuse already covering it. Only taken sessions appear.
/// </summary>
public sealed record OwnAttendanceItemDto(
    Guid SessionId,
    DateOnly Date,
    string CourseNameAr,
    string CourseNameEn,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string Status,
    string? Note,
    bool Excusable,
    Guid? ExcuseId,
    string? ExcuseStatus);

/// <summary>
/// GET <c>/api/me/attendance</c> and <c>/api/students/{id}/attendance</c>. <c>CanSubmitExcuse</c> is resolved by the server from <c>Excuses.Submitters</c> and the relationship (L21): a student
/// under <c>GuardiansOnly</c> gets false and the UI shows "Your guardian can submit an excuse". The read never writes.
/// </summary>
public sealed record OwnAttendanceDto(
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    DateOnly From,
    DateOnly To,
    AttendanceSummaryDto Summary,
    PagedResult<OwnAttendanceItemDto> Items,
    bool CanSubmitExcuse,
    DateTime ServerNowUtc);

/// <summary>
/// Query of <c>GET /api/attendance-reports/{kind}</c> (<see cref="AttendanceReportKinds"/>: <c>student</c> needs <c>StudentId</c>, <c>class</c> needs <c>ClassId</c>).
/// <c>From</c>/<c>To</c> are local dates; null = the current term.
/// </summary>
public sealed record AttendanceReportQuery(
    Guid? StudentId = null,
    Guid? ClassId = null,
    Guid? ClassCourseId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>One row of an attendance report: a student (class report) or a course of the student (student report).</summary>
public sealed record AttendanceReportRowDto(
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string StudentNumber,
    Guid? ClassCourseId,
    string? CourseNameAr,
    string? CourseNameEn,
    AttendanceSummaryDto Summary);

/// <summary>
/// GET <c>/api/attendance-reports/{student|class}</c> and <c>…/export</c> (<c>Attendance.View</c>; Supervisor their courses, Counselor and Manager their unit subtree; Teacher 403).
/// </summary>
public sealed record AttendanceReportDto(
    string Kind,
    DateOnly From,
    DateOnly To,
    AttendanceSummaryDto Total,
    PagedResult<AttendanceReportRowDto> Rows,
    DateTime ComputedAtUtc,
    DateTime ServerNowUtc);

/// <summary>Query of <c>GET /api/attendance/alerts</c>. <c>UnitId</c> null = every unit the caller's scope covers.</summary>
public sealed record AttendanceAlertsQuery(Guid? UnitId = null, bool ActiveOnly = true, int Page = 1, int PageSize = Paging.DefaultPageSize);

/// <summary>
/// A student who crossed the absence threshold (FR-EXC-04, D61). <c>Score</c> = <c>Absences</c> + <c>Lates</c> × <c>Attendance.LateWeightPercent</c> / 100 over the last <c>WindowDays</c>
/// (an <c>Excused</c> record never counts). The alert fires once per crossing; <c>ClearedAtUtc</c> is set when the count drops below the threshold again.
/// </summary>
public sealed record AttendanceAlertDto(
    Guid Id,
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string StudentNumber,
    Guid UnitId,
    int Absences,
    int Lates,
    int WindowDays,
    decimal Score,
    decimal Threshold,
    DateTime AlertedAtUtc,
    DateTime? ClearedAtUtc);
