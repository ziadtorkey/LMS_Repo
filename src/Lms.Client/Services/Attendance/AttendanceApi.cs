using Lms.Shared;
using Lms.Shared.Attendance;

namespace Lms.Client.Services.Attendance;

public sealed class AttendanceApi(Api.ApiHttp http) : IAttendanceApi
{
    public async Task<IReadOnlyList<AttendanceSessionListItemDto>> GetSessionsAsync(DateOnly? date = null, CancellationToken ct = default) =>
        await http.GetAsync<List<AttendanceSessionListItemDto>>(Routes.AttendanceSessions(date), ct);

    public Task<PagedResult<AttendanceSessionListItemDto>> SearchSessionsAsync(Guid unitId, AttendanceSessionSearchQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<AttendanceSessionListItemDto>>(Routes.OrgUnitAttendanceSessions(unitId, query.From, query.To, query.ClassId, query.ClassCourseId)
            + (query.State is { } s ? $"&state={Uri.EscapeDataString(s)}" : "") + $"&page={query.Page}&pageSize={query.PageSize}", ct);

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

    public Task<PagedResult<AttendanceAlertDto>> GetAlertsAsync(AttendanceAlertsQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<AttendanceAlertDto>>(Routes.AttendanceAlertsPattern + Query(("unitId", query.UnitId), ("activeOnly", query.ActiveOnly), ("page", query.Page), ("pageSize", query.PageSize)), ct);

    public Task<AttendanceReportDto> GetReportAsync(string kind, AttendanceReportQuery query, CancellationToken ct = default) =>
        http.GetAsync<AttendanceReportDto>(Routes.AttendanceReport(kind) + ReportQuery(query), ct);

    public Task<(Stream Content, string FileName)> ExportReportAsync(string kind, AttendanceReportQuery query, CancellationToken ct = default) =>
        http.DownloadAsync(Routes.AttendanceReportExport(kind) + ReportQuery(query), ct);

    private static string ReportQuery(AttendanceReportQuery q) => Query(("studentId", q.StudentId), ("classId", q.ClassId), ("classCourseId", q.ClassCourseId),
        ("from", q.From is { } f ? f.ToString("yyyy-MM-dd") : null), ("to", q.To is { } t ? t.ToString("yyyy-MM-dd") : null), ("page", q.Page), ("pageSize", q.PageSize));

    private static string Query(params (string Key, object? Value)[] parts)
    {
        var query = string.Join('&', parts.Where(p => p.Value is not null)
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value is bool b ? (b ? "true" : "false") : Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)!)}"));
        return query.Length == 0 ? "" : "?" + query;
    }

    public Task<OwnAttendanceDto> GetMyAttendanceAsync(OwnAttendanceQuery query, CancellationToken ct = default) =>
        http.GetAsync<OwnAttendanceDto>(Routes.MyAttendance(query.From, query.To) + PageQuery(Routes.MyAttendance(query.From, query.To), query), ct);

    public Task<OwnAttendanceDto> GetStudentAttendanceAsync(Guid studentId, OwnAttendanceQuery query, CancellationToken ct = default) =>
        http.GetAsync<OwnAttendanceDto>(Routes.StudentAttendance(studentId, query.From, query.To) + PageQuery(Routes.StudentAttendance(studentId, query.From, query.To), query), ct);

    private static string PageQuery(string route, OwnAttendanceQuery query) => (route.Contains('?') ? "&" : "?") + $"page={query.Page}&pageSize={query.PageSize}";
}
