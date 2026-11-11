using Lms.Shared;
using Lms.Shared.Access;

namespace Lms.Client.Services.Api;

public sealed class AccessAdminApi(ApiHttp http) : IAccessAdminApi
{
    public async Task<IReadOnlyList<PermissionGroupDto>> GetPermissionGroupsAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<PermissionGroupDto>>(Routes.Permissions, ct);

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<RoleDto>>(Routes.Roles, ct);

    public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<RoleDto>(HttpMethod.Post, Routes.Roles, request, ct))!;

    public async Task<RoleDto> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<RoleDto>(HttpMethod.Put, Routes.Role(id), request, ct))!;

    public Task DeleteRoleAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Role(id), null, ct);

    public async Task<PagedResult<AssignmentDto>> GetAssignmentsAsync(Guid unitId, bool includeDescendants, int page, int pageSize, CancellationToken ct = default) =>
        await http.GetAsync<PagedResult<AssignmentDto>>(
            $"{Routes.OrgUnitAssignments(unitId)}?includeDescendants={includeDescendants.ToString().ToLowerInvariant()}&page={page}&pageSize={pageSize}", ct);

    public async Task<AssignmentDto> CreateAssignmentAsync(CreateAssignmentRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AssignmentDto>(HttpMethod.Post, Routes.Assignments, request, ct))!;

    public async Task<AssignmentDto> UpdateAssignmentAsync(Guid id, UpdateAssignmentRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AssignmentDto>(HttpMethod.Put, Routes.Assignment(id), request, ct))!;

    public Task DeleteAssignmentAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Assignment(id), null, ct);
}
