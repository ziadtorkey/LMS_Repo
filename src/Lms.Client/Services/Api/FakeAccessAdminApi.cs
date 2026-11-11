using Lms.Shared.Access;
using Lms.Shared.Authorization;

namespace Lms.Client.Services.Api;

/// <summary>In-memory roles API for the standalone dev server and bUnit tests (until the backend is used).</summary>
public sealed class FakeAccessAdminApi : IAccessAdminApi
{
    private readonly List<RoleDto> _roles = BuiltInRoles.All
        .Select((r, i) => new RoleDto(Guid.NewGuid(), r.Key, r.SchoolGroupLabel.NameAr, r.SchoolGroupLabel.NameEn,
            r.BaseKind, true, r.DefaultPermissions, i))
        .ToList();

    /// <summary>When set, the next mutating call throws it (tests simulate server problems).</summary>
    public Exception? NextFailure { get; set; }
    public IReadOnlyList<RoleDto> Roles => _roles;
    public CreateRoleRequest? LastCreate { get; private set; }
    public UpdateRoleRequest? LastUpdate { get; private set; }

    public Task<IReadOnlyList<PermissionGroupDto>> GetPermissionGroupsAsync(CancellationToken ct = default) =>
        Task.FromResult(PermissionGroups.Assignable);

    public Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RoleDto>>(_roles.ToList());

    public Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken ct = default)
    {
        Fail();
        LastCreate = request;
        var role = new RoleDto(Guid.NewGuid(), null, request.NameAr, request.NameEn, request.BaseKind, false, request.Permissions, 0);
        _roles.Add(role);
        return Task.FromResult(role);
    }

    public Task<RoleDto> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default)
    {
        Fail();
        LastUpdate = request;
        var i = _roles.FindIndex(r => r.Id == id);
        _roles[i] = _roles[i] with { NameAr = request.NameAr, NameEn = request.NameEn, Permissions = request.Permissions };
        return Task.FromResult(_roles[i]);
    }

    public Task DeleteRoleAsync(Guid id, CancellationToken ct = default)
    {
        Fail();
        var role = _roles.First(r => r.Id == id);
        if (role.IsBuiltIn) throw new ApiProblemException(409, "built_in_role", null, null);
        if (role.AssignmentCount > 0) throw new ApiProblemException(409, "role_in_use", null, null);
        _roles.Remove(role);
        return Task.CompletedTask;
    }

    private void Fail()
    {
        if (NextFailure is not { } ex) return;
        NextFailure = null;
        throw ex;
    }
}
