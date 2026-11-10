using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class AccessIsolationTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Roles_and_role_permissions_are_isolated_per_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "AI1");
        var b = await AccessTestData.SeedAsync(factory, "AI2");

        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));

        Assert.All(await db.Roles.ToListAsync(), r => Assert.Equal(a.Org.Id, r.OrganizationId));
        Assert.All(await db.RolePermissions.ToListAsync(), p => Assert.Equal(a.Org.Id, p.OrganizationId));
        Assert.DoesNotContain(await db.Roles.ToListAsync(), r => b.Roles.Values.Any(x => x.Id == r.Id));
    }

    [Fact]
    public async Task Role_assignments_are_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "AI3");
        var b = await AccessTestData.SeedAsync(factory, "AI4");
        var atRoot = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var atSchool = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var other = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.Manager, Guid.NewGuid());

        await using var rootScope = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        await using var schoolScope = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));
        await using var noScope = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, null));

        var rootIds = await rootScope.RoleAssignments.Select(x => x.Id).ToListAsync();
        Assert.Contains(atRoot.Id, rootIds);
        Assert.Contains(atSchool.Id, rootIds);
        Assert.DoesNotContain(other.Id, rootIds);
        Assert.Equal([atSchool.Id], await schoolScope.RoleAssignments.Select(x => x.Id).ToListAsync());
        Assert.Empty(await noScope.RoleAssignments.ToListAsync());
    }
}