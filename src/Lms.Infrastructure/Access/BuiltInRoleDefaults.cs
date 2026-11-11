using Lms.Shared.Authorization;

namespace Lms.Infrastructure.Access;

/// <summary>Versioning of the built-in role default permission sets (see <see cref="BuiltInRoleReconciler"/>).</summary>
public static class BuiltInRoleDefaults
{
    /// <summary>
    /// Version 1 = the M1 defaults; version 2 = the M2 defaults (Staff.*, IdentityProviders.*). Bump (and add the new
    /// permissions to <see cref="IntroducedAfterVersion1"/>-style sets) when a later milestone adds defaults.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>The 19 permissions that existed when clients were provisioned under M0 (defaults version 0).</summary>
    public static readonly IReadOnlySet<string> M0Baseline = new HashSet<string>
    {
        Permissions.OrgUnits.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.Update, Permissions.OrgUnits.Delete,
        Permissions.Roles.View, Permissions.Roles.Create, Permissions.Roles.Update, Permissions.Roles.Delete,
        Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Delete,
        Permissions.Settings.View, Permissions.Settings.Update, Permissions.Policies.View, Permissions.Policies.Update,
        Permissions.Audit.View, Permissions.Audit.Export, Permissions.Files.Upload, Permissions.Files.Download,
    };

    /// <summary>Permissions introduced by defaults version 2 (M2 People).</summary>
    public static readonly IReadOnlySet<string> IntroducedAfterVersion1 = new HashSet<string>
    {
        Permissions.Staff.View, Permissions.Staff.Create, Permissions.Staff.Update, Permissions.Staff.Delete,
        Permissions.Staff.Import, Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage,
    };

    /// <summary>
    /// The defaults version that introduced <paramref name="permission"/>: 0 for the M0 baseline, 2 for the M2 set,
    /// otherwise 1. The reconciler adds to a role only the defaults introduced after the role's version.
    /// </summary>
    public static int IntroducedIn(string permission) =>
        M0Baseline.Contains(permission) ? 0
        : IntroducedAfterVersion1.Contains(permission) ? 2
        : 1;
}
