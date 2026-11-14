using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Scales;

internal sealed class ScaleResolver(LmsDbContext db) : IScaleResolver
{
    public async Task<IReadOnlyList<VisibleScale>> ScalesAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: scales of ancestor units lie outside the caller's own subtree filter. The lookup re-applies the client
        // (OrganizationId) and the soft-delete filter and is bound to the exact ancestor-or-self paths of a unit the caller may see.
        var scales = await db.Set<GradingScale>().IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.OrganizationId == unit.OrganizationId && chain.Contains(s.OrgUnitPath))
            .OrderBy(s => s.NameEn).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
        return await WithBandsAsync(unit.OrganizationId, scales, cancellationToken);
    }

    public async Task<VisibleScale?> FindScaleAsync(OrgUnit unit, Guid scaleId, CancellationToken cancellationToken)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: see ScalesAsync; one scale by id, same client and the same ancestor-or-self paths.
        var scale = await db.Set<GradingScale>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == scaleId && !s.IsDeleted && s.OrganizationId == unit.OrganizationId && chain.Contains(s.OrgUnitPath), cancellationToken);
        return scale is null ? null : (await WithBandsAsync(unit.OrganizationId, [scale], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<VisibleWeightTemplate>> TemplatesAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: weight templates of ancestor units, same rule as the scales above.
        var templates = await db.Set<WeightTemplate>().IgnoreQueryFilters()
            .Where(t => !t.IsDeleted && t.OrganizationId == unit.OrganizationId && chain.Contains(t.OrgUnitPath))
            .OrderBy(t => t.NameEn).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return await WithItemsAsync(unit.OrganizationId, templates, cancellationToken);
    }

    public async Task<VisibleWeightTemplate?> FindTemplateAsync(OrgUnit unit, Guid templateId, CancellationToken cancellationToken)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: see TemplatesAsync; one template by id.
        var template = await db.Set<WeightTemplate>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == templateId && !t.IsDeleted && t.OrganizationId == unit.OrganizationId && chain.Contains(t.OrgUnitPath), cancellationToken);
        return template is null ? null : (await WithItemsAsync(unit.OrganizationId, [template], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<VisibleScale>> WithBandsAsync(Guid organizationId, List<GradingScale> scales, CancellationToken ct)
    {
        var ids = scales.Select(s => s.Id).ToList();

        // ISOLATION-BYPASS: the bands of scales already found visible, by scale id, limited to the same client.
        var bands = (await db.Set<GradingScaleBand>().IgnoreQueryFilters()
                .Where(b => b.OrganizationId == organizationId && ids.Contains(b.ScaleId))
                .ToListAsync(ct))
            .ToLookup(b => b.ScaleId);
        return scales.Select(s => new VisibleScale(s, bands[s.Id].OrderBy(b => b.Order).ToList())).ToList();
    }

    private async Task<IReadOnlyList<VisibleWeightTemplate>> WithItemsAsync(Guid organizationId, List<WeightTemplate> templates, CancellationToken ct)
    {
        var ids = templates.Select(t => t.Id).ToList();

        // ISOLATION-BYPASS: the items of templates already found visible, by template id, limited to the same client.
        var items = (await db.Set<WeightTemplateItem>().IgnoreQueryFilters()
                .Where(i => i.OrganizationId == organizationId && ids.Contains(i.TemplateId))
                .ToListAsync(ct))
            .ToLookup(i => i.TemplateId);
        return templates.Select(t => new VisibleWeightTemplate(t, items[t.Id].OrderBy(i => i.Order).ToList())).ToList();
    }
}
