using Lms.Shared;
using Lms.Shared.Authorization;

namespace Lms.Infrastructure.Access;

/// <summary>Rules for the permission set of a role being created or edited (Review Focus 2).</summary>
public static class RolePermissionRules
{
    /// <summary>Whether the caller may delete a role holding <paramref name="currentPermissions"/> (same rule as editing it).</summary>
    public static bool CanModify(
        IReadOnlyCollection<string> currentPermissions, IReadOnlySet<string> callerPermissions, bool callerIsPlatformAdmin) =>
        callerIsPlatformAdmin || currentPermissions.All(Permissions.WithSelfService(callerPermissions).Contains);

    /// <summary>The ProblemCodes value of the first broken rule, or null when the permission set is acceptable.</summary>
    public static string? Validate(
        IReadOnlyCollection<string> requested,
        IReadOnlyCollection<string> currentPermissions,
        IReadOnlySet<string> callerPermissions,
        bool callerIsPlatformAdmin,
        string? builtInKey)
    {
        if (requested.Any(p => !Permissions.All.Contains(p)))
        {
            return ProblemCodes.validation_failed;
        }

        if (requested.Any(Permissions.PlatformOnly.Contains))
        {
            return ProblemCodes.platform_only_permission;
        }

        // Both sides of the edit must be within the caller's own permissions: otherwise a lesser role editor could
        // strip a more powerful role (the Org Admin) down to what they hold, and nobody could restore it.
        // Self-service permissions (own profile, own children) count as held by every caller (M3 contract).
        callerPermissions = Permissions.WithSelfService(callerPermissions);
        if (!callerIsPlatformAdmin
            && (requested.Any(p => !callerPermissions.Contains(p)) || currentPermissions.Any(p => !callerPermissions.Contains(p))))
        {
            return ProblemCodes.permission_escalation;
        }

        // The built-in Org Admin must keep the means to fix roles, or the client could lock itself out.
        if (builtInKey == BuiltInRoles.OrgAdmin
            && (!requested.Contains(Permissions.Roles.View) || !requested.Contains(Permissions.Roles.Update)))
        {
            return ProblemCodes.last_admin_permission;
        }

        return null;
    }
}
