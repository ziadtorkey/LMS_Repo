namespace Lms.Shared.AcademicYears;

/// <summary>Lifecycle of an academic year (FR-YEAR-01). At most one Active year per unit.</summary>
public static class AcademicYearStatuses
{
    public const string Planned = "Planned";
    public const string Active = "Active";
    public const string Closed = "Closed";
}

/// <summary>A term of a year; <c>Order</c> follows the start date, <c>IsCurrent</c> = today is within it.</summary>
public sealed record TermDto(
    Guid Id,
    string NameAr,
    string NameEn,
    int Order,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsCurrent);

/// <summary>An academic year; <c>IsInherited</c> = defined at an ancestor of the unit being viewed.</summary>
public sealed record AcademicYearDto(
    Guid Id,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    string NameAr,
    string NameEn,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    bool IsInherited,
    IReadOnlyList<TermDto> Terms);

/// <summary>
/// Response of GET /api/academic-years/effective: the years of the nearest ancestor-or-self unit that defines any
/// (<c>DefiningOrgUnitId</c>) and the Active one, if any.
/// </summary>
public sealed record YearContextDto(IReadOnlyList<AcademicYearDto> Years, Guid? CurrentYearId, Guid DefiningOrgUnitId);

/// <summary>Body of POST and PUT /api/academic-years; <c>OrgUnitId</c> is ignored on update.</summary>
public sealed record SaveAcademicYearRequest(Guid OrgUnitId, string NameAr, string NameEn, DateOnly StartDate, DateOnly EndDate);

/// <summary>Body of POST /api/academic-years/{id}/terms and PUT /api/terms/{id}.</summary>
public sealed record SaveTermRequest(string NameAr, string NameEn, DateOnly StartDate, DateOnly EndDate);
