using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Lms.Shared.Authorization;

/// <summary>
/// Policies block permissions or role assignments in a subtree (PRD §4.3, D28). Later milestones add keys
/// (CreateCourses, CreateClasses, CreateStudents, ...). Every key is in <see cref="BlockedPermissions"/> (possibly with
/// an empty list); a key that blocks roles is also in <see cref="BlockedRoles"/>.
/// </summary>
public static class PolicyCatalog
{
    public const string CreateOrgUnits = "CreateOrgUnits";

    /// <summary>Blocks creating and importing staff (D28).</summary>
    public const string CreateStaff = "CreateStaff";

    /// <summary>Blocks assigning the Teacher role on every path that creates an assignment (D28).</summary>
    public const string CreateTeachers = "CreateTeachers";

    /// <summary>
    /// Blocks assigning Org Admin, Manager and any custom role holding an <see cref="AdminMarkerPermissions"/> entry,
    /// on every path that creates an assignment (D28).
    /// </summary>
    public const string CreateAdmins = "CreateAdmins";

    /// <summary>Blocks creating and importing students (§4.3, M3).</summary>
    public const string CreateStudents = "CreateStudents";

    /// <summary>Blocks creating and importing courses (§4.3, M3).</summary>
    public const string CreateCourses = "CreateCourses";

    /// <summary>Blocks creating classes (§4.3, M3).</summary>
    public const string CreateClasses = "CreateClasses";

    /// <summary>Policy key → permissions it blocks. Immutable (FrozenDictionary of immutable lists).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BlockedPermissions { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [CreateOrgUnits] = ImmutableArray.Create(Permissions.OrgUnits.Create),
            [CreateStaff] = ImmutableArray.Create(Permissions.Staff.Create, Permissions.Staff.Import),
            [CreateTeachers] = ImmutableArray<string>.Empty,
            [CreateAdmins] = ImmutableArray<string>.Empty,
            [CreateStudents] = ImmutableArray.Create(Permissions.Students.Create, Permissions.Students.Import),
            [CreateCourses] = ImmutableArray.Create(Permissions.Courses.Create, Permissions.Courses.Import),
            [CreateClasses] = ImmutableArray.Create(Permissions.Classes.Create),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Policy key → built-in role keys (<see cref="BuiltInRoles"/>) it blocks from being assigned in the subtree.
    /// Only role-blocking keys are present. Immutable.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BlockedRoles { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [CreateTeachers] = ImmutableArray.Create(BuiltInRoles.Teacher),
            [CreateAdmins] = ImmutableArray.Create(BuiltInRoles.OrgAdmin, BuiltInRoles.Manager),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// A custom role holding any of these permissions counts as an admin role for <see cref="CreateAdmins"/>
    /// (it can assign or edit roles, D28).
    /// </summary>
    public static IReadOnlyList<string> AdminMarkerPermissions { get; } =
        ImmutableArray.Create(Permissions.Assignments.Create, Permissions.Roles.Update);

    /// <summary>Exact policy-block message copy (M0 plan, Global Constraints).</summary>
    public const string BlockedMessageEn = "Blocked by parent organization policy";

    /// <summary>Exact policy-block message copy, Arabic.</summary>
    public const string BlockedMessageAr = "محظور بسياسة الوحدة الأعلى";
}
