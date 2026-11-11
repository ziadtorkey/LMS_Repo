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
}
