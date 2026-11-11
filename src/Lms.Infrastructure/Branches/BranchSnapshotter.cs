using Lms.Domain.Configuration;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Branches;

public sealed class BranchSnapshotter(LmsDbContext db) : IBranchSnapshotter
{
    public const int MaxNodes = 500;

    public async Task<BranchSnapshot> CaptureAsync(
        OrgUnit root, bool includePolicies, bool includeSettings, CancellationToken cancellationToken, bool includeCourses = false)
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

        var courses = includeCourses ? await CaptureCoursesAsync(root, cancellationToken) : null;
        var nodes = units.Select(u => new BranchNode(
            keys[u.Id],
            u.ParentId is { } parent && keys.TryGetValue(parent, out var parentKey) && u.Id != root.Id ? parentKey : null,
            u.LevelTypeId, u.NameAr, u.NameEn, u.Code,
            u.IsLevelTransfer,
            u.NextTransferUnitId is { } next && keys.TryGetValue(next, out var nextKey) ? nextKey : null,
            u.IsCourseManagementLevel,
            policies is null ? [] : policies[u.Id].Order(StringComparer.Ordinal).ToList(),
            settings is null ? [] : settings[u.Id].OrderBy(s => s.Name, StringComparer.Ordinal).ToList(),
            courses?.AllocatedOutside[u.Id].ToList(),
            courses?.AllocatedInside[u.Id].ToList(),
            courses?.Owned[u.Id].ToList())).ToList();
        return new BranchSnapshot(nodes);
    }

    private sealed record CourseCapture(
        ILookup<Guid, BranchCourse> Owned, ILookup<Guid, int> AllocatedInside, ILookup<Guid, Guid> AllocatedOutside);

    /// <summary>Courses owned inside the branch (with restrictions) and the allocations at its units; filtered sets only.</summary>
    private async Task<CourseCapture> CaptureCoursesAsync(OrgUnit root, CancellationToken ct)
    {
        var owned = await db.Set<Course>().Where(c => c.OrgUnitPath.StartsWith(root.Path)).OrderBy(c => c.Code).ToListAsync(ct);
        var ids = owned.Select(c => c.Id).ToList();
        var restrictions = (await db.Set<CourseRestriction>().Where(r => ids.Contains(r.CourseId)).ToListAsync(ct))
            .ToLookup(r => r.CourseId, r => new BranchRestriction(r.Type, r.Values));
        var keys = owned.Select((c, i) => (c.Id, Key: i)).ToDictionary(x => x.Id, x => x.Key);
        var allocations = await db.Set<CourseAllocation>().Where(a => a.OrgUnitPath.StartsWith(root.Path)).ToListAsync(ct);
        return new CourseCapture(
            owned.ToLookup(
                c => c.OrgUnitId,
                c => new BranchCourse(
                    keys[c.Id], c.Code, c.NameAr, c.NameEn, c.DescriptionAr, c.DescriptionEn, c.Language, c.WeeklyPeriods,
                    restrictions[c.Id].OrderBy(r => r.Type, StringComparer.Ordinal).ToList())),
            allocations.Where(a => keys.ContainsKey(a.CourseId)).OrderBy(a => keys[a.CourseId]).ToLookup(a => a.OrgUnitId, a => keys[a.CourseId]),
            allocations.Where(a => !keys.ContainsKey(a.CourseId)).OrderBy(a => a.CourseId).ToLookup(a => a.OrgUnitId, a => a.CourseId));
    }
}
