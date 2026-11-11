using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.AcademicYears;

internal sealed class AcademicYearResolver(LmsDbContext db, ICurrentContext context) : IAcademicYearResolver
{
    public async Task<(IReadOnlyList<AcademicYear> Years, Guid DefiningOrgUnitId)> GetEffectiveAsync(
        string orgUnitPath, CancellationToken cancellationToken)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(orgUnitPath);

        // The client is the caller's own; a platform admin without a client takes the organization of the unit itself.
        // ISOLATION-BYPASS: reading the unit by exact path (globally unique) only to learn its organization.
        var organizationId = context.OrganizationId
            ?? await db.Set<OrgUnit>().IgnoreQueryFilters().Where(u => u.Path == orgUnitPath)
                .Select(u => (Guid?)u.OrganizationId).FirstOrDefaultAsync(cancellationToken);
        if (organizationId is null)
        {
            return ([], SelfId(orgUnitPath));
        }

        // ISOLATION-BYPASS: inherited years live at ancestors, outside the caller's own subtree filter. The lookup
        // re-applies the client (OrganizationId) and the soft-delete filter explicitly and is bound to the exact
        // ancestor paths of a unit the caller was already allowed to see.
        var years = await db.Set<AcademicYear>().IgnoreQueryFilters()
            .Where(y => !y.IsDeleted && y.OrganizationId == organizationId && chain.Contains(y.OrgUnitPath))
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
