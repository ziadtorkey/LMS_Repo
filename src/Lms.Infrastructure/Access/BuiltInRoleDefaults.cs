using Lms.Shared.Authorization;

namespace Lms.Infrastructure.Access;

/// <summary>Versioning of the built-in role default permission sets (see <see cref="BuiltInRoleReconciler"/>).</summary>
public static class BuiltInRoleDefaults
{
    /// <summary>
    /// Version 1 = the M1 defaults; version 2 = the M2 defaults (Staff.*, IdentityProviders.*); version 3 = the M3
    /// defaults (Students.*, Courses.*, Blueprints.*, Classes.*, TeacherAssignments.*, Enrollment.*, Groups.*,
    /// LessonPlans.*); version 4 = the M4a wave 1 defaults (Modules.*); version 5 = the M4a wave 2 defaults
    /// (CourseAssignments.*, Quizzes.*, Library.*, Forums.*); version 6 = the M4b wave 3a defaults (LiveSessions.*);
    /// version 7 = the M4b wave 3b defaults (Packages.*); version 8 = the M5 wave 1 defaults (Grades.*); version 9 = the M5 wave 2 defaults (Timetable.*). Bump (and add the new permissions to an
    /// <see cref="IntroducedInVersion9"/>-style set) when a later milestone adds defaults.
    /// </summary>
    public const int CurrentVersion = 9;

    /// <summary>The 19 permissions that existed when clients were provisioned under M0 (defaults version 0).</summary>
    public static readonly IReadOnlySet<string> M0Baseline = new HashSet<string>
    {
        Permissions.OrgUnits.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.Update, Permissions.OrgUnits.Delete,
        Permissions.Roles.View, Permissions.Roles.Create, Permissions.Roles.Update, Permissions.Roles.Delete,
        Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Delete,
        Permissions.Settings.View, Permissions.Settings.Update, Permissions.Policies.View, Permissions.Policies.Update,
        Permissions.Audit.View, Permissions.Audit.Export, Permissions.Files.Upload, Permissions.Files.Download,
    };

    /// <summary>
    /// The permission set each built-in role was provisioned with under M0 (contracts #22), frozen: a permission that
    /// existed in M0 but was given to a role only later (the Manager's D16 Assignments.Create/Delete) is "new" for that
    /// role, so <see cref="IntroducedIn(string, string)"/> consults this list, not the global <see cref="M0Baseline"/>.
    /// Never edit it: it records what M0 clients really have.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> M0RoleDefaults =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [BuiltInRoles.OrgAdmin] = M0Baseline,
            [BuiltInRoles.Manager] = new HashSet<string>
            {
                Permissions.OrgUnits.View, Permissions.Roles.View, Permissions.Assignments.View, Permissions.Settings.View,
                Permissions.Policies.View, Permissions.Audit.View, Permissions.Files.Upload, Permissions.Files.Download,
            },
            [BuiltInRoles.Supervisor] = new HashSet<string>
            {
                Permissions.OrgUnits.View, Permissions.Settings.View, Permissions.Policies.View, Permissions.Files.Upload,
                Permissions.Files.Download,
            },
            [BuiltInRoles.Counselor] = new HashSet<string> { Permissions.Files.Upload, Permissions.Files.Download },
            [BuiltInRoles.Teacher] = new HashSet<string> { Permissions.Files.Upload, Permissions.Files.Download },
            [BuiltInRoles.Student] = new HashSet<string> { Permissions.Files.Upload, Permissions.Files.Download },
            [BuiltInRoles.Guardian] = new HashSet<string> { Permissions.Files.Download },
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

    /// <summary>Permissions introduced by defaults version 4 (M4a Teaching & Learning, wave 1).</summary>
    public static readonly IReadOnlySet<string> IntroducedInVersion4 = new HashSet<string>
    {
        Permissions.Modules.View, Permissions.Modules.Manage,
    };

    /// <summary>Permissions introduced by defaults version 5 (M4a Teaching & Learning, wave 2).</summary>
    public static readonly IReadOnlySet<string> IntroducedInVersion5 = new HashSet<string>
    {
        Permissions.CourseAssignments.View, Permissions.CourseAssignments.Manage, Permissions.CourseAssignments.Submit,
        Permissions.CourseAssignments.Grade,
        Permissions.Quizzes.View, Permissions.Quizzes.Manage, Permissions.Quizzes.Attempt, Permissions.Quizzes.Grade,
        Permissions.Library.View, Permissions.Library.Manage,
        Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate,
    };

    /// <summary>Permissions introduced by defaults version 6 (M4b live sessions, wave 3a).</summary>
    public static readonly IReadOnlySet<string> IntroducedInVersion6 = new HashSet<string>
    {
        Permissions.LiveSessions.View, Permissions.LiveSessions.Manage,
    };

    /// <summary>Permissions introduced by defaults version 9 (M5 wave 2: timetable).</summary>
    public static readonly IReadOnlySet<string> IntroducedInVersion9 = new HashSet<string>
    {
        Permissions.Timetable.View, Permissions.Timetable.Manage, Permissions.Timetable.Configure, Permissions.Timetable.ViewOwn,
    };

    /// <summary>Permissions introduced by defaults version 8 (M5 wave 1: grading).</summary>
    public static readonly IReadOnlySet<string> IntroducedInVersion8 = new HashSet<string>
    {
        Permissions.Grades.View, Permissions.Grades.Manage, Permissions.Grades.Publish, Permissions.Grades.ConfigureScales,
        Permissions.Grades.ViewReports, Permissions.Grades.ViewOwn,
    };

    /// <summary>Permissions introduced by defaults version 7 (M4b wave 3b: SCORM and H5P packages).</summary>
    public static readonly IReadOnlySet<string> IntroducedInVersion7 = new HashSet<string>
    {
        Permissions.Packages.Upload, Permissions.Packages.View,
    };

    /// <summary>
    /// The defaults version that introduced <paramref name="permission"/>: 0 for the M0 baseline, 2 for the M2 set,
    /// 3 for the M3 set, 4 for the M4a wave 1 set, 5 for the M4a wave 2 set, 6 for the M4b wave 3a set, 7 for the M4b
    /// wave 3b set, 8 for the M5 wave 1 set, 9 for the M5 wave 2 set, otherwise 1. The reconciler adds to a role only the defaults introduced after the role's version.
    /// </summary>
    public static int IntroducedIn(string permission) =>
        M0Baseline.Contains(permission) ? 0
        : IntroducedAfterVersion1.Contains(permission) ? 2
        : IntroducedInVersion3.Contains(permission) ? 3
        : IntroducedInVersion4.Contains(permission) ? 4
        : IntroducedInVersion5.Contains(permission) ? 5
        : IntroducedInVersion6.Contains(permission) ? 6
        : IntroducedInVersion7.Contains(permission) ? 7
        : IntroducedInVersion9.Contains(permission) ? 9
        : IntroducedInVersion8.Contains(permission) ? 8
        : 1;

    /// <summary>
    /// The defaults version that gave <paramref name="permission"/> to the built-in role <paramref name="roleKey"/>: 0 when
    /// the role's own M0 list had it, otherwise the version that introduced the permission (at least 1: it reached this
    /// role after M0 even if another role had it in M0).
    /// </summary>
    public static int IntroducedIn(string roleKey, string permission) =>
        M0RoleDefaults.TryGetValue(roleKey, out var m0) && m0.Contains(permission) ? 0
        : Math.Max(1, IntroducedIn(permission));
}
