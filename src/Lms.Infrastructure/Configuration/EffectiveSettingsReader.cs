using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Configuration;

public interface IEffectiveSettingsReader
{
    /// <summary>One entry per catalog setting with its effective value at the unit and where it comes from.</summary>
    Task<IReadOnlyList<EffectiveSettingDto>> ReadAsync(OrgUnit unit, CancellationToken cancellationToken);
}

public sealed class EffectiveSettingsReader(LmsDbContext db, IOrgTree orgTree) : IEffectiveSettingsReader
{
    public async Task<IReadOnlyList<EffectiveSettingDto>> ReadAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        // Root first, unit last; the ancestors lie above the scope of the caller, so the tree service reads them.
        var chain = await orgTree.GetAncestorsAndSelfAsync(unit.Path, unit.OrganizationId, cancellationToken);
        var chainIds = chain.Select(x => x.Id).ToList();

        // ISOLATION-BYPASS: values (and locks) sit on ancestors above the subtree scope of the caller. The client and
        // soft-delete filters are re-applied explicitly, and only rows of the units on the path to this unit are read.
        var rows = await db.Set<OrgUnitSetting>().IgnoreQueryFilters()
            .Where(x => !x.IsDeleted && x.OrganizationId == unit.OrganizationId && chainIds.Contains(x.OrgUnitId))
            .Select(x => new { x.Key, x.OrgUnitId, x.ValueJson, x.IsLocked })
            .ToListAsync(cancellationToken);

        return SettingCatalog.All.Select(definition =>
        {
            // Shallow to deep, the same order the resolver uses.
            var own = chain
                .Select(c => rows.FirstOrDefault(r => r.Key == definition.Name && r.OrgUnitId == c.Id))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();

            // Same rule as the resolver: the highest locked row wins, otherwise the deepest row.
            var winner = own.FirstOrDefault(r => r.IsLocked) ?? own.LastOrDefault();
            var lockedByAncestor = own.Any(r => r.IsLocked && r.OrgUnitId != unit.Id);
            if (winner is null)
            {
                return new EffectiveSettingDto(definition.Name, definition.DefaultJson, false, false, false, null, null, null);
            }

            var source = chain.Single(c => c.Id == winner.OrgUnitId);
            return new EffectiveSettingDto(
                definition.Name, winner.ValueJson, winner.IsLocked, own.Any(r => r.OrgUnitId == unit.Id),
                lockedByAncestor, source.Id, source.NameAr, source.NameEn);
        }).ToList();
    }
}
