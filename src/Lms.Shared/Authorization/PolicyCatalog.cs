using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Lms.Shared.Authorization;

/// <summary>
/// Policies block permissions in a subtree (PRD §4.3). Later milestones add keys
/// (CreateCourses, CreateClasses, CreateStaff, CreateAdmins, CreateStudents, CreateTeachers, ...).
/// </summary>
public static class PolicyCatalog
{
    public const string CreateOrgUnits = "CreateOrgUnits";

    /// <summary>Policy key → permissions it blocks. Immutable (FrozenDictionary of immutable lists).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BlockedPermissions { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [CreateOrgUnits] = ImmutableArray.Create(Permissions.OrgUnits.Create),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Exact policy-block message copy (M0 plan, Global Constraints).</summary>
    public const string BlockedMessageEn = "Blocked by parent organization policy";

    /// <summary>Exact policy-block message copy, Arabic.</summary>
    public const string BlockedMessageAr = "محظور بسياسة الوحدة الأعلى";
}
