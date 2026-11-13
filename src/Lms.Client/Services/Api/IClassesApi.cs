using Lms.Shared;
using Lms.Shared.Classes;

namespace Lms.Client.Services.Api;

/// <summary>Classes of an academic year: list, create, bulk create, copy to another year and close (FR-CLS-01, FR-CLS-02, FR-CLS-04).</summary>
public interface IClassesApi
{
    Task<PagedResult<ClassSummaryDto>> ListAsync(ClassQuery query, CancellationToken ct = default);
    Task<ClassSummaryDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ClassSummaryDto> CreateAsync(SaveClassRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, SaveClassRequest request, CancellationToken ct = default);

    /// <summary>Closes the class: every later write on it answers 409 <c>class_closed</c>.</summary>
    Task CloseAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ClassSummaryDto>> BulkCreateAsync(BulkCreateClassesRequest request, CancellationToken ct = default);
    Task<CopyClassesResultDto> CopyAsync(CopyClassesRequest request, CancellationToken ct = default);
}

public sealed class ClassesApi(ApiHttp http) : IClassesApi
{
    public Task<PagedResult<ClassSummaryDto>> ListAsync(ClassQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ClassSummaryDto>>(Routes.Classes + ToQueryString(query), ct);

    public Task<ClassSummaryDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<ClassSummaryDto>(Routes.Class(id), ct);

    public async Task<ClassSummaryDto> CreateAsync(SaveClassRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ClassSummaryDto>(HttpMethod.Post, Routes.Classes, request, ct))!;

    public Task UpdateAsync(Guid id, SaveClassRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.Class(id), request, ct);

    public Task CloseAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassClose(id), null, ct);

    public async Task<IReadOnlyList<ClassSummaryDto>> BulkCreateAsync(BulkCreateClassesRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<List<ClassSummaryDto>>(HttpMethod.Post, Routes.ClassesBulk, request, ct)) ?? [];

    public async Task<CopyClassesResultDto> CopyAsync(CopyClassesRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CopyClassesResultDto>(HttpMethod.Post, Routes.ClassesCopy, request, ct))!;

    /// <summary>Query string of a class list request; unset filters are left out.</summary>
    public static string ToQueryString(ClassQuery q)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.OrgUnitId is { } unit)
        {
            parts.Add($"orgUnitId={unit}");
            parts.Add($"includeDescendants={q.IncludeDescendants.ToString().ToLowerInvariant()}");
        }
        if (q.AcademicYearId is { } year) parts.Add($"academicYearId={year}");
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        if (!string.IsNullOrWhiteSpace(q.Search)) parts.Add($"search={Uri.EscapeDataString(q.Search)}");
        if (!string.IsNullOrWhiteSpace(q.Sort)) parts.Add($"sort={Uri.EscapeDataString(q.Sort)}");
        return "?" + string.Join('&', parts);
    }
}
