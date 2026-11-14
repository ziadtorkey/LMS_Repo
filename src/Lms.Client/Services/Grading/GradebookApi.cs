using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Client.Services.Grading;

public sealed class GradebookApi(ApiHttp http) : IGradebookApi
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public Task<GradebookDto> GetAsync(Guid classCourseId, GradebookQuery query, CancellationToken ct = default) =>
        http.GetAsync<GradebookDto>(Routes.ClassCourseGradebook(classCourseId, query.TermId, query.Page, query.PageSize), ct);

    public async Task<GradebookConfigDto> SaveConfigAsync(Guid classCourseId, SaveGradebookConfigRequest request, Guid? termId = null, CancellationToken ct = default) =>
        (await http.SendAsync<GradebookConfigDto>(HttpMethod.Put, Routes.ClassCourseGradebookConfig(classCourseId, termId), request, ct))!;

    public async Task<IReadOnlyList<GradeCategoryDto>> SaveCategoriesAsync(Guid classCourseId, SaveGradeCategoriesRequest request, Guid? termId = null, CancellationToken ct = default) =>
        (await http.SendAsync<List<GradeCategoryDto>>(HttpMethod.Put, Routes.ClassCourseGradebookCategories(classCourseId, termId), request, ct))!;

    public async Task<GradeItemDto> CreateItemAsync(Guid classCourseId, CreateGradeItemRequest request, Guid? termId = null, CancellationToken ct = default) =>
        (await http.SendAsync<GradeItemDto>(HttpMethod.Post, Routes.ClassCourseGradebookItems(classCourseId, termId), request, ct))!;

    public async Task<GradeItemDto> UpdateItemAsync(Guid classCourseId, Guid itemId, UpdateGradeItemRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradeItemDto>(HttpMethod.Put, Routes.ClassCourseGradebookItem(classCourseId, itemId), request, ct))!;

    public Task DeleteItemAsync(Guid classCourseId, Guid itemId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.ClassCourseGradebookItem(classCourseId, itemId), null, ct);

    public async Task<GradeRowDto> UpsertEntriesAsync(Guid classCourseId, UpsertEntriesRequest request, Guid? termId = null, CancellationToken ct = default) =>
        (await http.SendAsync<GradeRowDto>(HttpMethod.Put, Routes.ClassCourseGradebookEntries(classCourseId, termId), request, ct))!;

    public async Task<PublishResultDto> PublishAsync(Guid classCourseId, PublishItemsRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PublishResultDto>(HttpMethod.Post, Routes.ClassCourseGradebookPublish(classCourseId), request, ct))!;

    public Task<StudentCourseGradesDto> GetStudentViewAsync(Guid classCourseId, Guid studentId, Guid? termId = null, CancellationToken ct = default) =>
        http.GetAsync<StudentCourseGradesDto>(
            Routes.ClassCourseGradebookStudentView(classCourseId, studentId) + (termId is { } t ? $"?termId={t}" : string.Empty), ct);

    public Task<(Stream Content, string FileName)> ExportItemAsync(Guid classCourseId, Guid itemId, CancellationToken ct = default) =>
        http.DownloadAsync(Routes.ClassCourseGradebookItemExport(classCourseId, itemId), ct);

    public Task<GradeImportPreviewDto> PreviewImportAsync(Guid classCourseId, Guid itemId, string fileName, Stream content, CancellationToken ct = default) =>
        http.UploadAsync<GradeImportPreviewDto>(Routes.ClassCourseGradebookItemImportPreview(classCourseId, itemId), fileName, XlsxType, content, null, ct);

    public async Task<GradeImportResultDto> ApplyImportAsync(Guid classCourseId, Guid itemId, ApplyGradeImportRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradeImportResultDto>(HttpMethod.Post, Routes.ClassCourseGradebookItemImportApply(classCourseId, itemId), request, ct))!;

    public Task<StudentGradesDto> GetMyGradesAsync(Guid? termId = null, CancellationToken ct = default) =>
        http.GetAsync<StudentGradesDto>(Routes.MyGrades(termId), ct);

    public Task<StudentGradesDto> GetStudentGradesAsync(Guid studentId, Guid? termId = null, CancellationToken ct = default) =>
        http.GetAsync<StudentGradesDto>(Routes.StudentGrades(studentId, termId), ct);

    public Task<GradeReportDto> GetReportAsync(string kind, GradeReportQuery query, CancellationToken ct = default) =>
        http.GetAsync<GradeReportDto>(Routes.GradeReport(kind) + ReportQueryString(query), ct);

    public Task<(Stream Content, string FileName)> ExportReportAsync(string kind, GradeReportQuery query, CancellationToken ct = default) =>
        http.DownloadAsync(Routes.GradeReportExport(kind) + ReportQueryString(query), ct);

    /// <summary>The report filters as a query string (only what is set).</summary>
    public static string ReportQueryString(GradeReportQuery q)
    {
        var parts = new List<string>();
        if (q.StudentId is { } s) parts.Add($"studentId={s}");
        if (q.ClassId is { } c) parts.Add($"classId={c}");
        if (q.ClassCourseId is { } cc) parts.Add($"classCourseId={cc}");
        if (q.TermId is { } t) parts.Add($"termId={t}");
        parts.Add($"page={q.Page}");
        parts.Add($"pageSize={q.PageSize}");
        return "?" + string.Join('&', parts);
    }
}
