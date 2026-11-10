using Lms.Domain.AcademicYears;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.AcademicYears;

internal sealed class AcademicYearResolver(LmsDbContext db) : IAcademicYearResolver
{
    public async Task<(IReadOnlyList<AcademicYear> Years, Guid DefiningOrgUnitId)> GetEffectiveAsync(
        string orgUnitPath, CancellationToken cancellationToken)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(orgUnitPath);

        // ISOLATION-BYPASS: inherited years live at ancestors, outside the caller's own subtree filter. The lookup is
        // bound to the exact ancestor paths of a unit the caller was already allowed to see (paths embed globally
        // unique unit ids, so no other client can match) and still excludes soft-deleted rows.
        var years = await db.AcademicYears.IgnoreQueryFilters()
            .Where(y => !y.IsDeleted && chain.Contains(y.OrgUnitPath))
            .OrderBy(y => y.StartDate)
            .ToListAsync(cancellationToken);

        var defining = years.GroupBy(y => y.OrgUnitPath).OrderByDescending(g => g.Key.Length).FirstOrDefault();
        if (defining is null)
        {
            return ([], SelfId(orgUnitPath));
        }

        return (defining.ToList(), defining.First().OrgUnitId);
    }

    public async Task<AcademicYear?> GetCurrentAsync(string orgUnitPath, CancellationToken cancellationToken)
    {
        var (years, _) = await GetEffectiveAsync(orgUnitPath, cancellationToken);
        return years.FirstOrDefault(y => y.Status == AcademicYearStatus.Active);
    }

    private static Guid SelfId(string path) =>
        Guid.TryParseExact(path.TrimEnd('/').Split('/')[^1], "N", out var id) ? id : Guid.Empty;
}
