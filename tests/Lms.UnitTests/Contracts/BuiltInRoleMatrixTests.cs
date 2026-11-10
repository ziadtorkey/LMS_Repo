using Lms.Shared.Authorization;

namespace Lms.UnitTests.Contracts;

/// <summary>Pins the M1 built-in role matrix (PRD §3.4, M1 plan Task 1, D16). Every list is written out literally.</summary>
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
            ]
        },
        {
            "Manager",
            [
                "OrgUnits.View", "Roles.View", "Assignments.View", "Settings.View", "Policies.View",
                "Audit.View", "Files.Upload", "Files.Download",
                "LevelTypes.View", "Templates.View", "AcademicYears.View", "Assignments.Create", "Assignments.Delete",
            ]
        },
        {
            "Supervisor",
            [
                "OrgUnits.View", "Settings.View", "Policies.View", "Files.Upload", "Files.Download",
                "LevelTypes.View", "Templates.View", "AcademicYears.View",
            ]
        },
        { "Counselor", ["Files.Upload", "Files.Download", "AcademicYears.View"] },
        { "Teacher", ["Files.Upload", "Files.Download", "AcademicYears.View"] },
        { "Student", ["Files.Upload", "Files.Download", "AcademicYears.View"] },
        { "Guardian", ["Files.Download", "AcademicYears.View"] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Built_in_roles_match_the_M1_matrix_exactly(string roleKey, string[] expected)
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
