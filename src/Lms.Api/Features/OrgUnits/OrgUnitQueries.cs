using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.OrgUnits;

/// <summary>Reads for the org tree: visible units with children flags and the permissions blocked by policies.</summary>
public sealed class OrgUnitQueries(LmsDbContext db)
{
    public async Task<IReadOnlyList<OrgUnitNodeDto>> ListAsync(string? search, bool includeInactive, CancellationToken ct)
    {
        var units = await db.OrgUnits.OrderBy(u => u.Depth).ThenBy(u => u.NameEn).ThenBy(u => u.Id).ToListAsync(ct);
        var byId = units.ToDictionary(u => u.Id);

        if (!includeInactive)
        {
            // An inactive unit hides itself and everything below it (D15).
            var inactivePaths = units.Where(u => u.Status == OrgUnitStatus.Inactive).Select(u => u.Path).ToList();
            units = units.Where(u => !inactivePaths.Any(p => OrgUnitPath.IsWithin(u.Path, p))).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var matches = units.Where(u => Contains(u.NameAr, term) || Contains(u.NameEn, term) || Contains(u.Code, term)).ToList();
            var keep = new HashSet<Guid>(matches.Select(m => m.Id));
            foreach (var match in matches)
            {
                // the visible ancestors of every match, so the UI can show it in place
                for (var parent = match.ParentId; parent is { } id && byId.ContainsKey(id); parent = byId[id].ParentId)
                {
                    keep.Add(id);
                }
            }

            units = units.Where(u => keep.Contains(u.Id)).ToList();
        }

        var withChildren = units.Where(u => u.ParentId is not null).Select(u => u.ParentId!.Value).ToHashSet();
        var policies = await LoadPoliciesAsync(units.Select(u => u.OrganizationId).Distinct().ToList(), ct);
        return units.Select(u => ToNode(u, withChildren.Contains(u.Id), policies)).ToList();
    }

    public async Task<OrgUnitNodeDto> GetNodeAsync(OrgUnit unit, CancellationToken ct)
    {
        var hasChildren = await db.OrgUnits.AnyAsync(u => u.ParentId == unit.Id, ct);
        var policies = await LoadPoliciesAsync([unit.OrganizationId], ct);
        return ToNode(unit, hasChildren, policies);
    }

    private static bool Contains(string value, string term) => value.Contains(term, StringComparison.InvariantCultureIgnoreCase);

    /// <summary>One query per request: every live policy of the clients involved.</summary>
    private async Task<List<(string Path, IReadOnlyList<string> Blocked)>> LoadPoliciesAsync(
        IReadOnlyList<Guid> organizationIds, CancellationToken ct)
    {
        // ISOLATION-BYPASS: policies sit on ancestors above the caller's scope; the clients are matched explicitly
        // and deleted rows are excluded by hand.
        var rows = await db.Set<OrgUnitPolicy>().IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && organizationIds.Contains(p.OrganizationId))
            .Select(p => new { p.OrgUnitPath, p.PolicyKey })
            .ToListAsync(ct);

        return rows
            .Where(r => PolicyCatalog.BlockedPermissions.ContainsKey(r.PolicyKey))
            .Select(r => (r.OrgUnitPath, PolicyCatalog.BlockedPermissions[r.PolicyKey]))
            .ToList();
    }

    private static OrgUnitNodeDto ToNode(
        OrgUnit u, bool hasChildren, List<(string Path, IReadOnlyList<string> Blocked)> policies) => new(
        u.Id, u.ParentId, u.LevelTypeId, u.NameAr, u.NameEn, u.Code, u.Depth, u.Status.ToString(),
        u.IsLevelTransfer, u.NextTransferUnitId, u.IsCourseManagementLevel, hasChildren,
        policies.Where(p => OrgUnitPath.IsWithin(u.Path, p.Path)).SelectMany(p => p.Blocked).Distinct().Order().ToList());
}