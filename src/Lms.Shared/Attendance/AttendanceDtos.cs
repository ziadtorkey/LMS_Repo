namespace Lms.Shared.Attendance;

// M5 contract, wave 3: taking attendance. Dates are the client's local dates (IClientCalendar, L2); every timed answer carries ServerNowUtc.

/// <summary>Query of <c>GET /api/attendance/sessions</c>. <c>Date</c> null = the client's local today.</summary>
public sealed record AttendanceSessionsQuery(DateOnly? Date = null);

/// <summary>
/// One attendance session in a teacher's day (or a Manager's/Counselor's view of a class). <c>EditableUntilUtc</c> is the end of the edit window
/// (<c>TakenAtUtc</c> + <c>Attendance.EditWindowHours</c>), null while Open. <c>IsSubstitute</c> is the server's answer from <c>IsSubstituteOn</c> (the client never infers it,
/// L15). <c>IsProposal</c> marks a live session proposal waiting for the teacher's confirmation. <c>Version</c> is the session row version (L12).
/// </summary>
public sealed record AttendanceSessionListItemDto(
    Guid Id,
    Guid ClassCourseId,
    string CourseNameAr,
    string CourseNameEn,
    string ClassNameAr,
    string ClassNameEn,
    DateOnly Date,
    Guid? PeriodId,
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    string Source,
    string State,
    int MarkedCount,
    int RosterCount,
    DateTime? EditableUntilUtc,
    bool IsSubstitute,
    bool IsProposal,
    int Version,
    DateTime ServerNowUtc);

/// <summary>
/// Query of <c>GET /api/org-units/{id}/attendance-sessions</c> (Attendance.View): sessions of the unit's subtree between two local dates (at most
/// <see cref="AttendanceLimits.SearchMaxDays"/> days), optionally of one class, one class course or one state (<see cref="AttendanceSessionStates"/>).
/// </summary>
public sealed record AttendanceSessionSearchQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClassId = null,
    Guid? ClassCourseId = null,
    string? State = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>Body of <c>POST /api/class-courses/{id}/attendance-sessions</c>: an ad hoc session for a ClassCourse on a (past or today's) local date; a future date is 409 <c>attendance_session_future</c>.</summary>
public sealed record CreateAdHocAttendanceSessionRequest(DateOnly Date, Guid? PeriodId = null);

/// <summary>
/// One student's row. <c>Status</c> is null while unmarked (only possible in an Open session). The live fields are set only for <c>Source = Live</c>: how many
/// merged minutes the student was in the room, when they first joined, and whether the status is still the server's proposal (<c>Proposed</c>) rather than a person's choice.
/// </summary>
public sealed record AttendanceRecordDto(
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string StudentNumber,
    string? Status,
    string? Note,
    int? JoinedMinutes,
    DateTime? JoinedAtUtc,
    bool Proposed);

/// <summary>
/// A live participant the server could not match to the roster (a guest, a nickname, a shared account); the teacher maps them by hand (L11). Only display names the
/// provider reported are shown, to staff with <c>Attendance.Take</c> on this session.
/// </summary>
public sealed record UnmatchedParticipantDto(
    Guid ParticipantId,
    string DisplayName,
    int JoinedMinutes,
    DateTime FirstJoinedAtUtc,
    Guid? MappedStudentId);

/// <summary>
/// GET and PUT answer: the session, its roster and (Live) the unmatched participants. <c>ParticipantsAvailable</c> is false until the live participants were imported
/// (<c>LiveSession.ParticipantsImportedAtUtc</c>, L11 item 5); then nobody is proposed Absent and the page says "participants not available yet".
/// </summary>
public sealed record AttendanceRecordsDto(
    AttendanceSessionListItemDto Session,
    IReadOnlyList<AttendanceRecordDto> Records,
    IReadOnlyList<UnmatchedParticipantDto> Unmatched,
    bool ParticipantsAvailable);

/// <summary>One student's mark in a take or edit call.</summary>
public sealed record TakeAttendanceRecord(Guid StudentId, string Status, string? Note = null);

/// <summary>
/// Body of <c>PUT /api/attendance-sessions/{id}/records</c>. <c>DefaultStatus</c> (the "mark all present" shortcut) applies to every roster student not listed in
/// <c>Records</c>. A stale <c>Version</c> is 409 <c>concurrency_conflict</c>. <c>Reason</c> is required for an edit after the window (L14).
/// </summary>
public sealed record TakeAttendanceRequest(
    string? DefaultStatus,
    IReadOnlyList<TakeAttendanceRecord> Records,
    string? Reason,
    int Version);

/// <summary>Body of <c>POST /api/attendance-sessions/{id}/confirm</c>: the teacher accepts the live proposal as it stands (after any edits saved by PUT).</summary>
public sealed record ConfirmAttendanceRequest(int Version);

/// <summary>One participant mapping; <c>StudentId</c> null clears it ("skip").</summary>
public sealed record LiveMappingEntry(Guid ParticipantId, Guid? StudentId);

/// <summary>
/// Body of <c>PUT /api/attendance-sessions/{id}/live-mappings</c>: the manual mappings of unmatched participants (kept in the attendance-owned
/// <c>LiveAttendanceMapping</c>, never in <c>LiveSessionParticipant</c>, L11). The answer is the re-proposed <c>AttendanceRecordsDto</c>.
/// </summary>
public sealed record LiveMappingRequest(IReadOnlyList<LiveMappingEntry> Mappings, int Version);

/// <summary>One row of the edit history of a session (<c>AttendanceRecordChange</c>, append only).</summary>
public sealed record AttendanceChangeDto(
    Guid Id,
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string Kind,
    string? FromStatus,
    string ToStatus,
    string? Reason,
    Guid ChangedByUserId,
    string ChangedByNameAr,
    string ChangedByNameEn,
    DateTime ChangedAtUtc);
