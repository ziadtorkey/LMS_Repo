using Lms.Shared.Authorization;

namespace Lms.Infrastructure.Access;

/// <summary>Versioning of the built-in role default permission sets (see <see cref="BuiltInRoleReconciler"/>).</summary>
public static class BuiltInRoleDefaults
{
    /// <summary>Version 1 = the M1 defaults. Bump (and add a baseline) when a later milestone adds defaults.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The 19 permissions that existed when clients were provisioned under M0 (defaults version 0).</summary>
    public static readonly IReadOnlySet<string> M0Baseline = new HashSet<string>
    {
        Permissions.OrgUnits.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.Update, Permissions.OrgUnits.Delete,
        Permissions.Roles.View, Permissions.Roles.Create, Permissions.Roles.Update, Permissions.Roles.Delete,
        Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Delete,
        Permissions.Settings.View, Permissions.Settings.Update, Permissions.Policies.View, Permissions.Policies.Update,
        Permissions.Audit.View, Permissions.Audit.Export, Permissions.Files.Upload, Permissions.Files.Download,
    };
}