using Lms.Shared.Authorization;

namespace Lms.Shared.Access;

/// <summary>Permissions of one module (the part before the dot) for the role editor.</summary>
public sealed record PermissionGroupDto(string Module, IReadOnlyList<string> Permissions);

/// <summary>Response of GET /api/permissions: every permission a client role may hold, grouped by module.</summary>
public static class PermissionGroups
{
    /// <summary>All non-<c>PlatformOnly</c> permissions grouped by module, in catalog order.</summary>
    public static IReadOnlyList<PermissionGroupDto> Assignable { get; } =
        Authorization.Permissions.All
            .Except(Authorization.Permissions.PlatformOnly, StringComparer.Ordinal)
            .GroupBy(p => p[..p.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal)
            .Select(g => new PermissionGroupDto(g.Key, g.ToArray().AsReadOnly()))
            .ToArray()
            .AsReadOnly();
}

/// <summary>
/// A role of the active client. Built-in roles carry <c>BuiltInKey</c> (<see cref="BuiltInRoles"/>); their labels and
/// permissions are editable per client, their base kind is fixed.
/// </summary>
public sealed record RoleDto(
    Guid Id,
    string? BuiltInKey,
    string NameAr,
    string NameEn,
    string BaseKind,
    bool IsBuiltIn,
    IReadOnlyList<string> Permissions,
    int AssignmentCount);

/// <summary>Body of POST /api/roles (custom role; <c>BaseKind</c> from <see cref="RoleBaseKinds"/>).</summary>
public sealed record CreateRoleRequest(string NameAr, string NameEn, string BaseKind, IReadOnlyList<string> Permissions);

/// <summary>Body of PUT /api/roles/{id}.</summary>
public sealed record UpdateRoleRequest(string NameAr, string NameEn, IReadOnlyList<string> Permissions);
