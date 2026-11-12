using Lms.Domain.Abstractions;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Courses;

internal sealed class CourseMapper(LmsDbContext db, IOrgTree tree, ICurrentContext context)
{
    public async Task<IReadOnlyList<CourseSummaryDto>> SummariesAsync(IReadOnlyList<CourseView> views, CancellationToken ct)
    {
        if (views.Count == 0)
        {
            return [];
        }

        var courseIds = views.Select(v => v.Course.Id).ToList();
        var owners = await OwnerNamesAsync(views.Select(v => v.Course.OrgUnitId).Distinct().ToList(), ct);
        var allocations = await db.Set<CourseAllocation>().Where(a => courseIds.Contains(a.CourseId))
            .GroupBy(a => a.CourseId).Select(g => new { CourseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CourseId, g => g.Count, ct);

        return views.Select(v =>
        {
            var c = v.Course;
            var hasOwner = owners.TryGetValue(c.OrgUnitId, out var owner);
            // HasPublishedBlueprint becomes real with the blueprint tasks (M3 Task 16).
            return new CourseSummaryDto(
                c.Id, c.Code, c.NameAr, c.NameEn, c.Language, c.WeeklyPeriods, c.OrgUnitId,
                hasOwner ? owner.NameAr : string.Empty, hasOwner ? owner.NameEn : string.Empty, c.Status, v.IsInherited,
                allocations.GetValueOrDefault(c.Id), false);
        }).ToList();
    }

    public async Task<IReadOnlyList<CourseSupervisorDto>> SupervisorsAsync(Guid courseId, CancellationToken ct)
    {
        var supervisorRows = await db.Set<CourseSupervisor>().Where(s => s.CourseId == courseId).Select(s => s.UserId).ToListAsync(ct);
        var staff = supervisorRows.Count == 0
            ? []
            : await db.Set<StaffProfile>().Where(p => supervisorRows.Contains(p.UserId))
                .Select(p => new { p.UserId, p.NameAr, p.NameEn, p.Email }).ToListAsync(ct);
        return supervisorRows.Select(userId =>
        {
            var profile = staff.FirstOrDefault(p => p.UserId == userId);
            return new CourseSupervisorDto(userId, profile?.NameAr ?? string.Empty, profile?.NameEn ?? string.Empty, profile?.Email);
        }).ToList();
    }

    public async Task<CourseDetailDto> DetailAsync(CourseView view, bool canEdit, CancellationToken ct)
    {
        var course = view.Course;
        var summary = (await SummariesAsync([view], ct))[0];
        var restrictions = (await db.Set<CourseRestriction>().Where(r => r.CourseId == course.Id).OrderBy(r => r.Type).ToListAsync(ct))
            .Select(r => new CourseRestrictionDto(r.Type, r.Values)).ToList();

        var supervisors = await SupervisorsAsync(course.Id, ct);

        return new CourseDetailDto(summary, course.DescriptionAr, course.DescriptionEn, restrictions, supervisors, canEdit);
    }

    private async Task<Dictionary<Guid, (string NameAr, string NameEn)>> OwnerNamesAsync(IReadOnlyList<Guid> ownerIds, CancellationToken ct)
    {
        var names = new Dictionary<Guid, (string, string)>();

        // Owners inside the caller scope come through the normal filters; owners above it through the org tree reader.
        foreach (var unit in await db.OrgUnits.Where(u => ownerIds.Contains(u.Id)).Select(u => new { u.Id, u.NameAr, u.NameEn }).ToListAsync(ct))
        {
            names[unit.Id] = (unit.NameAr, unit.NameEn);
        }

        if (names.Count < ownerIds.Count && context is { OrganizationId: { } organizationId, ScopePath: { } scope })
        {
            foreach (var unit in await tree.GetAncestorsAndSelfAsync(scope, organizationId, ct))
            {
                names.TryAdd(unit.Id, (unit.NameAr, unit.NameEn));
            }
        }

        return names;
    }
}
