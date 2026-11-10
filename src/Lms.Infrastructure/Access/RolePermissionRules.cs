using Lms.Shared;
using Lms.Shared.Authorization;

namespace Lms.Infrastructure.Access;

/// <summary>Rules for the permission set of a role being created or edited (Review Focus 2).</summary>
public static class RolePermissionRules
{
    /// <summary>The ProblemCodes value of the first broken rule, or null when the permission set is acceptable.</summary>
    public static string? Validate(
        IReadOnlyCollection<string> requested,
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

        if (!callerIsPlatformAdmin && requested.Any(p => !callerPermissions.Contains(p)))
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
