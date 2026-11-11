using Lms.Shared.Access;
using Lms.Shared.Authorization;

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

        // Self-service permissions (own profile, own children) count as held by every caller and are left out of the
        // comparison (M3 contract): no staff role holds them, and they give no power over other people.
        var self = Permissions.SelfService;
        var callerCore = callerPermissions.Except(self, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var roleCore = rolePermissions.Except(self, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (!roleCore.IsSubsetOf(callerCore))
        {
            return false;
        }

        var callerHoldsEverything = PermissionGroups.Assignable.SelectMany(g => g.Permissions)
            .Except(self, StringComparer.Ordinal)
            .All(callerCore.Contains);
        return callerHoldsEverything || roleCore.Count < callerCore.Count;
    }
}
