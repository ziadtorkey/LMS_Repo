using Lms.Shared;
using Lms.Shared.Access;

namespace Lms.Client.Services.Api;

/// <summary>Roles and permissions administration and role assignments (M1 Tasks 29-30).</summary>
public interface IAccessAdminApi
{
    Task<IReadOnlyList<PermissionGroupDto>> GetPermissionGroupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<RoleDto> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid id, CancellationToken ct = default);

    // Role assignments (Task 30)
    Task<PagedResult<AssignmentDto>> GetAssignmentsAsync(Guid unitId, bool includeDescendants, int page, int pageSize, CancellationToken ct = default);
    Task<AssignmentDto> CreateAssignmentAsync(CreateAssignmentRequest request, CancellationToken ct = default);
    Task<AssignmentDto> UpdateAssignmentAsync(Guid id, UpdateAssignmentRequest request, CancellationToken ct = default);
    Task DeleteAssignmentAsync(Guid id, CancellationToken ct = default);
}
