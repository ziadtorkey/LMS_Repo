namespace Lms.Shared.Authorization;

/// <summary>
/// Policies block permissions in a subtree (PRD §4.3). Later milestones add keys
/// (CreateCourses, CreateClasses, CreateStaff, CreateAdmins, CreateStudents, CreateTeachers, ...).
/// </summary>
public static class PolicyCatalog
{
    public const string CreateOrgUnits = "CreateOrgUnits";

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> BlockedPermissions { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [CreateOrgUnits] = [Permissions.OrgUnits.Create],
        };

    /// <summary>Exact policy-block message copy (M0 plan, Global Constraints).</summary>
    public const string BlockedMessageEn = "Blocked by parent organization policy";

    /// <summary>Exact policy-block message copy, Arabic.</summary>
    public const string BlockedMessageAr = "محظور بسياسة الوحدة الأعلى";
}
