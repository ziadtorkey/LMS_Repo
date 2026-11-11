using Lms.Infrastructure.Access;
using Lms.Shared.Authorization;

namespace Lms.UnitTests.Access;

/// <summary>Versioning of built-in role defaults: the reconciler adds only defaults introduced after a role's version.</summary>
public class BuiltInRoleDefaultsTests
{
    [Fact]
    public void Current_version_is_3_for_the_M3_defaults()
    {
        Assert.Equal(3, BuiltInRoleDefaults.CurrentVersion);
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
    public void Each_permission_has_the_version_that_introduced_it(string permission, int version)
    {
        Assert.Equal(version, BuiltInRoleDefaults.IntroducedIn(permission));
    }

    [Fact]
    public void A_version_1_role_only_receives_the_M2_and_M3_defaults()
    {
        var added = BuiltInRoles.Get(BuiltInRoles.Counselor).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 1)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ["Classes.View", "Enrollment.RequestException", "Enrollment.View", "Groups.View", "Staff.View",
             "Students.ApproveChanges", "Students.Update", "Students.View"],
            added);
    }

    [Fact]
    public void A_version_2_role_only_receives_the_M3_defaults()
    {
        var added = BuiltInRoles.Get(BuiltInRoles.Guardian).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 2)
            .ToList();
        var orgAdmin = BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 2)
            .ToList();

        Assert.Equal(["Students.ViewChildren"], added);
        Assert.Equal(33, orgAdmin.Count);
        Assert.DoesNotContain(orgAdmin, p => p.StartsWith("Staff.", StringComparison.Ordinal));
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
}
