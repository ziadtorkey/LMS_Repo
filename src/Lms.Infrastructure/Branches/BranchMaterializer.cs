using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Branches;

public sealed class BranchMaterializer(LmsDbContext db, IOrgTree tree) : IBranchMaterializer
{
    public async Task<MaterializeResult> MaterializeAsync(
        BranchSnapshot snapshot, OrgUnit parent, Func<string, string> transformName, Func<string, string> transformCode,
        CancellationToken cancellationToken)
    {
        var nodes = snapshot.Nodes;
        if (nodes.Count == 0 || nodes[0].Key != 0)
        {
            throw new BranchInvalidException("The snapshot has no root.");
        }

        await CheckLevelsAsync(nodes, parent, cancellationToken);

        var codes = nodes.Select(n => transformCode(n.Code)).ToList();
        var taken = new List<string>();
        foreach (var code in codes)
        {
            if (await tree.CodeExistsAsync(parent.OrganizationId, code, null, cancellationToken))
            {
                taken.Add(code);
            }
        }

        if (taken.Count > 0)
        {
            throw new BranchCodeConflictException(taken);
        }

        var skippedNames = await LockedAboveAsync(parent, cancellationToken);
        var skipped = new SortedSet<string>(StringComparer.Ordinal);
        var created = new Dictionary<int, OrgUnit>();
        foreach (var (node, index) in nodes.Select((n, i) => (n, i)))
        {
            var above = node.ParentKey is { } parentKey ? created[parentKey] : parent;
            var unit = OrgUnit.CreateChild(above, node.LevelTypeId, transformName(node.NameAr), transformName(node.NameEn), codes[index]);
            created[node.Key] = unit;
            db.OrgUnits.Add(unit);
        }

        foreach (var node in nodes)
        {
            var unit = created[node.Key];
            unit.SetAcademicFlags(
                node.IsLevelTransfer,
                node.NextTransferKey is { } next && created.TryGetValue(next, out var target) ? target.Id : null,
                node.IsCourseManagementLevel);
            foreach (var key in node.PolicyKeys)
            {
                db.Add(new OrgUnitPolicy(unit.OrganizationId, unit.Id, unit.Path, key));
            }

            foreach (var setting in node.Settings)
            {
                if (skippedNames.Contains(setting.Name))
                {
                    skipped.Add(setting.Name);
                    continue;
                }

                db.Add(new OrgUnitSetting(unit.OrganizationId, unit.Id, unit.Path, setting.Name, setting.ValueJson, setting.IsLocked));
            }
        }

        // One SaveChanges is one transaction: the whole branch exists or none of it.
        await db.SaveChangesAsync(cancellationToken);
        return new MaterializeResult(created[0], created.Count, skipped.ToList());
    }

    /// <summary>Every level type must belong to the client, and the root's must be deeper than the parent's.</summary>
    private async Task CheckLevelsAsync(IReadOnlyList<BranchNode> nodes, OrgUnit parent, CancellationToken ct)
    {
        var ids = nodes.Select(n => n.LevelTypeId).Append(parent.LevelTypeId).Distinct().ToList();
        var levels = await db.LevelTypes.Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Order, ct);
        if (ids.Any(id => !levels.ContainsKey(id)) || levels[nodes[0].LevelTypeId] <= levels[parent.LevelTypeId])
        {
            throw new BranchInvalidException("The branch's level types do not fit below the target.");
        }
    }

    /// <summary>Names of settings locked at the parent or one of its ancestors.</summary>
    private async Task<HashSet<string>> LockedAboveAsync(OrgUnit parent, CancellationToken ct)
    {
        var paths = OrgUnitPath.AncestorsAndSelf(parent.Path);
        // ISOLATION-BYPASS: locks sit on ancestors above the caller's scope; the client and soft-delete filters are
        // re-applied explicitly.
        var names = await db.Set<OrgUnitSetting>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == parent.OrganizationId && !s.IsDeleted && s.IsLocked && paths.Contains(s.OrgUnitPath))
            .Select(s => s.Key).ToListAsync(ct);
        return names.ToHashSet(StringComparer.Ordinal);
    }
}
