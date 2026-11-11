using System.Collections.Immutable;
using System.Reflection;

namespace Lms.Shared.Authorization;

/// <summary>
/// Fixed permission catalog. Values follow "Module.Action" (PRD §3.3).
/// Later milestones append nested classes; never rename an existing value.
/// </summary>
public static class Permissions
{
    public static class OrgUnits
    {
        public const string View = "OrgUnits.View";
        public const string Create = "OrgUnits.Create";
        public const string Update = "OrgUnits.Update";
        public const string Delete = "OrgUnits.Delete";
        public const string Move = "OrgUnits.Move";
    }

    public static class Roles
    {
        public const string View = "Roles.View";
        public const string Create = "Roles.Create";
        public const string Update = "Roles.Update";
        public const string Delete = "Roles.Delete";
    }

    public static class Assignments
    {
        public const string View = "Assignments.View";
        public const string Create = "Assignments.Create";
        public const string Update = "Assignments.Update";
        public const string Delete = "Assignments.Delete";
    }

    public static class Settings
    {
        public const string View = "Settings.View";
        public const string Update = "Settings.Update";
    }

    public static class Policies
    {
        public const string View = "Policies.View";
        public const string Update = "Policies.Update";
    }

    public static class Audit
    {
        public const string View = "Audit.View";
        public const string Export = "Audit.Export";
    }

    public static class Files
    {
        public const string Upload = "Files.Upload";
        public const string Download = "Files.Download";
    }

    /// <summary>Platform Admin only (see <see cref="PlatformOnly"/>).</summary>
    public static class Clients
    {
        public const string View = "Clients.View";
        public const string Create = "Clients.Create";
        public const string Update = "Clients.Update";
    }

    public static class LevelTypes
    {
        public const string View = "LevelTypes.View";
        public const string Create = "LevelTypes.Create";
        public const string Update = "LevelTypes.Update";
        public const string Delete = "LevelTypes.Delete";
    }

    public static class Templates
    {
        public const string View = "Templates.View";
        public const string Create = "Templates.Create";
        public const string Delete = "Templates.Delete";
        public const string Apply = "Templates.Apply";
    }

    public static class AcademicYears
    {
        public const string View = "AcademicYears.View";
        public const string Create = "AcademicYears.Create";
        public const string Update = "AcademicYears.Update";
        public const string Delete = "AcademicYears.Delete";
    }

    // M2 People (D23: the Students.* permissions arrive with the M3 contract).
    public static class Staff
    {
        public const string View = "Staff.View";
        public const string Create = "Staff.Create";
        public const string Update = "Staff.Update";
        public const string Delete = "Staff.Delete";
        public const string Import = "Staff.Import";
    }

    /// <summary>A client's own OpenID Connect / Azure AD sign-in providers (FR-SEC-08, D33).</summary>
    public static class IdentityProviders
    {
        public const string View = "IdentityProviders.View";
        public const string Manage = "IdentityProviders.Manage";
    }

    // M3 Academic Structure (M3 plan Task 1; Students.* deferred from M2 by D23).
    public static class Students
    {
        public const string View = "Students.View";
        public const string Create = "Students.Create";
        public const string Update = "Students.Update";
        public const string Delete = "Students.Delete";
        public const string Import = "Students.Import";
        public const string ApproveChanges = "Students.ApproveChanges";

        /// <summary>A student edits their own non-critical data and requests changes to critical data (FR-STU-04).</summary>
        public const string SelfEdit = "Students.SelfEdit";

        /// <summary>A guardian reads their own children (FR-STU-06).</summary>
        public const string ViewChildren = "Students.ViewChildren";
    }

    public static class Courses
    {
        public const string View = "Courses.View";
        public const string Create = "Courses.Create";
        public const string Update = "Courses.Update";
        public const string Delete = "Courses.Delete";
        public const string Import = "Courses.Import";
        public const string Allocate = "Courses.Allocate";
        public const string ManageSupervisors = "Courses.ManageSupervisors";
    }

    public static class Blueprints
    {
        public const string View = "Blueprints.View";
        public const string Edit = "Blueprints.Edit";
        public const string Publish = "Blueprints.Publish";
    }

    public static class Classes
    {
        public const string View = "Classes.View";
        public const string Create = "Classes.Create";
        public const string Update = "Classes.Update";
        public const string Delete = "Classes.Delete";
        public const string Close = "Classes.Close";
        public const string ManageCourses = "Classes.ManageCourses";
    }

    public static class TeacherAssignments
    {
        public const string View = "TeacherAssignments.View";
        public const string Manage = "TeacherAssignments.Manage";
    }

    public static class Enrollment
    {
        public const string View = "Enrollment.View";
        public const string Manage = "Enrollment.Manage";
        public const string Import = "Enrollment.Import";
        public const string Transfer = "Enrollment.Transfer";
        public const string RequestException = "Enrollment.RequestException";
        public const string ApproveException = "Enrollment.ApproveException";
    }

    public static class Groups
    {
        public const string View = "Groups.View";
        public const string Manage = "Groups.Manage";
    }

    public static class LessonPlans
    {
        public const string View = "LessonPlans.View";
    }

    // Immutable list (not a FrozenSet) so existing consumers typed IReadOnlyList<string> keep compiling;
    // ImmutableArray rejects every mutation through IList<string>/ICollection<string>.
    private static readonly Lazy<IReadOnlyList<string>> AllLazy = new(() =>
        typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToImmutableArray());

    /// <summary>Every permission in the catalog (reflection, cached, immutable).</summary>
    public static IReadOnlyList<string> All => AllLazy.Value;

    /// <summary>
    /// Permissions reserved for the Platform Admin (above all clients). No built-in or custom
    /// client role may hold them (enforced by contract tests and by the roles API).
    /// </summary>
    public static IReadOnlyList<string> PlatformOnly { get; } =
        ImmutableArray.Create(Clients.View, Clients.Create, Clients.Update);

    /// <summary>
    /// Permissions that reach only the holder's own data (own student profile, own children). No staff role holds them,
    /// so the escalation guards (D16) count every caller as holding them: handing them out grants no power over others.
    /// </summary>
    public static IReadOnlyList<string> SelfService { get; } =
        ImmutableArray.Create(Students.SelfEdit, Students.ViewChildren);

    /// <summary>The caller's permissions plus <see cref="SelfService"/>, for the escalation guards.</summary>
    public static IReadOnlySet<string> WithSelfService(IEnumerable<string> callerPermissions) =>
        callerPermissions.Concat(SelfService).ToHashSet(StringComparer.Ordinal);
}
