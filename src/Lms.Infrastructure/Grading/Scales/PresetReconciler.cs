using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Scales;

internal sealed class SaudiPresetCatalog : IScalePresetCatalog
{
    public IReadOnlyList<SeedScale> Presets => SaudiPresetSeed.All;
}

/// <summary>
/// Seeds one client's grading presets at its root unit (L7, M5 bypass site 9): one plain default "Percentage" scale (once, even if an admin
/// later deletes it) and the Saudi presets as provisional templates. A template behind the catalog is brought up to date; copies
/// that admins made from a template are never touched here (they show "update available" and an admin applies it). Add-only and idempotent.
/// </summary>
internal sealed class PresetReconciler(LmsDbContext db, IScalePresetCatalog catalog) : IPresetReconciler
{
    public async Task<int> ReconcileAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // ISOLATION-BYPASS: runs without a caller (provisioning or startup); the client is the parameter, every row read is limited to it.
        var root = await db.Set<OrgUnit>().IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.OrganizationId == organizationId && u.ParentId == null)
            .FirstOrDefaultAsync(cancellationToken);
        if (root is null)
        {
            return 0;
        }

        // ISOLATION-BYPASS: system context; the root unit's own scales including soft-deleted ones (a deleted plain default stays deleted), this client only.
        var scales = await db.Set<GradingScale>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && s.OrgUnitId == root.Id)
            .ToListAsync(cancellationToken);
        var changes = 0;
        var service = new GradingScaleService(db, new NoScaleUsage(), catalog);

        if (!scales.Any(s => s.SeedKey == SaudiPresetSeed.PlainDefaultKey))
        {
            var plain = GradingScale.CreatePlain(root, "النسبة المئوية", "Percentage", "Percentage", isDefault: !scales.Any(s => s.IsDefault && !s.IsDeleted));
            plain.SetPlainSeed(SaudiPresetSeed.PlainDefaultKey);
            db.Add(plain);
            db.Add(new GradingScaleBand(plain, 0, 0, 100, "PCT", null, null, null, null, null));
            changes++;
        }

        foreach (var seed in catalog.Presets)
        {
            var template = scales.FirstOrDefault(s => s.IsTemplate && s.SeedKey == seed.Key && !s.IsDeleted);
            if (template is null)
            {
                var created = GradingScale.CreateSeeded(root, seed.NameAr, seed.NameEn, seed.Kind, seed.Key, seed.Version, isTemplate: true);
                db.Add(created);
                service.AddSeedBands(created, seed);
                changes++;
            }
            else if (template.SeedVersion < seed.Version)
            {
                db.RemoveRange(await db.Set<GradingScaleBand>().Where(b => b.ScaleId == template.Id).ToListAsync(cancellationToken));
                template.ApplySeed(seed.NameAr, seed.NameEn, seed.Kind, seed.Version);
                service.AddSeedBands(template, seed);
                changes++;
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changes;
    }
}

/// <summary>Until the gradebook module exists (M5 Task 7) nothing uses a scale.</summary>
internal sealed class NoScaleUsage : IScaleUsage
{
    public Task<bool> IsInUseAsync(Guid scaleId, CancellationToken cancellationToken) => Task.FromResult(false);
}
