using Lms.Shared;
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

    private readonly List<AssignmentDto> _assignments = [];

    public IReadOnlyList<AssignmentDto> Assignments => _assignments;
    public (Guid UnitId, bool IncludeDescendants, int Page, int PageSize)? LastQuery { get; private set; }
    public CreateAssignmentRequest? LastAssignment { get; private set; }
    public UpdateAssignmentRequest? LastAssignmentUpdate { get; private set; }

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

    public Task<PagedResult<AssignmentDto>> GetAssignmentsAsync(Guid unitId, bool includeDescendants, int page, int pageSize, CancellationToken ct = default)
    {
        LastQuery = (unitId, includeDescendants, page, pageSize);
        var rows = _assignments.Where(a => a.OrgUnitId == unitId || (includeDescendants && Below(a.OrgUnitId, unitId))).ToList();
        var items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<AssignmentDto>(items, rows.Count, page, pageSize));
    }

    public Task<AssignmentDto> CreateAssignmentAsync(CreateAssignmentRequest request, CancellationToken ct = default)
    {
        Fail();
        LastAssignment = request;
        var role = _roles.First(r => r.Id == request.RoleId);
        var unit = FakeUnits.All.First(u => u.Id == request.OrgUnitId);
        var dto = new AssignmentDto(Guid.NewGuid(), Guid.NewGuid(), request.Email,
            request.DisplayNameAr ?? request.Email, request.DisplayNameEn ?? request.Email,
            role.Id, role.NameAr, role.NameEn, unit.Id, unit.NameAr, unit.NameEn,
            request.ValidFrom, request.ValidTo, IsValidToday(request.ValidFrom, request.ValidTo));
        _assignments.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<AssignmentDto> UpdateAssignmentAsync(Guid id, UpdateAssignmentRequest request, CancellationToken ct = default)
    {
        Fail();
        LastAssignmentUpdate = request;
        var i = _assignments.FindIndex(a => a.Id == id);
        _assignments[i] = _assignments[i] with
        {
            ValidFrom = request.ValidFrom, ValidTo = request.ValidTo, IsCurrentlyValid = IsValidToday(request.ValidFrom, request.ValidTo),
        };
        return Task.FromResult(_assignments[i]);
    }

    public Task DeleteAssignmentAsync(Guid id, CancellationToken ct = default)
    {
        Fail();
        _assignments.RemoveAll(a => a.Id == id);
        return Task.CompletedTask;
    }

    /// <summary>Adds a ready-made assignment (tests and demo data).</summary>
    public AssignmentDto Seed(string email, Guid roleId, Guid unitId, DateOnly? from = null, DateOnly? to = null)
    {
        var role = _roles.First(r => r.Id == roleId);
        var unit = FakeUnits.All.First(u => u.Id == unitId);
        var dto = new AssignmentDto(Guid.NewGuid(), Guid.NewGuid(), email, email, email, role.Id, role.NameAr, role.NameEn,
            unit.Id, unit.NameAr, unit.NameEn, from, to, IsValidToday(from, to));
        _assignments.Add(dto);
        return dto;
    }

    private static bool IsValidToday(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return (from is null || from <= today) && (to is null || to >= today);
    }

    private static bool Below(Guid unitId, Guid ancestorId)
    {
        for (var u = FakeUnits.All.FirstOrDefault(x => x.Id == unitId); u?.ParentId is { } p; u = FakeUnits.All.FirstOrDefault(x => x.Id == p))
            if (p == ancestorId) return true;
        return false;
    }

    private void Fail()
    {
        if (NextFailure is not { } ex) return;
        NextFailure = null;
        throw ex;
    }
}
