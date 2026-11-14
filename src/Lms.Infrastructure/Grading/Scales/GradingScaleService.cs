using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Scales;

/// <summary>One band of a save, in the order the caller listed (the order is the position).</summary>
public sealed record BandSpec(
    decimal MinPercent, decimal MaxPercent, string Code, string? LabelAr, string? LabelEn, string? DescriptorAr, string? DescriptorEn, decimal? Points);

public sealed record ItemSpec(string NameAr, string NameEn, decimal Weight);

/// <summary>The writes of scales and weight templates at the unit that owns them (M5 Task 4). Authorization and validation are the caller's.</summary>
public sealed class GradingScaleService(LmsDbContext db, IScaleUsage usage, IScalePresetCatalog catalog)
{
    public async Task<GradingScale> CreateAsync(
        OrgUnit unit, string nameAr, string nameEn, string kind, IReadOnlyList<BandSpec> bands, CancellationToken ct)
    {
        var scale = GradingScale.CreatePlain(unit, nameAr, nameEn, kind, isDefault: false);
        db.Add(scale);
        AddBands(scale, bands);
        await db.SaveChangesAsync(ct);
        return scale;
    }

    /// <summary>Replaces names, kind and bands. A change to a seeded copy makes it the admin's own (no longer provisional).</summary>
    public async Task UpdateAsync(
        GradingScale scale, string nameAr, string nameEn, string kind, IReadOnlyList<BandSpec> bands, CancellationToken ct)
    {
        var existing = await db.Set<GradingScaleBand>().Where(b => b.ScaleId == scale.Id).OrderBy(b => b.Order).ToListAsync(ct);
        var changed = existing.Count != bands.Count || existing.Zip(bands).Any(p => !Same(p.First, p.Second));
        scale.Rename(nameAr, nameEn, kind);
        if (changed)
        {
            db.RemoveRange(existing);
            AddBands(scale, bands);
            scale.MarkEdited();
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmAsync(GradingScale scale, CancellationToken ct)
    {
        scale.Confirm();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Makes the scale the unit's default and clears the previous one (one default per unit).</summary>
    public async Task MakeDefaultAsync(GradingScale scale, CancellationToken ct)
    {
        var others = await db.Set<GradingScale>().Where(s => s.OrgUnitId == scale.OrgUnitId && s.IsDefault && s.Id != scale.Id).ToListAsync(ct);
        foreach (var other in others)
        {
            other.ClearDefault();
        }

        if (others.Count > 0)
        {
            await db.SaveChangesAsync(ct); // the unique index must see the old default cleared before the new one is set
        }

        scale.MakeDefault();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a scale (soft) unless a gradebook uses it. False = in use.</summary>
    public async Task<bool> DeleteAsync(GradingScale scale, CancellationToken ct)
    {
        if (await usage.IsInUseAsync(scale.Id, ct))
        {
            return false;
        }

        db.RemoveRange(await db.Set<GradingScaleBand>().Where(b => b.ScaleId == scale.Id).ToListAsync(ct));
        db.Remove(scale);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// A provisional copy of a preset at the unit (L7). When the unit already holds an unedited copy of it that is behind the catalog, that copy is
    /// updated instead (it becomes provisional again); an edited copy is left alone and a fresh copy is made.
    /// </summary>
    public async Task<GradingScale?> CopyPresetAsync(OrgUnit unit, string seedKey, CancellationToken ct)
    {
        var seed = catalog.Presets.FirstOrDefault(p => p.Key == seedKey);
        if (seed is null)
        {
            return null;
        }

        var behind = await db.Set<GradingScale>()
            .FirstOrDefaultAsync(s => s.OrgUnitId == unit.Id && s.SeedKey == seedKey && !s.IsTemplate && !s.Edited && s.SeedVersion < seed.Version, ct);
        if (behind is not null)
        {
            db.RemoveRange(await db.Set<GradingScaleBand>().Where(b => b.ScaleId == behind.Id).ToListAsync(ct));
            behind.ApplySeed(seed.NameAr, seed.NameEn, seed.Kind, seed.Version);
            AddSeedBands(behind, seed);
            await db.SaveChangesAsync(ct);
            return behind;
        }

        var copy = GradingScale.CreateSeeded(unit, seed.NameAr, seed.NameEn, seed.Kind, seed.Key, seed.Version, isTemplate: false);
        db.Add(copy);
        AddSeedBands(copy, seed);
        await db.SaveChangesAsync(ct);
        return copy;
    }

    public async Task<WeightTemplate> SaveTemplateAsync(
        OrgUnit unit, WeightTemplate? template, string nameAr, string nameEn, IReadOnlyList<ItemSpec> items, CancellationToken ct)
    {
        if (template is null)
        {
            template = new WeightTemplate(unit, nameAr, nameEn);
            db.Add(template);
        }
        else
        {
            template.Rename(nameAr, nameEn);
            db.RemoveRange(await db.Set<WeightTemplateItem>().Where(i => i.TemplateId == template.Id).ToListAsync(ct));
        }

        for (var i = 0; i < items.Count; i++)
        {
            db.Add(new WeightTemplateItem(template, i, items[i].NameAr, items[i].NameEn, items[i].Weight));
        }

        await db.SaveChangesAsync(ct);
        return template;
    }

    public async Task DeleteTemplateAsync(WeightTemplate template, CancellationToken ct)
    {
        db.RemoveRange(await db.Set<WeightTemplateItem>().Where(i => i.TemplateId == template.Id).ToListAsync(ct));
        db.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    private void AddBands(GradingScale scale, IReadOnlyList<BandSpec> bands)
    {
        for (var i = 0; i < bands.Count; i++)
        {
            var b = bands[i];
            db.Add(new GradingScaleBand(scale, i, b.MinPercent, b.MaxPercent, b.Code.Trim(), b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points));
        }
    }

    internal void AddSeedBands(GradingScale scale, SeedScale seed)
    {
        for (var i = 0; i < seed.Bands.Count; i++)
        {
            var b = seed.Bands[i];
            db.Add(new GradingScaleBand(scale, i, b.Min, b.Max, b.Code, b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points));
        }
    }

    private static bool Same(GradingScaleBand stored, BandSpec spec) =>
        stored.MinPercent == spec.MinPercent && stored.MaxPercent == spec.MaxPercent && stored.Code == spec.Code.Trim()
        && stored.LabelAr == spec.LabelAr && stored.LabelEn == spec.LabelEn && stored.DescriptorAr == spec.DescriptorAr
        && stored.DescriptorEn == spec.DescriptorEn && stored.Points == spec.Points;
}
