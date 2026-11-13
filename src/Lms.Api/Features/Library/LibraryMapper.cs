using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.Library;
using Lms.Domain.Organizations;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Library;

/// <summary>Resources to <see cref="LibraryResourceDto"/>: owner names, tags, levels, subject and unit-owned assets in a fixed number of queries.</summary>
internal sealed class LibraryMapper(LibraryQueries queries, ICourseCatalog catalog)
{
    /// <param name="browseUnit">The unit the caller browses: a resource owned elsewhere is inherited (read-only).</param>
    /// <param name="canManageHere">Does the caller hold Library.Manage at the browsed unit? Only resources it owns are editable.</param>
    public async Task<IReadOnlyList<LibraryResourceDto>> ToDtosAsync(
        IReadOnlyList<LibraryResource> resources, OrgUnit browseUnit, bool canManageHere, CancellationToken ct)
    {
        if (resources.Count == 0)
        {
            return [];
        }

        var ids = resources.Select(r => r.Id).ToList();
        var organizationId = browseUnit.OrganizationId;
        var tags = await queries.Tags(organizationId).Where(t => ids.Contains(t.ResourceId))
            .Select(t => new { t.ResourceId, t.Tag }).ToListAsync(ct);
        var levels = await queries.Levels(organizationId).Where(l => ids.Contains(l.ResourceId))
            .Select(l => new { l.ResourceId, l.LevelTypeId }).ToListAsync(ct);
        var owners = await queries.OwnersAsync(resources.Select(r => r.OrgUnitId).Distinct().ToList(), organizationId, ct);
        var assetIds = resources.SelectMany(r => new[] { r.FileAssetId, r.CoverAssetId }).Where(a => a is not null).Select(a => a!.Value).Distinct().ToList();
        var assets = assetIds.Count == 0
            ? []
            : await AssetsAsync(assetIds, organizationId, ct);
        var courses = new Dictionary<Guid, Course>();
        foreach (var courseId in resources.Where(r => r.CourseId is not null).Select(r => r.CourseId!.Value).Distinct())
        {
            if ((await catalog.FindAsync(courseId, ct))?.Course is { } course)
            {
                courses[courseId] = course;
            }
        }

        return resources.Select(r =>
        {
            owners.TryGetValue(r.OrgUnitId, out var owner);
            Course? course = null;
            if (r.CourseId is { } cid)
            {
                courses.TryGetValue(cid, out course);
            }

            var inherited = r.OrgUnitId != browseUnit.Id;
            return new LibraryResourceDto(
                r.Id, r.OrgUnitId, owner?.NameAr ?? string.Empty, owner?.NameEn ?? string.Empty, inherited, r.TitleAr, r.TitleEn,
                r.DescriptionAr, r.DescriptionEn, r.Type, levels.Where(l => l.ResourceId == r.Id).Select(l => l.LevelTypeId).ToList(),
                r.CourseId, course?.NameAr, course?.NameEn, tags.Where(t => t.ResourceId == r.Id).Select(t => t.Tag).Order().ToList(),
                Asset(assets, r.CoverAssetId), Asset(assets, r.FileAssetId), r.Url, r.DownloadAllowed,
                !inherited && canManageHere, r.CreatedAt);
        }).ToList();
    }

    public static AssetDto ToDto(Asset a) => new(
        a.Id, null, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.DurationSeconds,
        AssetTypes.ForFileName(a.FileName)?.Previewable == true, a.CreatedAt, a.OrgUnitId);

    private static AssetDto? Asset(Dictionary<Guid, Asset> assets, Guid? id) =>
        id is { } key && assets.TryGetValue(key, out var asset) ? ToDto(asset) : null;

    private async Task<Dictionary<Guid, Asset>> AssetsAsync(IReadOnlyCollection<Guid> ids, Guid organizationId, CancellationToken ct) =>
        await queries.LibraryAssets(organizationId).Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
}
