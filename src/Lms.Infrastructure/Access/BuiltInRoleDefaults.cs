using Lms.Shared.Authorization;

namespace Lms.Infrastructure.Access;

/// <summary>Versioning of the built-in role default permission sets (see <see cref="BuiltInRoleReconciler"/>).</summary>
public static class BuiltInRoleDefaults
{
    /// <summary>
    /// Version 1 = the M1 defaults; version 2 = the M2 defaults (Staff.*, IdentityProviders.*); version 3 = the M3
    /// defaults (Students.*, Courses.*, Blueprints.*, Classes.*, TeacherAssignments.*, Enrollment.*, Groups.*,
    /// LessonPlans.*). Bump (and add the new permissions to an <see cref="IntroducedInVersion3"/>-style set) when a
    /// later milestone adds defaults.
    /// </summary>
    public const int CurrentVersion = 3;

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

    /// <summary>Permissions introduced by defaults version 3 (M3 Academic Structure).</summary>
    public static readonly IReadOnlySet<string> IntroducedInVersion3 = new HashSet<string>
    {
        Permissions.Students.View, Permissions.Students.Create, Permissions.Students.Update, Permissions.Students.Delete,
        Permissions.Students.Import, Permissions.Students.ApproveChanges, Permissions.Students.SelfEdit,
        Permissions.Students.ViewChildren,
        Permissions.Courses.View, Permissions.Courses.Create, Permissions.Courses.Update, Permissions.Courses.Delete,
        Permissions.Courses.Import, Permissions.Courses.Allocate, Permissions.Courses.ManageSupervisors,
        Permissions.Blueprints.View, Permissions.Blueprints.Edit, Permissions.Blueprints.Publish,
        Permissions.Classes.View, Permissions.Classes.Create, Permissions.Classes.Update, Permissions.Classes.Delete,
        Permissions.Classes.Close, Permissions.Classes.ManageCourses,
        Permissions.TeacherAssignments.View, Permissions.TeacherAssignments.Manage,
        Permissions.Enrollment.View, Permissions.Enrollment.Manage, Permissions.Enrollment.Import,
        Permissions.Enrollment.Transfer, Permissions.Enrollment.RequestException, Permissions.Enrollment.ApproveException,
        Permissions.Groups.View, Permissions.Groups.Manage, Permissions.LessonPlans.View,
    };

    /// <summary>
    /// The defaults version that introduced <paramref name="permission"/>: 0 for the M0 baseline, 2 for the M2 set,
    /// 3 for the M3 set, otherwise 1. The reconciler adds to a role only the defaults introduced after the role's version.
    /// </summary>
    public static int IntroducedIn(string permission) =>
        M0Baseline.Contains(permission) ? 0
        : IntroducedAfterVersion1.Contains(permission) ? 2
        : IntroducedInVersion3.Contains(permission) ? 3
        : 1;
}
