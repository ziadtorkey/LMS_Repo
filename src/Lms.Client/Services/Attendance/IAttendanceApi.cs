using Lms.Shared;
using Lms.Shared.Attendance;

namespace Lms.Client.Services.Attendance;

/// <summary>Attendance sessions of a teacher: the day's list, taking and editing, history and the live-session proposal (M5 wave 3, FR-ATT-01).</summary>
public interface IAttendanceApi
{
    /// <summary>The caller's own sessions of a local day (today when null), substitute sessions included.</summary>
    Task<IReadOnlyList<AttendanceSessionListItemDto>> GetSessionsAsync(DateOnly? date = null, CancellationToken ct = default);

    Task<AttendanceSessionListItemDto> CreateAdHocAsync(Guid classCourseId, CreateAdHocAttendanceSessionRequest request, CancellationToken ct = default);

    Task<AttendanceRecordsDto> GetRecordsAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Takes or edits the roster. A stale <c>Version</c> is 409 <c>concurrency_conflict</c>; after the edit window a reason and Attendance.Edit are required.</summary>
    Task<AttendanceRecordsDto> TakeAsync(Guid sessionId, TakeAttendanceRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<AttendanceChangeDto>> GetHistoryAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Confirms a proposal made from a live session (the teacher's decision; nothing is final before it).</summary>
    Task<AttendanceRecordsDto> ConfirmAsync(Guid sessionId, ConfirmAttendanceRequest request, CancellationToken ct = default);

    Task<AttendanceRecordsDto> SaveLiveMappingsAsync(Guid sessionId, LiveMappingRequest request, CancellationToken ct = default);
}

public sealed class AttendanceApi(Api.ApiHttp http) : IAttendanceApi
{
    public async Task<IReadOnlyList<AttendanceSessionListItemDto>> GetSessionsAsync(DateOnly? date = null, CancellationToken ct = default) =>
        await http.GetAsync<List<AttendanceSessionListItemDto>>(Routes.AttendanceSessions(date), ct);

    public async Task<AttendanceSessionListItemDto> CreateAdHocAsync(Guid classCourseId, CreateAdHocAttendanceSessionRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AttendanceSessionListItemDto>(HttpMethod.Post, Routes.ClassCourseAttendanceSessions(classCourseId), request, ct))!;

    public Task<AttendanceRecordsDto> GetRecordsAsync(Guid sessionId, CancellationToken ct = default) =>
        http.GetAsync<AttendanceRecordsDto>(Routes.AttendanceSessionRecords(sessionId), ct);

    public async Task<AttendanceRecordsDto> TakeAsync(Guid sessionId, TakeAttendanceRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AttendanceRecordsDto>(HttpMethod.Put, Routes.AttendanceSessionRecords(sessionId), request, ct))!;

    public async Task<IReadOnlyList<AttendanceChangeDto>> GetHistoryAsync(Guid sessionId, CancellationToken ct = default) =>
        await http.GetAsync<List<AttendanceChangeDto>>(Routes.AttendanceSessionHistory(sessionId), ct);

    public async Task<AttendanceRecordsDto> ConfirmAsync(Guid sessionId, ConfirmAttendanceRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AttendanceRecordsDto>(HttpMethod.Post, Routes.AttendanceSessionConfirm(sessionId), request, ct))!;

    public async Task<AttendanceRecordsDto> SaveLiveMappingsAsync(Guid sessionId, LiveMappingRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AttendanceRecordsDto>(HttpMethod.Put, Routes.AttendanceSessionLiveMappings(sessionId), request, ct))!;
}
