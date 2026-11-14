using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Reads;

/// <summary>
/// The scale of a gradebook and its legend (L23: as configured, never a provisional marker). A gradebook without a chosen scale uses the
/// default scale visible at its unit; a unit without any gets an empty legend and no band.
/// </summary>
public sealed class BandLegendReader(LmsDbContext db, IScaleResolver scales)
{
    public async Task<VisibleScale?> ScaleOfAsync(Gradebook gradebook, CancellationToken ct)
    {
        var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == gradebook.OrgUnitId, ct);
        if (unit is null)
        {
            return null;
        }

        if (gradebook.GradingScaleId is { } id)
        {
            return await scales.FindScaleAsync(unit, id, ct);
        }

        return (await scales.ScalesAsync(unit, ct)).FirstOrDefault(s => s.Scale.IsDefault && !s.Scale.IsTemplate);
    }

    public static BandLegendDto Legend(VisibleScale? scale) =>
        scale is null
            ? new BandLegendDto(string.Empty, string.Empty, string.Empty, [])
            : new BandLegendDto(
                scale.Scale.NameAr, scale.Scale.NameEn, scale.Scale.Kind,
                scale.Bands.OrderBy(b => b.Order).Select(b => new BandLegendEntryDto(
                    b.Code, b.MinPercent, b.MaxPercent, b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points)).ToList());
}
