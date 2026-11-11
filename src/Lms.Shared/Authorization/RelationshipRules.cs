using System.Collections.Immutable;

namespace Lms.Shared.Authorization;

/// <summary>Relationships that restrict a built-in role's permission to its own resources (PRD §3.3 step 2).</summary>
public static class Relationships
{
    /// <summary>ClassCourses where the caller holds a current teacher assignment (or classes where they are homeroom teacher).</summary>
    public const string OwnClassCourses = "OwnClassCourses";

    /// <summary>Courses where the caller is a live course supervisor.</summary>
    public const string SupervisedCourses = "SupervisedCourses";

    /// <summary>The caller's own active enrollments.</summary>
    public const string OwnEnrollments = "OwnEnrollments";

    public static IReadOnlyList<string> All { get; } = [OwnClassCourses, SupervisedCourses, OwnEnrollments];
}

/// <summary>A built-in role whose listed permissions apply only to resources reached through <paramref name="Relationship"/>.</summary>
public sealed record RelationshipRule(string RoleKey, string Relationship, IReadOnlyList<string> Permissions);

/// <summary>
/// Relationship rules (PRD §3.3 step 2, M3 plan Task 12). Evaluated after the permission, scope and policy checks.
/// Custom roles and built-in roles without a rule are unrestricted inside their unit scope.
/// </summary>
public static class RelationshipRules
{
    public static IReadOnlyList<RelationshipRule> All { get; } =
    [
        // Courses.View and Blueprints.View stay unit-wide: the catalog and the blueprints are not secret inside the unit.
        new(BuiltInRoles.Teacher, Relationships.OwnClassCourses, ImmutableArray.Create(
            Permissions.Students.View, Permissions.Classes.View, Permissions.TeacherAssignments.View,
            Permissions.Enrollment.View, Permissions.Groups.View, Permissions.Groups.Manage,
            Permissions.LessonPlans.View)),
        new(BuiltInRoles.Supervisor, Relationships.SupervisedCourses, ImmutableArray.Create(
            Permissions.Courses.Update, Permissions.Courses.Delete, Permissions.Courses.Allocate,
            Permissions.Blueprints.Edit, Permissions.Blueprints.Publish, Permissions.TeacherAssignments.Manage,
            Permissions.Enrollment.ApproveException)),
        new(BuiltInRoles.Student, Relationships.OwnEnrollments, ImmutableArray.Create(Permissions.LessonPlans.View)),
    ];

    /// <summary>The relationship that restricts <paramref name="permission"/> for the built-in role, or null (unrestricted).</summary>
    public static string? For(string roleKey, string permission) =>
        All.FirstOrDefault(r => string.Equals(r.RoleKey, roleKey, StringComparison.Ordinal)
            && r.Permissions.Contains(permission, StringComparer.Ordinal))?.Relationship;
}
