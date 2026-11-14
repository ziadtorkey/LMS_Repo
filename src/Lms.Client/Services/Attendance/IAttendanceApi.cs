using Lms.Shared;
using Lms.Shared.Attendance;

namespace Lms.Client.Services.Attendance;

/// <summary>Attendance sessions of a teacher: the day's list, taking and editing, history and the live-session proposal (M5 wave 3, FR-ATT-01).</summary>
public interface IAttendanceApi
{
    /// <summary>The caller's own sessions of a local day (today when null), substitute sessions included.</summary>
    Task<IReadOnlyList<AttendanceSessionListItemDto>> GetSessionsAsync(DateOnly? date = null, CancellationToken ct = default);

    /// <summary>Sessions of a unit (and its subtree) in a date range, closed ones included, for Attendance.View holders (counselor, manager, supervisor).</summary>
    Task<PagedResult<AttendanceSessionListItemDto>> SearchSessionsAsync(Guid unitId, AttendanceSessionSearchQuery query, CancellationToken ct = default);

    Task<AttendanceSessionListItemDto> CreateAdHocAsync(Guid classCourseId, CreateAdHocAttendanceSessionRequest request, CancellationToken ct = default);

    Task<AttendanceRecordsDto> GetRecordsAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Takes or edits the roster. A stale <c>Version</c> is 409 <c>concurrency_conflict</c>; after the edit window a reason and Attendance.Edit are required.</summary>
    Task<AttendanceRecordsDto> TakeAsync(Guid sessionId, TakeAttendanceRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AttendanceChangeDto>> GetHistoryAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Confirms a proposal made from a live session (the teacher's decision; nothing is final before it).</summary>
    Task<AttendanceRecordsDto> ConfirmAsync(Guid sessionId, ConfirmAttendanceRequest request, CancellationToken ct = default);

    Task<AttendanceRecordsDto> SaveLiveMappingsAsync(Guid sessionId, LiveMappingRequest request, CancellationToken ct = default);

    /// <summary>The signed-in student's own attendance (counts and a page of sessions).</summary>
    Task<OwnAttendanceDto> GetMyAttendanceAsync(OwnAttendanceQuery query, CancellationToken ct = default);

    /// <summary>A child's attendance for a guardian (an active link; another child is 404).</summary>
    Task<OwnAttendanceDto> GetStudentAttendanceAsync(Guid studentId, OwnAttendanceQuery query, CancellationToken ct = default);
}
