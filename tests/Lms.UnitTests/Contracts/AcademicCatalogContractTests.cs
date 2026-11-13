using Lms.Shared.Authorization;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M3 contract (plan Task 1): relationship rules and the academic notification events.</summary>
public sealed partial class AcademicCatalogContractTests
{
    [Fact]
    public void Every_relationship_rule_names_a_built_in_role_and_permissions_that_role_holds()
    {
        Assert.NotEmpty(RelationshipRules.All);
        Assert.All(RelationshipRules.All, rule =>
        {
            var role = BuiltInRoles.Get(rule.RoleKey);
            Assert.Contains(rule.Relationship, Relationships.All);
            Assert.NotEmpty(rule.Permissions);
            Assert.All(rule.Permissions, p => Assert.Contains(p, role.DefaultPermissions));
        });
        Assert.Equal(RelationshipRules.All.Count, RelationshipRules.All.Select(r => r.RoleKey).Distinct().Count());
    }

    [Fact]
    public void Relationship_rules_match_the_plan()
    {
        Assert.Equal(["OwnClassCourses", "SupervisedCourses", "OwnEnrollments"], Relationships.All);

        Assert.Equal(
            ["Classes.View", "CourseAssignments.Grade", "CourseAssignments.Manage", "CourseAssignments.View",
             "Enrollment.View", "Forums.Moderate", "Forums.Post", "Forums.View", "Groups.Manage", "Groups.View",
             "LessonPlans.View", "LiveSessions.Manage", "LiveSessions.View", "Modules.Manage", "Modules.View", "Packages.Upload", "Packages.View",
             "Quizzes.Grade", "Quizzes.Manage", "Quizzes.View", "Students.View", "TeacherAssignments.View"],
            Rule(BuiltInRoles.Teacher).Permissions.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Blueprints.Edit", "Blueprints.Publish", "Courses.Allocate", "Courses.Delete", "Courses.Update",
             "Enrollment.ApproveException", "Students.View", "TeacherAssignments.Manage"],
            Rule(BuiltInRoles.Supervisor).Permissions.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["CourseAssignments.Submit", "CourseAssignments.View", "Forums.Post", "Forums.View", "LessonPlans.View",
             "LiveSessions.View", "Modules.View", "Packages.View", "Quizzes.Attempt", "Quizzes.View"],
            Rule(BuiltInRoles.Student).Permissions.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(BuiltInRoles.Teacher, Permissions.Groups.Manage, Relationships.OwnClassCourses)]
    [InlineData(BuiltInRoles.Teacher, Permissions.Courses.View, null)]
    [InlineData(BuiltInRoles.Teacher, Permissions.Blueprints.View, null)]
    [InlineData(BuiltInRoles.Supervisor, Permissions.Blueprints.Publish, Relationships.SupervisedCourses)]
    [InlineData(BuiltInRoles.Supervisor, Permissions.Students.View, Relationships.SupervisedCourses)]
    [InlineData(BuiltInRoles.Supervisor, Permissions.Courses.Create, null)]
    [InlineData(BuiltInRoles.Student, Permissions.LessonPlans.View, Relationships.OwnEnrollments)]
    [InlineData(BuiltInRoles.Manager, Permissions.Groups.Manage, null)]
    [InlineData(BuiltInRoles.Teacher, Permissions.Modules.Manage, Relationships.OwnClassCourses)]
    [InlineData(BuiltInRoles.Student, Permissions.Modules.View, Relationships.OwnEnrollments)]
    [InlineData(BuiltInRoles.Supervisor, Permissions.Modules.View, null)]
    [InlineData(BuiltInRoles.Teacher, Permissions.CourseAssignments.Grade, Relationships.OwnClassCourses)]
    [InlineData(BuiltInRoles.Teacher, Permissions.Quizzes.Manage, Relationships.OwnClassCourses)]
    [InlineData(BuiltInRoles.Teacher, Permissions.Forums.Moderate, Relationships.OwnClassCourses)]
    [InlineData(BuiltInRoles.Teacher, Permissions.Library.Manage, null)]
    [InlineData(BuiltInRoles.Student, Permissions.CourseAssignments.Submit, Relationships.OwnEnrollments)]
    [InlineData(BuiltInRoles.Student, Permissions.Quizzes.Attempt, Relationships.OwnEnrollments)]
    [InlineData(BuiltInRoles.Student, Permissions.Forums.Post, Relationships.OwnEnrollments)]
    [InlineData(BuiltInRoles.Student, Permissions.Library.View, null)]
    [InlineData(BuiltInRoles.Manager, Permissions.CourseAssignments.Grade, null)]
    [InlineData(BuiltInRoles.Supervisor, Permissions.Quizzes.View, null)]
    [InlineData(BuiltInRoles.Teacher, Permissions.LiveSessions.Manage, Relationships.OwnClassCourses)]
    [InlineData(BuiltInRoles.Teacher, Permissions.LiveSessions.View, Relationships.OwnClassCourses)]
    [InlineData(BuiltInRoles.Student, Permissions.LiveSessions.View, Relationships.OwnEnrollments)]
    [InlineData(BuiltInRoles.Supervisor, Permissions.LiveSessions.View, null)]
    [InlineData(BuiltInRoles.Manager, Permissions.LiveSessions.Manage, null)]
    [InlineData("custom-role", Permissions.Groups.Manage, null)]
    public void For_returns_the_relationship_or_null(string role, string permission, string? expected) =>
        Assert.Equal(expected, RelationshipRules.For(role, permission));

    [Fact]
    public void Relationship_rule_lists_are_immutable() =>
        Assert.All(RelationshipRules.All, r =>
        {
            var asList = Assert.IsAssignableFrom<IList<string>>(r.Permissions);
            Assert.Throws<NotSupportedException>(() => asList.Add("Hacked"));
        });

    [Fact]
    public void New_notification_events_have_unique_keys_and_valid_recipient_scope()
    {
        var builtIn = BuiltInRoles.All.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        string[] m3 =
        [
            NotificationEvents.StudentsChangeRequested, NotificationEvents.StudentsChangeRequestDecided,
            NotificationEvents.TeachingAssigned, NotificationEvents.BlueprintsPushed,
            NotificationEvents.EnrollmentExceptionRequested, NotificationEvents.EnrollmentExceptionDecided,
            NotificationEvents.EnrollmentStudentTransferred,
        ];

        Assert.Equal(m3.Length, m3.Distinct(StringComparer.Ordinal).Count());
        Assert.All(m3, key =>
        {
            var e = NotificationEvents.Find(key);
            Assert.NotNull(e);
            Assert.Contains(e.RecipientScope, RecipientScopes.All);
            Assert.All(e.DefaultRecipientRoles, r => Assert.Contains(r, builtIn));
            Assert.Equal(e.Parameters.Count, e.Parameters.Distinct(StringComparer.Ordinal).Count());
        });

        var requested = NotificationEvents.Find(NotificationEvents.StudentsChangeRequested)!;
        Assert.Equal([BuiltInRoles.OrgAdmin, BuiltInRoles.Manager, BuiltInRoles.Counselor], requested.DefaultRecipientRoles);
        Assert.Equal(RecipientScopes.AncestorsAndSelf, requested.RecipientScope);
        var exception = NotificationEvents.Find(NotificationEvents.EnrollmentExceptionRequested)!;
        Assert.Equal([BuiltInRoles.Manager], exception.DefaultRecipientRoles);
        Assert.Equal(RecipientScopes.AncestorsAndSelf, exception.RecipientScope);
        Assert.Equal(["ClassNameAr", "ClassNameEn", "CourseNameAr", "CourseNameEn", "Role"],
            NotificationEvents.Find(NotificationEvents.TeachingAssigned)!.Parameters);
        Assert.Equal(["CourseNameAr", "CourseNameEn", "Version"], NotificationEvents.Find(NotificationEvents.BlueprintsPushed)!.Parameters);
        Assert.Equal(
            ["StudentNameAr", "StudentNameEn", "FromClassAr", "FromClassEn", "ToClassAr", "ToClassEn", "EffectiveOn"],
            NotificationEvents.Find(NotificationEvents.EnrollmentStudentTransferred)!.Parameters);
        Assert.Contains("Decision", NotificationEvents.Find(NotificationEvents.EnrollmentExceptionDecided)!.Parameters);
        Assert.Contains("Decision", NotificationEvents.Find(NotificationEvents.StudentsChangeRequestDecided)!.Parameters);
    }

    private static RelationshipRule Rule(string role) => RelationshipRules.All.Single(r => r.RoleKey == role);
}
