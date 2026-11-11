using Lms.Shared.Access;

namespace Lms.Client.Services.Api;

/// <summary>Roles and permissions administration (M1 Task 29; assignments are added by Task 30).</summary>
public interface IAccessAdminApi
{
    Task<IReadOnlyList<PermissionGroupDto>> GetPermissionGroupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<RoleDto> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid id, CancellationToken ct = default);
}
