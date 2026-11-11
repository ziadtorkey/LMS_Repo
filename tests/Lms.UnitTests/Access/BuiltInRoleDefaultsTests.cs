using Lms.Infrastructure.Access;
using Lms.Shared.Authorization;

namespace Lms.UnitTests.Access;

/// <summary>Versioning of built-in role defaults: the reconciler adds only defaults introduced after a role's version.</summary>
public class BuiltInRoleDefaultsTests
{
    [Fact]
    public void Current_version_is_2_for_the_M2_defaults()
    {
        Assert.Equal(2, BuiltInRoleDefaults.CurrentVersion);
    }

    [Theory]
    [InlineData(Permissions.OrgUnits.View, 0)]
    [InlineData(Permissions.LevelTypes.View, 1)]
    [InlineData(Permissions.AcademicYears.Delete, 1)]
    [InlineData(Permissions.Staff.View, 2)]
    [InlineData(Permissions.Staff.Import, 2)]
    [InlineData(Permissions.IdentityProviders.Manage, 2)]
    public void Each_permission_has_the_version_that_introduced_it(string permission, int version)
    {
        Assert.Equal(version, BuiltInRoleDefaults.IntroducedIn(permission));
    }

    [Fact]
    public void A_version_1_role_only_receives_the_M2_defaults()
    {
        var added = BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) > 1)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ["IdentityProviders.Manage", "IdentityProviders.View", "Staff.Create", "Staff.Delete", "Staff.Import", "Staff.Update", "Staff.View"],
            added);
    }

    [Fact]
    public void No_permission_is_introduced_after_the_current_version()
    {
        Assert.All(Permissions.All, p => Assert.InRange(BuiltInRoleDefaults.IntroducedIn(p), 0, BuiltInRoleDefaults.CurrentVersion));
    }
}
