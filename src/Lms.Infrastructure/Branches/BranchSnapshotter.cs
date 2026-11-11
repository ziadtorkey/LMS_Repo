using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Branches;

public sealed class BranchSnapshotter(LmsDbContext db) : IBranchSnapshotter
{
    public const int MaxNodes = 500;

    public async Task<BranchSnapshot> CaptureAsync(
        OrgUnit root, bool includePolicies, bool includeSettings, CancellationToken cancellationToken)
    {
        // Through the filtered sets: only live units, policies and settings inside the caller's scope are read.
        var units = await db.OrgUnits.Where(u => u.Path.StartsWith(root.Path))
            .OrderBy(u => u.Depth).ThenBy(u => u.Path).Take(MaxNodes + 1).ToListAsync(cancellationToken);
        if (units.Count > MaxNodes)
        {
            throw new BranchTooLargeException(MaxNodes);
        }

        var keys = units.Select((u, i) => (u.Id, Key: i)).ToDictionary(x => x.Id, x => x.Key);
        var policies = includePolicies
            ? (await db.Set<OrgUnitPolicy>().Where(p => p.OrgUnitPath.StartsWith(root.Path)).ToListAsync(cancellationToken))
                .ToLookup(p => p.OrgUnitId, p => p.PolicyKey)
            : null;
        var settings = includeSettings
            ? (await db.Set<OrgUnitSetting>().Where(s => s.OrgUnitPath.StartsWith(root.Path)).ToListAsync(cancellationToken))
                .ToLookup(s => s.OrgUnitId, s => new BranchSetting(s.Key, s.ValueJson, s.IsLocked))
            : null;

        var nodes = units.Select(u => new BranchNode(
            keys[u.Id],
            u.ParentId is { } parent && keys.TryGetValue(parent, out var parentKey) && u.Id != root.Id ? parentKey : null,
            u.LevelTypeId, u.NameAr, u.NameEn, u.Code,
            u.IsLevelTransfer,
            u.NextTransferUnitId is { } next && keys.TryGetValue(next, out var nextKey) ? nextKey : null,
            u.IsCourseManagementLevel,
            policies is null ? [] : policies[u.Id].Order(StringComparer.Ordinal).ToList(),
            settings is null ? [] : settings[u.Id].OrderBy(s => s.Name, StringComparer.Ordinal).ToList())).ToList();
        return new BranchSnapshot(nodes);
    }
}
