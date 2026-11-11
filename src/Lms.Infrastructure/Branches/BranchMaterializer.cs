using Lms.Domain.Configuration;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Branches;

public sealed class BranchMaterializer(LmsDbContext db, IOrgTree tree, ICourseCatalog catalog) : IBranchMaterializer
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

        await CheckCourseCodesAsync(nodes, parent.OrganizationId, transformCode, cancellationToken);

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

        var courses = await CopyCoursesAsync(nodes, created, transformName, transformCode, cancellationToken);

        // One SaveChanges is one transaction: the whole branch exists or none of it.
        await db.SaveChangesAsync(cancellationToken);
        return new MaterializeResult(created[0], created.Count, skipped.ToList(), courses.Created, courses.SkippedAllocations);
    }

    /// <summary>Course codes are unique per client: a copy whose codes collide with live courses creates nothing.</summary>
    private async Task CheckCourseCodesAsync(
        IReadOnlyList<BranchNode> nodes, Guid organizationId, Func<string, string> transformCode, CancellationToken ct)
    {
        var codes = nodes.SelectMany(n => n.OwnedCourses ?? []).Select(c => transformCode(c.Code).Trim().ToUpperInvariant()).ToList();
        if (codes.Count == 0)
        {
            return;
        }

        var duplicated = codes.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key);
        // ISOLATION-BYPASS: course codes are unique per client, so a code used outside the caller subtree must be found.
        // Only live courses of the target client are read, and only their code keys.
        var taken = await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && codes.Contains(c.CodeKey!))
            .Select(c => c.CodeKey!).ToListAsync(ct);
        var conflicts = taken.Concat(duplicated).Distinct().Order(StringComparer.Ordinal).ToList();
        if (conflicts.Count > 0)
        {
            throw new BranchCourseCodeConflictException(conflicts);
        }
    }

    /// <summary>
    /// New courses for the ones owned inside the branch (with restrictions) and allocations: of those, and of outside
    /// courses that are still available at the new unit (others are skipped and reported).
    /// </summary>
    private async Task<(int Created, IReadOnlyList<string> SkippedAllocations)> CopyCoursesAsync(
        IReadOnlyList<BranchNode> nodes, Dictionary<int, OrgUnit> units, Func<string, string> transformName,
        Func<string, string> transformCode, CancellationToken ct)
    {
        var newCourses = new Dictionary<int, Course>();
        foreach (var node in nodes)
        {
            foreach (var source in node.OwnedCourses ?? [])
            {
                var course = new Course(
                    units[node.Key], transformCode(source.Code).Trim().ToUpperInvariant(), transformName(source.NameAr),
                    transformName(source.NameEn), source.DescriptionAr, source.DescriptionEn, source.Language, source.WeeklyPeriods);
                newCourses[source.Key] = course;
                db.Set<Course>().Add(course);
                foreach (var restriction in source.Restrictions)
                {
                    db.Set<CourseRestriction>().Add(new CourseRestriction(course, restriction.Type, restriction.Values));
                }
            }
        }

        var skipped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var unit = units[node.Key];
            foreach (var key in node.AllocatedOwnedKeys ?? [])
            {
                if (newCourses.TryGetValue(key, out var inside))
                {
                    db.Set<CourseAllocation>().Add(new CourseAllocation(inside, unit));
                }
            }

            foreach (var id in node.AllocatedCourseIds ?? [])
            {
                var outside = (await catalog.FindAsync(id, ct))?.Course;
                if (outside is { Status: CourseStatuses.Active } && OrgUnitPath.AncestorsAndSelf(unit.Path).Contains(outside.OrgUnitPath))
                {
                    db.Set<CourseAllocation>().Add(new CourseAllocation(outside, unit));
                }
                else
                {
                    skipped.Add(outside?.Code ?? id.ToString());
                }
            }
        }

        return (newCourses.Count, skipped.ToList());
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
