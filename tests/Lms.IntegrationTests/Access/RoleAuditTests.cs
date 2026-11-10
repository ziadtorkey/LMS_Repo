using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class RoleAuditTests(LmsApiFactory factory)
{
    private async Task<List<AuditEntry>> EntriesForAsync(Guid entityId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.AuditEntries.Where(x => x.EntityId == entityId).OrderBy(x => x.TimestampUtc).ThenBy(x => x.Id).ToListAsync();
    }

    [Fact]
    public async Task Granting_and_revoking_role_permission_writes_audit_entries()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RA1");
        var actor = TestCurrentContext.For(tree.Org.Id, tree.Root.Path);
        var roleId = tree.Roles[BuiltInRoles.Teacher].Id;
        var grant = new RolePermission(tree.Org.Id, roleId, Permissions.Audit.View);

        await using (var db = factory.CreateDbContext(actor))
        {
            db.RolePermissions.Add(grant);
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext(actor))
        {
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.Id == grant.Id));
            await db.SaveChangesAsync();
        }

        var entries = await EntriesForAsync(grant.Id);
        Assert.Equal([AuditAction.Created, AuditAction.Deleted], entries.Select(e => e.Action));
        Assert.All(entries, e => Assert.Equal(nameof(RolePermission), e.EntityType));
        Assert.All(entries, e => Assert.Equal(tree.Org.Id, e.OrganizationId));
        var created = JsonDocument.Parse(entries[0].AfterJson!).RootElement;
        Assert.Equal(roleId, created.GetProperty("RoleId").GetGuid());
        Assert.Equal(Permissions.Audit.View, created.GetProperty("Permission").GetString());
        var deleted = JsonDocument.Parse(entries[1].BeforeJson!).RootElement;
        Assert.Equal(roleId, deleted.GetProperty("RoleId").GetGuid());
    }

    [Fact]
    public async Task Assigning_and_removing_role_assignment_writes_audit_entries()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RA2");
        var actor = TestCurrentContext.For(tree.Org.Id, tree.Root.Path);
        var assignment = new RoleAssignment(tree.School1, Guid.NewGuid(), tree.Roles[BuiltInRoles.Teacher].Id);

        await using (var db = factory.CreateDbContext(actor))
        {
            db.RoleAssignments.Add(assignment);
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext(actor))
        {
            db.RoleAssignments.Remove(await db.RoleAssignments.SingleAsync(x => x.Id == assignment.Id));
            await db.SaveChangesAsync();
        }

        var entries = await EntriesForAsync(assignment.Id);
        Assert.Equal([AuditAction.Created, AuditAction.Deleted], entries.Select(e => e.Action));
        Assert.All(entries, e => Assert.Equal(nameof(RoleAssignment), e.EntityType));
        Assert.Equal(actor.UserId, entries[0].UserId);
    }

    [Fact]
    public async Task Renaming_a_role_writes_an_update_entry_with_only_the_changed_property()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RA3");
        var actor = TestCurrentContext.For(tree.Org.Id, tree.Root.Path);
        var roleId = tree.Roles[BuiltInRoles.Counselor].Id;

        await using (var db = factory.CreateDbContext(actor))
        {
            var role = await db.Roles.SingleAsync(x => x.Id == roleId);
            db.Entry(role).Property(x => x.NameEn).CurrentValue = "Renamed role";
            await db.SaveChangesAsync();
        }

        var update = (await EntriesForAsync(roleId)).Single(e => e.Action == AuditAction.Updated);
        var after = JsonDocument.Parse(update.AfterJson!).RootElement;
        Assert.Equal("Renamed role", after.GetProperty("NameEn").GetString());
        Assert.False(after.TryGetProperty("NameAr", out _));
    }

    [Fact]
    public async Task Built_in_key_is_unique_per_client_but_custom_roles_and_other_clients_are_fine()
    {
        var a = await AccessTestData.SeedAsync(factory, "RA4");
        var b = await AccessTestData.SeedAsync(factory, "RA5");

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Roles.Add(new Role(a.Org.Id, null, "مخصص 1", "Custom 1", RoleBaseKind.Staff));
            db.Roles.Add(new Role(a.Org.Id, null, "مخصص 2", "Custom 2", RoleBaseKind.Staff));
            await db.SaveChangesAsync();
        }

        await using var duplicate = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        duplicate.Roles.Add(new Role(a.Org.Id, BuiltInRoles.Teacher, "م", "Another teacher", RoleBaseKind.Staff));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Contains(b.Roles.Values, r => r.BuiltInKey == BuiltInRoles.Teacher);
    }
}