using Lms.Infrastructure.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class RoleAssignmentSyncTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Sync_adds_missing_and_removes_extra_built_in_assignments()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RS1");
        var user = Guid.NewGuid();
        var keep = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, user);
        var extra = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Student, user);
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Teacher, user); // other role: untouched

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            await new RoleAssignmentSync(db).SyncBuiltInAsync(
                user, tree.Org.Id, BuiltInRoles.Student, [tree.School1, tree.Root], default);
        }

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var live = await read.RoleAssignments.Where(a => a.UserId == user).ToListAsync();
        var students = live.Where(a => a.RoleId == tree.Roles[BuiltInRoles.Student].Id).ToList();
        Assert.Equal(new[] { tree.Root.Id, tree.School1.Id }.Order(), students.Select(a => a.OrgUnitId).Order());
        Assert.Contains(students, a => a.Id == keep.Id); // kept, not recreated
        Assert.DoesNotContain(live, a => a.Id == extra.Id);
        Assert.Contains(live, a => a.Id == teacher.Id);
    }

    [Fact]
    public async Task Sync_is_idempotent_and_an_empty_set_removes_everything_of_that_role()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RS2");
        var user = Guid.NewGuid();

        for (var i = 0; i < 2; i++)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            await new RoleAssignmentSync(db).SyncBuiltInAsync(user, tree.Org.Id, BuiltInRoles.Guardian, [tree.School1], default);
        }

        await using (var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.Single(await read.RoleAssignments.Where(a => a.UserId == user).ToListAsync());
        }

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            await new RoleAssignmentSync(db).SyncBuiltInAsync(user, tree.Org.Id, BuiltInRoles.Guardian, [], default);
        }

        await using var after = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await after.RoleAssignments.Where(a => a.UserId == user).ToListAsync());
    }

    [Fact]
    public async Task Sync_rejects_units_of_another_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "RS3A");
        var b = await AccessTestData.SeedAsync(factory, "RS3B");
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RoleAssignmentSync(db).SyncBuiltInAsync(Guid.NewGuid(), a.Org.Id, BuiltInRoles.Student, [b.School1], default));
    }
}
