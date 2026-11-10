using Lms.Domain.Organizations;
using Lms.Infrastructure.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class DefaultRoleProvisionerTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Provisioner_creates_seven_built_in_roles_with_labels_per_type()
    {
        var school = await AccessTestData.SeedAsync(factory, "P1", OrganizationType.SchoolGroup);
        var university = await AccessTestData.SeedAsync(factory, "P2", OrganizationType.University);

        Assert.Equal(7, school.Roles.Count);
        Assert.Equal(BuiltInRoles.All.Select(r => r.Key).Order(), school.Roles.Keys.Order());
        Assert.All(school.Roles.Values, r => Assert.True(r.IsBuiltIn));
        Assert.Equal("School Manager", school.Roles[BuiltInRoles.Manager].NameEn);
        Assert.Equal("Dean", university.Roles[BuiltInRoles.Manager].NameEn);
        Assert.Equal("الطالب", university.Roles[BuiltInRoles.Student].NameAr);
        Assert.Equal(RoleBaseKinds.Guardian, school.Roles[BuiltInRoles.Guardian].BaseKind.ToString());
    }

    [Fact]
    public async Task Provisioned_roles_carry_their_default_permissions()
    {
        var tree = await AccessTestData.SeedAsync(factory, "P3");

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var admin = await db.RolePermissions.Where(p => p.RoleId == tree.Roles[BuiltInRoles.OrgAdmin].Id)
            .Select(p => p.Permission).ToListAsync();
        var guardian = await db.RolePermissions.Where(p => p.RoleId == tree.Roles[BuiltInRoles.Guardian].Id)
            .Select(p => p.Permission).ToListAsync();

        // The provisioner copies the contract's explicit lists (bug #21, M1 matrix); never "all permissions".
        Assert.Equal(BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions.Order(), admin.Order());
        Assert.Equal(BuiltInRoles.Get(BuiltInRoles.Guardian).DefaultPermissions.Order(), guardian.Order());
        Assert.DoesNotContain(admin, p => Permissions.PlatformOnly.Contains(p));
    }

    [Fact]
    public async Task Provisioning_twice_does_not_duplicate_roles()
    {
        var tree = await AccessTestData.SeedAsync(factory, "P4");

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        await new DefaultRoleProvisioner(db).ProvisionAsync(tree.Org.Id, OrganizationType.SchoolGroup, CancellationToken.None);

        Assert.Equal(7, await db.Roles.CountAsync(r => r.OrganizationId == tree.Org.Id));
    }
}