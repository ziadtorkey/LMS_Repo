using Lms.Shared;
using Lms.Shared.Attendance;

namespace Lms.Client.Services.Attendance;

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
