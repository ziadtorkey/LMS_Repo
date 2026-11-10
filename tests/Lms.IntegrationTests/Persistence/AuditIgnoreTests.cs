using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Infrastructure;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Persistence.Interceptors;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Lms.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
public sealed class AuditIgnoreTests(LmsApiFactory factory)
{
    [Audited]
    private sealed class Vault : Entity
    {
        public string Name { get; set; } = string.Empty;

        [AuditIgnore]
        public string Secret { get; set; } = string.Empty;
    }

    private sealed class VaultDbContext(DbContextOptions<VaultDbContext> options) : DbContext(options)
    {
        public DbSet<Vault> Vaults => Set<Vault>();
        public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Vault>().ToTable("ZzTestVaults").Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AuditEntry>().ToTable("AuditEntries").Property(x => x.Id).ValueGeneratedNever();
            modelBuilder.Entity<AuditEntry>().Property(x => x.Action).HasConversion<string>();
        }
    }

    [Fact]
    public async Task AuditIgnore_properties_are_never_serialized()
    {
        // A minimal model over the test database: a throw-away audited Vault table plus the real AuditEntries table.
        await using var lms = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var builder = new MySqlConnectionStringBuilder(lms.Database.GetConnectionString()!);
        var optionsBuilder = new DbContextOptionsBuilder<VaultDbContext>();
        ((DbContextOptionsBuilder)optionsBuilder).UseLmsMySql(builder.ConnectionString);
        optionsBuilder.AddInterceptors(new AuditInterceptor(TestCurrentContext.PlatformAdmin(), new SystemClock()));
        var options = optionsBuilder.Options;
        await using var db = new VaultDbContext(options);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS ZzTestVaults (Id char(36) NOT NULL PRIMARY KEY, Name varchar(100) NOT NULL, Secret varchar(100) NOT NULL)");

        var vault = new Vault { Name = "first", Secret = "hunter2-hash" };
        db.Vaults.Add(vault);
        await db.SaveChangesAsync();
        vault.Name = "second";
        vault.Secret = "rotated-hash";
        await db.SaveChangesAsync();
        vault.Secret = "rotated-again";
        await db.SaveChangesAsync(); // only an ignored property changed: no entry at all

        var entries = await db.AuditEntries.Where(x => x.EntityId == vault.Id).OrderBy(x => x.TimestampUtc).ToListAsync();

        Assert.Equal([AuditAction.Created, AuditAction.Updated], entries.Select(x => x.Action));
        Assert.All(entries, e =>
        {
            Assert.DoesNotContain("Secret", e.BeforeJson ?? string.Empty);
            Assert.DoesNotContain("Secret", e.AfterJson ?? string.Empty);
            Assert.DoesNotContain("hash", e.BeforeJson ?? string.Empty);
            Assert.DoesNotContain("hash", e.AfterJson ?? string.Empty);
            Assert.DoesNotContain("rotated", e.AfterJson ?? string.Empty);
        });
        Assert.Contains("second", entries[1].AfterJson);
    }
}
