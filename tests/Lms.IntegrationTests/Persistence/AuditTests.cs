using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
public sealed class AuditTests(LmsApiFactory factory)
{
    private sealed record Seed(Organization Org, LevelType Level, OrgUnit Root);

    private async Task<Seed> SeedAsync(string name, string nameAr = "جذر")
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var org = new Organization(Guid.NewGuid(), "مجموعة " + name, name, OrganizationType.SchoolGroup);
        var level = new LevelType(org.Id, "مستوى", "Level", 1, true);
        var root = OrgUnit.CreateRoot(org.Id, level.Id, nameAr, "Root " + name, "R-" + name);
        db.AddRange(org, level, root);
        await db.SaveChangesAsync();
        return new Seed(org, level, root);
    }

    private async Task<List<AuditEntry>> EntriesForAsync(Guid entityId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.AuditEntries.Where(x => x.EntityId == entityId).OrderBy(x => x.TimestampUtc).ThenBy(x => x.Id).ToListAsync();
    }

    [Fact]
    public async Task Create_update_delete_of_audited_entity_writes_entries()
    {
        var seed = await SeedAsync("Audit1");
        var actor = TestCurrentContext.For(seed.Org.Id, seed.Root.Path);

        await using (var db = factory.CreateDbContext(actor))
        {
            var unit = await db.OrgUnits.SingleAsync(x => x.Id == seed.Root.Id);
            db.Entry(unit).Property(x => x.NameEn).CurrentValue = "Renamed";
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext(actor))
        {
            var unit = await db.OrgUnits.SingleAsync(x => x.Id == seed.Root.Id);
            db.OrgUnits.Remove(unit);
            await db.SaveChangesAsync();
        }

        var entries = await EntriesForAsync(seed.Root.Id);

        Assert.Equal([AuditAction.Created, AuditAction.Updated, AuditAction.Deleted], entries.Select(x => x.Action));
        Assert.All(entries, x => Assert.Equal(nameof(OrgUnit), x.EntityType));
        Assert.All(entries, x => Assert.Equal(seed.Org.Id, x.OrganizationId));
        Assert.Equal(actor.UserId, entries[1].UserId);

        var before = JsonDocument.Parse(entries[1].BeforeJson!).RootElement;
        var after = JsonDocument.Parse(entries[1].AfterJson!).RootElement;
        Assert.Equal(["NameEn"], before.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["NameEn"], after.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Root Audit1", before.GetProperty("NameEn").GetString());
        Assert.Equal("Renamed", after.GetProperty("NameEn").GetString());
    }

    [Fact]
    public async Task Unaudited_entity_writes_nothing()
    {
        var seed = await SeedAsync("Audit2");

        Assert.Empty(await EntriesForAsync(seed.Level.Id));
        Assert.Empty(await EntriesForAsync(seed.Org.Id));
    }

    [Fact]
    public async Task Arabic_and_emoji_round_trip_in_names_and_audit_json()
    {
        const string name = "مدرسة النور 🌟";
        var seed = await SeedAsync("Audit3", name);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var unit = await db.OrgUnits.SingleAsync(x => x.Id == seed.Root.Id);
        var created = await db.AuditEntries.SingleAsync(x => x.EntityId == seed.Root.Id);

        Assert.Equal(name, unit.NameAr);
        var after = JsonDocument.Parse(created.AfterJson!).RootElement;
        Assert.Equal(name, after.GetProperty("NameAr").GetString());
    }

    [Fact]
    public async Task Audit_entries_are_isolated_per_client()
    {
        var a = await SeedAsync("AuditA");
        var b = await SeedAsync("AuditB");

        await using var asA = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        var ids = await asA.AuditEntries.Select(x => x.EntityId).ToListAsync();

        Assert.Contains(a.Root.Id, ids);
        Assert.DoesNotContain(b.Root.Id, ids);

        await using var noClient = factory.CreateDbContext(TestCurrentContext.For(Guid.NewGuid(), null));
        Assert.Empty(await noClient.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task Security_event_writer_persists_an_entry()
    {
        var userId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            await writer.WriteSecurityEventAsync("LoginFailed", userId, "{\"reason\":\"bad password\"}");
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.AuditEntries.SingleAsync(x => x.UserId == userId);

        Assert.Equal(AuditAction.SecurityEvent, entry.Action);
        Assert.Equal("LoginFailed", entry.EntityType);
        Assert.Null(entry.OrganizationId);
        Assert.Equal("{\"reason\":\"bad password\"}", entry.AfterJson);
    }

    [Fact]
    public async Task Security_event_writer_stores_the_ip_address()
    {
        var userId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditWriter>()
                .WriteSecurityEventAsync("LoginSucceeded", userId, null, ipAddress: "203.0.113.7");
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal("203.0.113.7", (await db.AuditEntries.SingleAsync(x => x.UserId == userId)).IpAddress);
    }

    [Fact]
    public async Task No_client_context_sees_no_audit_entries()
    {
        var userId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditWriter>().WriteSecurityEventAsync("LoginFailed", userId, null);
        }

        await using var noClient = factory.CreateDbContext(new TestCurrentContext { UserId = Guid.NewGuid() });
        Assert.Empty(await noClient.AuditEntries.ToListAsync());
    }
}
