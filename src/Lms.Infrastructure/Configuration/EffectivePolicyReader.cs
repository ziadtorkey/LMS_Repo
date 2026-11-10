using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Configuration;

public interface IEffectivePolicyReader
{
    /// <summary>One entry per catalog policy: set at the unit, inherited from an ancestor (with its names), or off.</summary>
    Task<IReadOnlyList<EffectivePolicyDto>> ReadAsync(OrgUnit unit, CancellationToken cancellationToken);
}

public sealed class EffectivePolicyReader(LmsDbContext db, IOrgTree orgTree) : IEffectivePolicyReader
{
    public async Task<IReadOnlyList<EffectivePolicyDto>> ReadAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        // Root first, unit last; the ancestors lie above the scope of the caller, so the tree service reads them.
        var chain = await orgTree.GetAncestorsAndSelfAsync(unit.Path, unit.OrganizationId, cancellationToken);
        var chainIds = chain.Select(x => x.Id).ToList();

        // ISOLATION-BYPASS: policies sit on ancestors above the subtree scope of the caller. The client and soft-delete
        // filters are re-applied explicitly, and only rows of the units on the path to this unit are read.
        var rows = await db.Set<OrgUnitPolicy>().IgnoreQueryFilters()
            .Where(x => !x.IsDeleted && x.OrganizationId == unit.OrganizationId && chainIds.Contains(x.OrgUnitId))
            .Select(x => new { x.PolicyKey, x.OrgUnitId })
            .ToListAsync(cancellationToken);

        return PolicyCatalog.BlockedPermissions.Keys.Order(StringComparer.Ordinal).Select(key =>
        {
            var holders = rows.Where(r => r.PolicyKey == key).Select(r => r.OrgUnitId).ToHashSet();
            if (holders.Contains(unit.Id))
            {
                return new EffectivePolicyDto(key, PolicyStates.SetHere, unit.Id, unit.NameAr, unit.NameEn);
            }

            // The highest ancestor holding it is the source (chain is ordered root first).
            var source = chain.FirstOrDefault(c => holders.Contains(c.Id));
            return source is null
                ? new EffectivePolicyDto(key, PolicyStates.Off, null, null, null)
                : new EffectivePolicyDto(key, PolicyStates.Inherited, source.Id, source.NameAr, source.NameEn);
        }).ToList();
    }
}
