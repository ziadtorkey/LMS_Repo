namespace Lms.Domain.AcademicYears;

/// <summary>Resolves which academic years apply to an org unit (inheritance, D17/D22).</summary>
public interface IAcademicYearResolver
{
    /// <summary>
    /// The years defined at the nearest ancestor-or-self unit of <paramref name="orgUnitPath"/> that defines any year (a
    /// unit with its own years overrides the parent list entirely). When nobody defines years the list is empty and
    /// <c>DefiningOrgUnitId</c> is the unit itself.
    /// </summary>
    Task<(IReadOnlyList<AcademicYear> Years, Guid DefiningOrgUnitId)> GetEffectiveAsync(string orgUnitPath, CancellationToken cancellationToken);

    /// <summary>The Active year of the effective list, if any.</summary>
    Task<AcademicYear?> GetCurrentAsync(string orgUnitPath, CancellationToken cancellationToken);
}
