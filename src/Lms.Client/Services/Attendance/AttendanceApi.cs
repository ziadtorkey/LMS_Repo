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

    public Task<OwnAttendanceDto> GetMyAttendanceAsync(OwnAttendanceQuery query, CancellationToken ct = default) =>
        http.GetAsync<OwnAttendanceDto>(Routes.MyAttendance(query.From, query.To) + PageQuery(Routes.MyAttendance(query.From, query.To), query), ct);

    public Task<OwnAttendanceDto> GetStudentAttendanceAsync(Guid studentId, OwnAttendanceQuery query, CancellationToken ct = default) =>
        http.GetAsync<OwnAttendanceDto>(Routes.StudentAttendance(studentId, query.From, query.To) + PageQuery(Routes.StudentAttendance(studentId, query.From, query.To), query), ct);

    private static string PageQuery(string route, OwnAttendanceQuery query) => (route.Contains('?') ? "&" : "?") + $"page={query.Page}&pageSize={query.PageSize}";
}
