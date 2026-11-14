using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.YearEnd;

namespace Lms.Client.Services.YearEnd;

/// <summary>Query strings of the year-end lists: only what is set, values escaped.</summary>
public static class YearEndQuery
{
    public static string Of(params (string Name, object? Value)[] parts)
    {
        var set = parts.Where(p => p.Value is not null && p.Value is not false)
            .Select(p => $"{p.Name}={Uri.EscapeDataString(Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "")}");
        var text = string.Join('&', set);
        return text.Length == 0 ? "" : "?" + text;
    }

    public static string Rows(PromotionRowsQuery q) =>
        Of(("outcome", q.Outcome), ("classId", q.ClassId), ("problemsOnly", q.ProblemsOnly ? "true" : null), ("search", q.Search), ("page", q.Page), ("pageSize", q.PageSize));

    public static string Changes(GradeChangeQuery q) =>
        Of(("state", q.State), ("yearId", q.YearId), ("classCourseId", q.ClassCourseId), ("page", q.Page), ("pageSize", q.PageSize));

    public static string Archive(ArchiveQuery q) =>
        Of(("search", q.Search), ("exitKind", q.ExitKind), ("yearId", q.YearId), ("orgUnitId", q.OrgUnitId), ("page", q.Page), ("pageSize", q.PageSize));
}

public sealed class YearCloseApi(ApiHttp http) : IYearCloseApi
{
    public Task<YearClosePreviewDto> GetPreviewAsync(Guid yearId, CancellationToken ct = default) =>
        http.GetAsync<YearClosePreviewDto>(Routes.AcademicYearClosePreview(yearId), ct);

    public async Task<YearClosedDto> CloseAsync(Guid yearId, CloseYearRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<YearClosedDto>(HttpMethod.Post, Routes.AcademicYearClose(yearId), request, ct))!;

    public Task<ClosingReportDto> GetReportAsync(Guid yearId, CancellationToken ct = default) =>
        http.GetAsync<ClosingReportDto>(Routes.AcademicYearClosingReport(yearId), ct);
}

public sealed class GradeChangesApi(ApiHttp http) : IGradeChangesApi
{
    public async Task<GradeChangeRequestDto> CreateAsync(Guid classCourseId, CreateGradeChangeRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradeChangeRequestDto>(HttpMethod.Post, Routes.ClassCourseGradeChangeRequests(classCourseId), request, ct))!;

    public Task<PagedResult<GradeChangeRequestDto>> ListAsync(GradeChangeQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<GradeChangeRequestDto>>(Routes.GradeChangeRequests + YearEndQuery.Changes(query), ct);

    public async Task<GradeChangeRequestDto> ApproveAsync(Guid id, DecideGradeChangeRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradeChangeRequestDto>(HttpMethod.Post, Routes.GradeChangeApprove(id), request, ct))!;

    public async Task<GradeChangeRequestDto> RejectAsync(Guid id, DecideGradeChangeRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradeChangeRequestDto>(HttpMethod.Post, Routes.GradeChangeReject(id), request, ct))!;
}

public sealed class PromotionApi(ApiHttp http) : IPromotionApi
{
    public async Task<PromotionRunDto> CreateRunAsync(CreatePromotionRunRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PromotionRunDto>(HttpMethod.Post, Routes.PromotionRuns, request, ct))!;

    public Task<PromotionRunDto> GetRunAsync(Guid runId, CancellationToken ct = default) =>
        http.GetAsync<PromotionRunDto>(Routes.PromotionRun(runId), ct);

    public Task<PagedResult<PromotionRunDto>> ListRunsAsync(int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<PromotionRunDto>>(Routes.PromotionRuns + YearEndQuery.Of(("page", page), ("pageSize", pageSize)), ct);

    public Task<PagedResult<PromotionRowDto>> GetRowsAsync(Guid runId, PromotionRowsQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<PromotionRowDto>>(Routes.PromotionRunRows(runId) + YearEndQuery.Rows(query), ct);

    public Task SaveDecisionsAsync(Guid runId, SavePromotionDecisionsRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.PromotionRunDecisions(runId), request, ct);

    public async Task<PromotionCountsDto> SetOutcomeForFilterAsync(Guid runId, SetPromotionOutcomeForFilterRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PromotionCountsDto>(HttpMethod.Put, Routes.PromotionRunDecisionsFilter(runId), request, ct))!;

    public async Task<PromotionRunDto> ExecuteAsync(Guid runId, ExecutePromotionRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PromotionRunDto>(HttpMethod.Post, Routes.PromotionRunExecute(runId), request, ct))!;

    public async Task<PromotionRunDto> UndoAsync(Guid runId, CancellationToken ct = default) =>
        (await http.SendAsync<PromotionRunDto>(HttpMethod.Post, Routes.PromotionRunUndo(runId), null, ct))!;

    public Task<PagedResult<PromotionFailureDto>> GetFailuresAsync(Guid runId, int page = 1, int pageSize = Paging.DefaultPageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<PromotionFailureDto>>(Routes.PromotionRunFailures(runId) + YearEndQuery.Of(("page", page), ("pageSize", pageSize)), ct);
}

public sealed class LifecycleApi(ApiHttp http) : ILifecycleApi
{
    public Task WithdrawAsync(Guid studentId, WithdrawStudentRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StudentWithdraw(studentId), request, ct);

    public Task GraduateAsync(Guid studentId, GraduateStudentRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StudentGraduate(studentId), request, ct);

    public Task ReadmitAsync(Guid studentId, ReadmitStudentRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StudentReadmit(studentId), request, ct);

    public Task<PagedResult<ArchivedStudentDto>> GetArchiveAsync(ArchiveQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ArchivedStudentDto>>(Routes.StudentsArchive + YearEndQuery.Archive(query), ct);

    public async Task<IReadOnlyList<WithdrawalReasonDto>> GetReasonsAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<WithdrawalReasonDto>>(Routes.WithdrawalReasons, ct);

    public Task SaveReasonsAsync(SaveWithdrawalReasonsRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.WithdrawalReasons, request, ct);
}

public sealed class NewYearApi(ApiHttp http) : INewYearApi
{
    public async Task<OpenNextYearPreviewDto> PreviewAsync(Guid fromYearId, OpenNextYearRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<OpenNextYearPreviewDto>(HttpMethod.Post, Routes.AcademicYearOpenNextPreview(fromYearId), request, ct))!;

    public async Task<NewYearRunDto> StartAsync(Guid fromYearId, OpenNextYearRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<NewYearRunDto>(HttpMethod.Post, Routes.AcademicYearOpenNext(fromYearId), request, ct))!;

    public Task<NewYearRunDto> GetRunAsync(Guid fromYearId, Guid runId, CancellationToken ct = default) =>
        http.GetAsync<NewYearRunDto>(Routes.AcademicYearOpenNextRun(fromYearId, runId), ct);
}
