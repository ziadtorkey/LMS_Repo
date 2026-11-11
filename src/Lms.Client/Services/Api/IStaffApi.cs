using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>Staff members (FR-STF-01..03). Task 25 appends update, status, delete and share.</summary>
public interface IStaffApi
{
    Task<PagedResult<StaffSummaryDto>> ListAsync(StaffQuery query, CancellationToken ct = default);
    Task<StaffDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<StaffDetailDto> CreateAsync(CreateStaffRequest request, CancellationToken ct = default);
}

public sealed class StaffApi(ApiHttp http) : IStaffApi
{
    public Task<PagedResult<StaffSummaryDto>> ListAsync(StaffQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<StaffSummaryDto>>(Routes.Staff + ToQueryString(query), ct);

    public Task<StaffDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<StaffDetailDto>(Routes.StaffMember(id), ct);

    public async Task<StaffDetailDto> CreateAsync(CreateStaffRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<StaffDetailDto>(HttpMethod.Post, Routes.Staff, request, ct))!;

    /// <summary>Query string of a staff list request; unset filters are left out.</summary>
    public static string ToQueryString(StaffQuery q)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.OrgUnitId is { } unit)
        {
            parts.Add($"orgUnitId={unit}");
            parts.Add($"includeDescendants={q.IncludeDescendants.ToString().ToLowerInvariant()}");
        }
        if (q.RoleId is { } role) parts.Add($"roleId={role}");
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        if (!string.IsNullOrWhiteSpace(q.Search)) parts.Add($"search={Uri.EscapeDataString(q.Search)}");
        parts.Add($"includeShared={q.IncludeShared.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(q.Sort)) parts.Add($"sort={Uri.EscapeDataString(q.Sort)}");
        return "?" + string.Join('&', parts);
    }
}
