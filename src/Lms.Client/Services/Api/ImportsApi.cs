using System.Net.Http.Headers;
using Lms.Shared;
using Lms.Shared.Imports;
using Microsoft.AspNetCore.Components.Forms;

namespace Lms.Client.Services.Api;

public sealed class ImportsApi(ApiHttp http) : IImportsApi
{
    public async Task<IReadOnlyList<ImportColumnDto>> GetColumnsAsync(string kind, CancellationToken ct = default) =>
        await http.GetAsync<List<ImportColumnDto>>(Routes.ImportColumns(kind), ct);

    public Task<(Stream Content, string FileName)> DownloadTemplateAsync(string kind, string culture, CancellationToken ct = default) =>
        http.DownloadAsync(Routes.ImportTemplate(kind, culture), ct);

    public async Task<ImportBatchDto> UploadAsync(string kind, Guid orgUnitId, IBrowserFile file, CancellationToken ct = default)
    {
        await using var stream = file.OpenReadStream(ImportLimits.MaxFileBytes, ct);
        return await http.UploadAsync<ImportBatchDto>(
            Routes.ImportsOfKind(kind), file.Name,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", stream,
            new Dictionary<string, string> { ["orgUnitId"] = orgUnitId.ToString() }, ct);
    }

    public Task<ImportBatchDto> GetBatchAsync(string kind, Guid id, CancellationToken ct = default) =>
        http.GetAsync<ImportBatchDto>(Routes.ImportBatch(kind, id), ct);

    public Task<PagedResult<ImportRowDto>> GetRowsAsync(
        string kind, Guid id, bool errorsOnly, string culture, int page, int pageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ImportRowDto>>(
            $"{Routes.ImportRows(kind, id)}?errorsOnly={errorsOnly.ToString().ToLowerInvariant()}" +
            $"&culture={Uri.EscapeDataString(culture)}&page={page}&pageSize={pageSize}", ct);

    public Task<(Stream Content, string FileName)> DownloadErrorReportAsync(string kind, Guid id, CancellationToken ct = default) =>
        http.DownloadAsync(Routes.ImportErrorReport(kind, id), ct);

    public async Task<ImportBatchDto> CommitAsync(string kind, Guid id, CancellationToken ct = default) =>
        (await http.SendAsync<ImportBatchDto>(HttpMethod.Post, Routes.ImportCommit(kind, id), null, ct))!;

    public Task DiscardAsync(string kind, Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.ImportBatch(kind, id), null, ct);

    public Task<PagedResult<ImportBatchDto>> ListAsync(string kind, int page, int pageSize, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ImportBatchDto>>($"{Routes.ImportsOfKind(kind)}?page={page}&pageSize={pageSize}", ct);
}
