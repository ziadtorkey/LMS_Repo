using Lms.Domain.AcademicYears;
using Lms.Infrastructure.Persistence;
using Lms.Shared.AcademicYears;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.AcademicYears;

internal static class AcademicYearMapper
{
    /// <summary>Builds year DTOs with their terms; <paramref name="viewedUnitId"/> decides <c>IsInherited</c>.</summary>
    public static async Task<IReadOnlyList<AcademicYearDto>> ToDtosAsync(
        LmsDbContext db, IReadOnlyList<AcademicYear> years, Guid viewedUnitId, DateOnly today, CancellationToken ct)
    {
        var yearIds = years.Select(y => y.Id).ToList();
        var organizationId = years.Count > 0 ? years[0].OrganizationId : Guid.Empty; // all years of one call share a client
        var unitIds = years.Select(y => y.OrgUnitId).Distinct().ToList();

        // ISOLATION-BYPASS: years may be inherited from an ancestor unit outside the caller's own subtree; their terms
        // and the defining unit's name are read by the ids of years the caller is already entitled to see, and are
        // re-restricted to that years' client (OrganizationId) and to live rows.
        var terms = (await db.Set<Term>().IgnoreQueryFilters()
                .Where(t => !t.IsDeleted && t.OrganizationId == organizationId && yearIds.Contains(t.AcademicYearId))
                .ToListAsync(ct))
            .GroupBy(t => t.AcademicYearId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.StartDate).ToList());
        // ISOLATION-BYPASS: name of the (possibly ancestor) defining unit, restricted to the years' client and live rows.
        var units = await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.OrganizationId == organizationId && unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NameAr, u.NameEn })
            .ToDictionaryAsync(u => u.Id, ct);

        return years.Select(y =>
        {
            units.TryGetValue(y.OrgUnitId, out var unit);
            var yearTerms = terms.TryGetValue(y.Id, out var list) ? list : [];
            return new AcademicYearDto(
                y.Id, y.OrgUnitId, unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty, y.NameAr, y.NameEn,
                y.StartDate, y.EndDate, y.Status.ToString(), y.OrgUnitId != viewedUnitId,
                yearTerms.Select(t => ToDto(t, today)).ToList());
        }).ToList();
    }

    public static TermDto ToDto(Term t, DateOnly today) =>
        new(t.Id, t.NameAr, t.NameEn, t.Order, t.StartDate, t.EndDate, t.StartDate <= today && today <= t.EndDate);
}
