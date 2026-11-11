using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>Staff members (FR-STF-01..03). Update, status, delete, invitation and sharing (Task 25).</summary>
public interface IStaffApi
{
    Task<PagedResult<StaffSummaryDto>> ListAsync(StaffQuery query, CancellationToken ct = default);
    Task<StaffDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<StaffDetailDto> CreateAsync(CreateStaffRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateStaffRequest request, CancellationToken ct = default);
    Task SetStatusAsync(Guid id, SetPersonStatusRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task ResendInvitationAsync(Guid id, CancellationToken ct = default);
    Task ShareAsync(Guid id, ShareStaffRequest request, CancellationToken ct = default);
    Task RemoveShareAsync(Guid id, Guid assignmentId, CancellationToken ct = default);
}

public sealed class StaffApi(ApiHttp http) : IStaffApi
{
    public Task<PagedResult<StaffSummaryDto>> ListAsync(StaffQuery query, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<StaffSummaryDto>>(Routes.Staff + ToQueryString(query), ct);

    public Task<StaffDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<StaffDetailDto>(Routes.StaffMember(id), ct);

    public async Task<StaffDetailDto> CreateAsync(CreateStaffRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<StaffDetailDto>(HttpMethod.Post, Routes.Staff, request, ct))!;

    public Task UpdateAsync(Guid id, UpdateStaffRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.StaffMember(id), request, ct);

    public Task SetStatusAsync(Guid id, SetPersonStatusRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.StaffStatus(id), request, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.StaffMember(id), null, ct);

    public Task ResendInvitationAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StaffResendInvitation(id), null, ct);

    public Task ShareAsync(Guid id, ShareStaffRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.StaffShares(id), request, ct);

    public Task RemoveShareAsync(Guid id, Guid assignmentId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.StaffShare(id, assignmentId), null, ct);

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
