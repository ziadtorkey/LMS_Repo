using Lms.Domain.Abstractions;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Lms.Infrastructure.Seeding;

/// <summary>
/// Seeds the two sample clients (school group and university) with level types, trees and built-in roles.
/// Development only and idempotent: every row is looked up by a stable key before it is created.
/// </summary>
public sealed class DevSeeder(string connectionString, IHostEnvironment environment)
{
    private sealed class SeedContext : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public Guid? AssignmentId => null;
        public string? ScopePath => null;
        public bool IsPlatformAdmin => true;
    }

    /// <summary>Returns false (and touches nothing) outside the Development environment.</summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            return false;
        }

        await using var db = LmsDbContextFactory.Create(connectionString, new SeedContext());
        await db.Database.MigrateAsync(cancellationToken);

        await SeedTreeAsync(db, DevSeedData.SchoolGroup, cancellationToken);
        await SeedTreeAsync(db, DevSeedData.University, cancellationToken);
        return true;
    }

    private static async Task SeedTreeAsync(LmsDbContext db, DevSeedData.ClientSeed seed, CancellationToken ct)
    {
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == seed.OrganizationId, ct);
        if (organization is null)
        {
            organization = new Organization(seed.OrganizationId, seed.NameAr, seed.NameEn, seed.Type);
            db.Organizations.Add(organization);
            await db.SaveChangesAsync(ct);
        }

        var levels = await db.LevelTypes.Where(l => l.OrganizationId == organization.Id).ToDictionaryAsync(l => l.NameEn, ct);
        foreach (var (level, index) in seed.Levels.Select((l, i) => (l, i)))
        {
            if (!levels.ContainsKey(level.NameEn))
            {
                var created = new LevelType(organization.Id, level.NameAr, level.NameEn, index + 1, level.CanHostClasses);
                db.LevelTypes.Add(created);
                levels[level.NameEn] = created;
            }
        }

        await db.SaveChangesAsync(ct);

        var units = await db.OrgUnits.Where(u => u.OrganizationId == organization.Id).ToDictionaryAsync(u => u.Code, ct);
        await AddUnitsAsync(db, organization, levels, units, seed.Root, parent: null, ct);

        await new DefaultRoleProvisioner(db).ProvisionAsync(organization.Id, seed.Type, ct);
    }

    private static async Task AddUnitsAsync(
        LmsDbContext db, Organization organization, Dictionary<string, LevelType> levels,
        Dictionary<string, OrgUnit> units, DevSeedData.UnitSeed node, OrgUnit? parent, CancellationToken ct)
    {
        if (!units.TryGetValue(node.Code, out var unit))
        {
            var levelId = levels[node.Level].Id;
            unit = parent is null
                ? OrgUnit.CreateRoot(organization.Id, levelId, node.NameAr, node.NameEn, node.Code)
                : OrgUnit.CreateChild(parent, levelId, node.NameAr, node.NameEn, node.Code);
            if (node.IsLevelTransfer)
            {
                unit.MarkAsLevelTransfer(null);
            }

            db.OrgUnits.Add(unit);
            await db.SaveChangesAsync(ct);
            units[node.Code] = unit;
        }

        foreach (var child in node.Children)
        {
            await AddUnitsAsync(db, organization, levels, units, child, unit, ct);
        }
    }
}