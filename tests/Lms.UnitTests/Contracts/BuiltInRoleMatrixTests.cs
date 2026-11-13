using Lms.Shared.Authorization;

namespace Lms.UnitTests.Contracts;

/// <summary>Pins the M4a built-in role matrix (PRD §3.4, M1 plan Task 1, D16, M2 plan Task 1, D23, D32, M3 plan Task 1, D46, M4a plan Tasks 1 and 2, D52, M4b plan Task 1). Every list is written out literally.</summary>
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
                "CourseAssignments.View", "CourseAssignments.Manage", "CourseAssignments.Grade",
                "Quizzes.View", "Quizzes.Manage", "Quizzes.Grade",
                "Library.View", "Library.Manage",
                "Forums.View", "Forums.Post", "Forums.Moderate",
                "LiveSessions.View", "LiveSessions.Manage",
                "Packages.Upload", "Packages.View",
                "Grades.View", "Grades.Manage", "Grades.Publish", "Grades.ConfigureScales", "Grades.ViewReports",
                "Attendance.Take", "Attendance.Edit", "Attendance.View", "Excuses.Review", "Excuses.Escalate",
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
                "CourseAssignments.View", "CourseAssignments.Manage", "CourseAssignments.Grade",
                "Quizzes.View", "Quizzes.Manage", "Quizzes.Grade",
                "Library.View", "Library.Manage",
                "Forums.View", "Forums.Post", "Forums.Moderate",
                "LiveSessions.View", "LiveSessions.Manage",
                "Packages.Upload", "Packages.View",
                "Grades.View", "Grades.Manage", "Grades.Publish", "Grades.ConfigureScales", "Grades.ViewReports",
                "Attendance.Take", "Attendance.Edit", "Attendance.View", "Excuses.Review", "Excuses.Escalate",
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
                "CourseAssignments.View", "Quizzes.View", "Library.View", "Library.Manage", "Forums.View",
                "LiveSessions.View", "Packages.View",
                "Grades.View", "Grades.ViewReports", "Attendance.View",
            ]
        },
        {
            "Counselor",
            [
                "Files.Upload", "Files.Download", "AcademicYears.View", "Staff.View",
                "Students.View", "Students.Update", "Students.ApproveChanges", "Classes.View",
                "Enrollment.View", "Enrollment.RequestException", "Groups.View",
                "Library.View",
                "Attendance.View", "Attendance.Edit", "Excuses.Review", "Excuses.Escalate",
            ]
        },
        {
            "Teacher",
            [
                "Files.Upload", "Files.Download", "AcademicYears.View",
                "Students.View", "Courses.View", "Blueprints.View", "Classes.View", "TeacherAssignments.View",
                "Enrollment.View", "Groups.View", "Groups.Manage", "LessonPlans.View",
                "Modules.View", "Modules.Manage",
                "CourseAssignments.View", "CourseAssignments.Manage", "CourseAssignments.Grade",
                "Quizzes.View", "Quizzes.Manage", "Quizzes.Grade",
                "Library.View", "Library.Manage",
                "Forums.View", "Forums.Post", "Forums.Moderate",
                "LiveSessions.View", "LiveSessions.Manage",
                "Packages.Upload", "Packages.View",
                "Grades.View", "Grades.Manage", "Grades.Publish", "Attendance.Take",
            ]
        },
        { "Student", ["Files.Upload", "Files.Download", "AcademicYears.View", "Students.SelfEdit", "LessonPlans.View",
            "Modules.View", "CourseAssignments.View", "CourseAssignments.Submit", "Quizzes.View", "Quizzes.Attempt",
            "Library.View", "Forums.View", "Forums.Post", "LiveSessions.View", "Packages.View", "Grades.ViewOwn", "Attendance.ViewOwn", "Excuses.Submit"] },
        { "Guardian", ["Files.Download", "AcademicYears.View", "Students.ViewChildren", "Grades.ViewOwn", "Attendance.ViewOwn", "Excuses.Submit"] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Built_in_roles_match_the_M5_wave_3_matrix_exactly(string roleKey, string[] expected)
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
    public void No_staff_role_holds_a_self_service_permission()
    {
        string[] staff = [BuiltInRoles.OrgAdmin, BuiltInRoles.Manager, BuiltInRoles.Supervisor, BuiltInRoles.Counselor,
            BuiltInRoles.Teacher];

        Assert.All(staff, r =>
            Assert.Empty(BuiltInRoles.Get(r).DefaultPermissions.Intersect(Permissions.SelfService, StringComparer.Ordinal)));
    }

    [Fact]
    public void Org_admin_and_manager_hold_the_staff_side_of_every_activity_D52()
    {
        string[] staffSide =
        [
            "Modules.View", "Modules.Manage", "CourseAssignments.View", "CourseAssignments.Manage",
            "CourseAssignments.Grade", "Quizzes.View", "Quizzes.Manage", "Quizzes.Grade", "Library.View",
            "Library.Manage", "Forums.View", "Forums.Post", "Forums.Moderate", "LiveSessions.View",
            "LiveSessions.Manage", "Packages.Upload", "Packages.View",
        ];

        Assert.All(new[] { BuiltInRoles.OrgAdmin, BuiltInRoles.Manager }, r =>
            Assert.Empty(staffSide.Except(BuiltInRoles.Get(r).DefaultPermissions, StringComparer.Ordinal)));
        // ...so the Teacher role stays a subset the Manager can hand out (D16 escalation guard).
        Assert.Empty(BuiltInRoles.Get(BuiltInRoles.Teacher).DefaultPermissions
            .Except(BuiltInRoles.Get(BuiltInRoles.Manager).DefaultPermissions, StringComparer.Ordinal));
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
