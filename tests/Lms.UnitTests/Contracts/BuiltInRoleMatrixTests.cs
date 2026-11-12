using Lms.Shared.Authorization;

namespace Lms.UnitTests.Contracts;

/// <summary>Pins the M4a built-in role matrix (PRD §3.4, M1 plan Task 1, D16, M2 plan Task 1, D23, D32, M3 plan Task 1, D46, M4a plan Task 1). Every list is written out literally.</summary>
public class BuiltInRoleMatrixTests
{
    private static readonly string[] M0OrgAdmin =
    [
        "OrgUnits.View", "OrgUnits.Create", "OrgUnits.Update", "OrgUnits.Delete",
        "Roles.View", "Roles.Create", "Roles.Update", "Roles.Delete",
        "Assignments.View", "Assignments.Create", "Assignments.Delete",
        "Settings.View", "Settings.Update", "Policies.View", "Policies.Update",
        "Audit.View", "Audit.Export", "Files.Upload", "Files.Download",
    ];

    public static TheoryData<string, string[]> Matrix => new()
    {
        {
            "OrgAdmin",
            [
                .. M0OrgAdmin,
                "LevelTypes.View", "LevelTypes.Create", "LevelTypes.Update", "LevelTypes.Delete",
                "OrgUnits.Move",
                "Templates.View", "Templates.Create", "Templates.Delete", "Templates.Apply",
                "Assignments.Update",
                "AcademicYears.View", "AcademicYears.Create", "AcademicYears.Update", "AcademicYears.Delete",
                "Staff.View", "Staff.Create", "Staff.Update", "Staff.Delete", "Staff.Import",
                "IdentityProviders.View", "IdentityProviders.Manage",
                "Students.View", "Students.Create", "Students.Update", "Students.Delete", "Students.Import",
                "Students.ApproveChanges",
                "Courses.View", "Courses.Create", "Courses.Update", "Courses.Delete", "Courses.Import",
                "Courses.Allocate", "Courses.ManageSupervisors",
                "Blueprints.View", "Blueprints.Edit", "Blueprints.Publish",
                "Classes.View", "Classes.Create", "Classes.Update", "Classes.Delete", "Classes.Close",
                "Classes.ManageCourses",
                "TeacherAssignments.View", "TeacherAssignments.Manage",
                "Enrollment.View", "Enrollment.Manage", "Enrollment.Import", "Enrollment.Transfer",
                "Enrollment.RequestException", "Enrollment.ApproveException",
                "Groups.View", "Groups.Manage", "LessonPlans.View",
                "Modules.View", "Modules.Manage",
            ]
        },
        {
            "Manager",
            [
                "OrgUnits.View", "Roles.View", "Assignments.View", "Settings.View", "Policies.View",
                "Audit.View", "Files.Upload", "Files.Download",
                "LevelTypes.View", "Templates.View", "AcademicYears.View", "Assignments.Create", "Assignments.Delete",
                "Staff.View", "Staff.Create",
                "Students.View", "Students.Create", "Students.Update", "Students.Delete", "Students.ApproveChanges",
                "Courses.View", "Courses.Create", "Courses.Update", "Courses.Delete", "Courses.Allocate",
                "Courses.ManageSupervisors",
                "Blueprints.View", "Blueprints.Edit", "Blueprints.Publish",
                "Classes.View", "Classes.Create", "Classes.Update", "Classes.Delete", "Classes.Close",
                "Classes.ManageCourses",
                "TeacherAssignments.View", "TeacherAssignments.Manage",
                "Enrollment.View", "Enrollment.Manage", "Enrollment.Transfer", "Enrollment.RequestException",
                "Enrollment.ApproveException",
                "Groups.View", "Groups.Manage", "LessonPlans.View",
                "Modules.View", "Modules.Manage",
            ]
        },
        {
            "Supervisor",
            [
                "OrgUnits.View", "Settings.View", "Policies.View", "Files.Upload", "Files.Download",
                "LevelTypes.View", "Templates.View", "AcademicYears.View", "Staff.View",
                "Students.View",
                "Courses.View", "Courses.Create", "Courses.Update", "Courses.Delete", "Courses.Allocate",
                "Blueprints.View", "Blueprints.Edit", "Blueprints.Publish",
                "Classes.View", "TeacherAssignments.View", "TeacherAssignments.Manage",
                "Enrollment.View", "Enrollment.ApproveException", "Groups.View", "LessonPlans.View",
                "Modules.View",
            ]
        },
        {
            "Counselor",
            [
                "Files.Upload", "Files.Download", "AcademicYears.View", "Staff.View",
                "Students.View", "Students.Update", "Students.ApproveChanges", "Classes.View",
                "Enrollment.View", "Enrollment.RequestException", "Groups.View",
            ]
        },
        {
            "Teacher",
            [
                "Files.Upload", "Files.Download", "AcademicYears.View",
                "Students.View", "Courses.View", "Blueprints.View", "Classes.View", "TeacherAssignments.View",
                "Enrollment.View", "Groups.View", "Groups.Manage", "LessonPlans.View",
                "Modules.View", "Modules.Manage",
            ]
        },
        { "Student", ["Files.Upload", "Files.Download", "AcademicYears.View", "Students.SelfEdit", "LessonPlans.View",
            "Modules.View"] },
        { "Guardian", ["Files.Download", "AcademicYears.View", "Students.ViewChildren"] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Built_in_roles_match_the_M4a_matrix_exactly(string roleKey, string[] expected)
    {
        var actual = BuiltInRoles.Get(roleKey).DefaultPermissions;

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.Equal(actual.Count, actual.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Matrix_covers_every_built_in_role()
    {
        Assert.Equal(
            BuiltInRoles.All.Select(r => r.Key).Order(StringComparer.Ordinal),
            Matrix.Select(row => (string)row[0]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_built_in_role_holds_a_platform_only_permission()
    {
        Assert.NotEmpty(Permissions.PlatformOnly);
        Assert.All(BuiltInRoles.All, r =>
            Assert.Empty(r.DefaultPermissions.Intersect(Permissions.PlatformOnly, StringComparer.Ordinal)));
    }

    [Fact]
    public void Every_permission_is_held_by_some_built_in_role_or_is_platform_only()
    {
        var held = BuiltInRoles.All.SelectMany(r => r.DefaultPermissions).ToHashSet(StringComparer.Ordinal);

        Assert.All(Permissions.All, p =>
            Assert.True(held.Contains(p) || Permissions.PlatformOnly.Contains(p), $"{p} is held by no role and is not PlatformOnly."));
    }

    [Fact]
    public void PlatformOnly_is_exactly_Clients_View_Create_Update()
    {
        Assert.Equal(
            new[] { "Clients.Create", "Clients.Update", "Clients.View" },
            Permissions.PlatformOnly.Order(StringComparer.Ordinal));
        Assert.All(Permissions.PlatformOnly, p => Assert.Contains(p, Permissions.All));
    }
}
