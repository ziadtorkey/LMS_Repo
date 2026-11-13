using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.Library;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Library;

/// <summary>
/// What the caller can read of the library: the resources owned at the browsed unit or by one of its ancestors (D13). A
/// resource is read-only below its owner. The normal subtree filter only returns rows at or below the caller's scope, so the
/// ancestor chain is read here, in one place, restricted to the caller's client and the live rows.
/// </summary>
internal sealed class LibraryQueries(LmsDbContext db, ICurrentContext context)
{
    /// <summary>Resources owned at <paramref name="unit"/> or above it, in the caller's client.</summary>
    public IQueryable<LibraryResource> VisibleAt(OrgUnit unit)
    {
        var paths = OrgUnitPath.AncestorsAndSelf(unit.Path);
        var organizationId = unit.OrganizationId;

        // ISOLATION-BYPASS: resources owned by an ancestor of the browsed unit lie above the caller's scope. Only the unit's own
        // ancestor chain (paths are globally unique unit ids) of the unit's client is read, deleted rows excluded by hand.
        return db.Set<LibraryResource>().IgnoreQueryFilters()
            .Where(r => !r.IsDeleted && r.OrganizationId == organizationId && paths.Contains(r.OrgUnitPath));
    }

    /// <summary>One resource when it is visible at the unit (owned there or above).</summary>
    public Task<LibraryResource?> FindAtAsync(Guid id, OrgUnit unit, CancellationToken ct) =>
        VisibleAt(unit).FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>The unit of the caller's active assignment; null when there is no scope.</summary>
    public async Task<OrgUnit?> ContextUnitAsync(CancellationToken ct) =>
        context.ScopePath is { } path ? await db.OrgUnits.FirstOrDefaultAsync(u => u.Path == path, ct) : null;

    /// <summary>
    /// The level types of the caller's student profiles' home units, or null when the caller is not a student (staff see every
    /// level). A student sees a resource only when it lists one of these level types, or none.
    /// </summary>
    public async Task<IReadOnlyList<Guid>?> StudentLevelTypesAsync(CancellationToken ct)
    {
        if (context.UserId is not { } userId)
        {
            return null;
        }

        var homes = await db.Set<StudentProfile>().Where(s => s.UserId == userId).Select(s => s.OrgUnitId).ToListAsync(ct);
        if (homes.Count == 0)
        {
            return null;
        }

        return await db.OrgUnits.Where(u => homes.Contains(u.Id)).Select(u => u.LevelTypeId).Distinct().ToListAsync(ct);
    }

    /// <summary>Owner unit names, for resources owned above the caller's scope (names only; same client, live rows).</summary>
    public async Task<Dictionary<Guid, OrgUnit>> OwnersAsync(IReadOnlyCollection<Guid> unitIds, Guid organizationId, CancellationToken ct)
    {
        if (unitIds.Count == 0)
        {
            return [];
        }

        // ISOLATION-BYPASS: the owner of an inherited resource is an ancestor unit above the caller's scope; only its names are
        // shown, for resources the caller can already read. Client matched, deleted units excluded by hand.
        return await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => unitIds.Contains(u.Id) && u.OrganizationId == organizationId && !u.IsDeleted)
            .ToDictionaryAsync(u => u.Id, ct);
    }

    /// <summary>Tags of resources of the client, including those owned by ancestors of the caller's scope.</summary>
    public IQueryable<LibraryTag> Tags(Guid organizationId) =>
        // ISOLATION-BYPASS: tags follow their resource, which may be owned above the caller's scope; client matched, deleted rows
        // excluded by hand, only used to filter resources the caller can already read.
        db.Set<LibraryTag>().IgnoreQueryFilters().Where(t => t.OrganizationId == organizationId && !t.IsDeleted);

    /// <summary>Level types of resources of the client (same reasoning as <see cref="Tags"/>).</summary>
    public IQueryable<LibraryResourceLevel> Levels(Guid organizationId) =>
        // ISOLATION-BYPASS: levels follow their resource, which may be owned above the caller's scope; client matched, deleted
        // rows excluded by hand, only used to filter resources the caller can already read.
        db.Set<LibraryResourceLevel>().IgnoreQueryFilters().Where(l => l.OrganizationId == organizationId && !l.IsDeleted);

    /// <summary>Assets of the client, including the unit-owned files and covers of resources owned above the caller's scope.</summary>
    public IQueryable<Asset> LibraryAssets(Guid organizationId) =>
        // ISOLATION-BYPASS: the file and the cover of an inherited resource are owned by the resource's unit, above the caller's
        // scope; client matched, deleted rows excluded by hand, only read for resources the caller can already read.
        db.Set<Asset>().IgnoreQueryFilters().Where(a => a.OrganizationId == organizationId && !a.IsDeleted);

    public IQueryable<LibraryResource> Apply(
        IQueryable<LibraryResource> resources, LibraryQuery query, IReadOnlyList<Guid>? studentLevels, Guid organizationId)
    {
        var tags = Tags(organizationId);
        var levels = Levels(organizationId);
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            resources = resources.Where(r => r.Type == query.Type);
        }

        if (query.CourseId is { } courseId)
        {
            resources = resources.Where(r => r.CourseId == courseId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var text = query.Search.Trim();
            resources = resources.Where(r => r.TitleAr.Contains(text) || r.TitleEn.Contains(text)
                || (r.DescriptionAr != null && r.DescriptionAr.Contains(text)) || (r.DescriptionEn != null && r.DescriptionEn.Contains(text)));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim();
            resources = resources.Where(r => tags.Any(t => t.ResourceId == r.Id && t.Tag == tag));
        }

        if (query.LevelTypeId is { } levelTypeId)
        {
            resources = resources.Where(r => !levels.Any(l => l.ResourceId == r.Id) || levels.Any(l => l.ResourceId == r.Id && l.LevelTypeId == levelTypeId));
        }

        if (studentLevels is not null)
        {
            resources = resources.Where(r => !levels.Any(l => l.ResourceId == r.Id) || levels.Any(l => l.ResourceId == r.Id && studentLevels.Contains(l.LevelTypeId)));
        }

        return resources;
    }
}
