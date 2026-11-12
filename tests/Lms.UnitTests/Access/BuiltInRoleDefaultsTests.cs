using Lms.Infrastructure.Access;
using Lms.Shared.Authorization;

namespace Lms.UnitTests.Access;

/// <summary>Versioning of built-in role defaults: the reconciler adds only defaults introduced after a role's version.</summary>
public class BuiltInRoleDefaultsTests
{
    [Fact]
    public void Current_version_is_5_for_the_M4a_wave_2_defaults()
    {
        Assert.Equal(5, BuiltInRoleDefaults.CurrentVersion);
    }

    [Theory]
    [InlineData(Permissions.OrgUnits.View, 0)]
    [InlineData(Permissions.LevelTypes.View, 1)]
    [InlineData(Permissions.AcademicYears.Delete, 1)]
    [InlineData(Permissions.Staff.View, 2)]
    [InlineData(Permissions.Staff.Import, 2)]
    [InlineData(Permissions.IdentityProviders.Manage, 2)]
    [InlineData(Permissions.Students.View, 3)]
    [InlineData(Permissions.Students.ViewChildren, 3)]
    [InlineData(Permissions.Courses.ManageSupervisors, 3)]
    [InlineData(Permissions.Blueprints.Publish, 3)]
    [InlineData(Permissions.LessonPlans.View, 3)]
    [InlineData(Permissions.Modules.View, 4)]
    [InlineData(Permissions.Modules.Manage, 4)]
    [InlineData(Permissions.CourseAssignments.Submit, 5)]
    [InlineData(Permissions.CourseAssignments.Grade, 5)]
    [InlineData(Permissions.Quizzes.Attempt, 5)]
    [InlineData(Permissions.Library.Manage, 5)]
    [InlineData(Permissions.Forums.Moderate, 5)]
    public void Each_permission_has_the_version_that_introduced_it(string permission, int version)
    {
        Assert.Equal(version, BuiltInRoleDefaults.IntroducedIn(permission));
    }

    [Fact]
    public void A_version_1_role_only_receives_the_M2_M3_and_M4a_defaults()
    {
        var added = BuiltInRoles.Get(BuiltInRoles.Counselor).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 1)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ["Classes.View", "Enrollment.RequestException", "Enrollment.View", "Groups.View", "Library.View",
             "Staff.View", "Students.ApproveChanges", "Students.Update", "Students.View"],
            added);
    }

    [Fact]
    public void A_version_2_role_receives_the_M3_and_M4a_defaults()
    {
        var added = BuiltInRoles.Get(BuiltInRoles.Guardian).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 2)
            .ToList();
        var orgAdmin = BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 2)
            .ToList();

        Assert.Equal(["Students.ViewChildren"], added);
        Assert.Equal(46, orgAdmin.Count);
        Assert.Equal(33, orgAdmin.Count(p => BuiltInRoleDefaults.IntroducedIn(p) == 3));
        Assert.Equal(11, orgAdmin.Count(p => BuiltInRoleDefaults.IntroducedIn(p) == 5));
        Assert.DoesNotContain(orgAdmin, p => p.StartsWith("Staff.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_version_3_role_receives_both_M4a_waves()
    {
        string[] Added(string role) => BuiltInRoles.Get(role).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 3)
            .Where(p => p.StartsWith("Modules.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Modules.Manage", "Modules.View"], Added(BuiltInRoles.OrgAdmin));
        Assert.Equal(["Modules.Manage", "Modules.View"], Added(BuiltInRoles.Manager));
        Assert.Equal(["Modules.View"], Added(BuiltInRoles.Supervisor));
        Assert.Equal(["Modules.Manage", "Modules.View"], Added(BuiltInRoles.Teacher));
        Assert.Equal(["Modules.View"], Added(BuiltInRoles.Student));
        Assert.Empty(Added(BuiltInRoles.Counselor));
        Assert.Empty(Added(BuiltInRoles.Guardian));
        Assert.Equal(13, BuiltInRoles.Get(BuiltInRoles.Teacher).DefaultPermissions.Count(p => BuiltInRoleDefaults.IntroducedIn(p) > 3));
    }

    [Fact]
    public void A_version_4_role_only_receives_the_M4a_wave_2_defaults()
    {
        string[] Added(string role) => BuiltInRoles.Get(role).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 4)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] staffSide =
        [
            "CourseAssignments.Grade", "CourseAssignments.Manage", "CourseAssignments.View", "Forums.Moderate",
            "Forums.Post", "Forums.View", "Library.Manage", "Library.View", "Quizzes.Grade", "Quizzes.Manage",
            "Quizzes.View",
        ];
        Assert.Equal(staffSide, Added(BuiltInRoles.OrgAdmin));
        Assert.Equal(staffSide, Added(BuiltInRoles.Manager));
        Assert.Equal(staffSide, Added(BuiltInRoles.Teacher));
        Assert.Equal(
            ["CourseAssignments.View", "Forums.View", "Library.Manage", "Library.View", "Quizzes.View"],
            Added(BuiltInRoles.Supervisor));
        Assert.Equal(["Library.View"], Added(BuiltInRoles.Counselor));
        Assert.Equal(
            ["CourseAssignments.Submit", "CourseAssignments.View", "Forums.Post", "Forums.View", "Library.View",
             "Quizzes.Attempt", "Quizzes.View"],
            Added(BuiltInRoles.Student));
        Assert.Empty(Added(BuiltInRoles.Guardian));
    }

    [Fact]
    public void Every_M4a_wave_1_permission_is_introduced_in_version_4() =>
        Assert.All(Permissions.All.Where(p => p.StartsWith("Modules.", StringComparison.Ordinal)),
            p => Assert.Equal(4, BuiltInRoleDefaults.IntroducedIn(p)));

    [Fact]
    public void Every_M4a_wave_2_permission_is_introduced_in_version_5()
    {
        string[] wave2 = ["CourseAssignments", "Quizzes", "Library", "Forums"];
        var permissions = Permissions.All.Where(p => wave2.Contains(p[..p.IndexOf('.')])).ToList();

        Assert.Equal(13, permissions.Count);
        Assert.All(permissions, p => Assert.Equal(5, BuiltInRoleDefaults.IntroducedIn(p)));
    }

    [Fact]
    public void Every_M3_permission_is_introduced_in_version_3()
    {
        string[] m3Modules = ["Students", "Courses", "Blueprints", "Classes", "TeacherAssignments", "Enrollment", "Groups", "LessonPlans"];

        Assert.All(Permissions.All.Where(p => m3Modules.Contains(p[..p.IndexOf('.')])),
            p => Assert.Equal(3, BuiltInRoleDefaults.IntroducedIn(p)));
    }

    [Fact]
    public void No_permission_is_introduced_after_the_current_version()
    {
        Assert.All(Permissions.All, p => Assert.InRange(BuiltInRoleDefaults.IntroducedIn(p), 0, BuiltInRoleDefaults.CurrentVersion));
    }

    [Theory]
    [InlineData(BuiltInRoles.OrgAdmin, Permissions.Assignments.Create, 0)]
    [InlineData(BuiltInRoles.Manager, Permissions.Assignments.View, 0)]
    [InlineData(BuiltInRoles.Manager, Permissions.Assignments.Create, 1)] // existed in M0, but only D16 gave it to the Manager
    [InlineData(BuiltInRoles.Manager, Permissions.Assignments.Delete, 1)]
    [InlineData(BuiltInRoles.Manager, Permissions.LevelTypes.View, 1)]
    [InlineData(BuiltInRoles.Manager, Permissions.Staff.Create, 2)]
    [InlineData(BuiltInRoles.Teacher, Permissions.Files.Upload, 0)]
    [InlineData(BuiltInRoles.Guardian, Permissions.Files.Upload, 1)]
    public void Introduced_version_is_judged_against_the_roles_own_M0_list(string role, string permission, int version)
    {
        Assert.Equal(version, BuiltInRoleDefaults.IntroducedIn(role, permission));
    }

    [Fact]
    public void The_frozen_M0_Manager_list_is_the_one_M0_clients_were_provisioned_with()
    {
        Assert.Equal(
            new[]
            {
                Permissions.OrgUnits.View, Permissions.Roles.View, Permissions.Assignments.View, Permissions.Settings.View,
                Permissions.Policies.View, Permissions.Audit.View, Permissions.Files.Upload, Permissions.Files.Download,
            }.Order(),
            BuiltInRoleDefaults.M0RoleDefaults[BuiltInRoles.Manager].Order());
        Assert.All(BuiltInRoles.All, role => Assert.True(BuiltInRoleDefaults.M0RoleDefaults.ContainsKey(role.Key)));
    }
}
