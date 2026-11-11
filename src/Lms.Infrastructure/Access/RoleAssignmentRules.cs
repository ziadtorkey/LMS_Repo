using Lms.Shared.Access;

namespace Lms.Infrastructure.Access;

/// <summary>The escalation guard for role assignments (PRD D16, Review Focus 2).</summary>
public static class RoleAssignmentRules
{
    /// <summary>
    /// A caller may assign (or remove) a role only if its permissions are a subset of their own, and a strict subset
    /// unless they hold every permission a client role can hold (the Org Admin): so a Manager can hand out Teacher,
    /// Counselor or Supervisor but never Manager or Org Admin. Platform admins are exempt.
    /// </summary>
    public static bool CanAssign(
        IReadOnlySet<string> rolePermissions, IReadOnlySet<string> callerPermissions, bool callerIsPlatformAdmin)
    {
        if (callerIsPlatformAdmin)
        {
            return true;
        }

        if (!rolePermissions.IsSubsetOf(callerPermissions))
        {
            return false;
        }

        var callerHoldsEverything = PermissionGroups.Assignable.SelectMany(g => g.Permissions)
            .All(callerPermissions.Contains);
        return callerHoldsEverything || rolePermissions.Count < callerPermissions.Count;
    }
}
