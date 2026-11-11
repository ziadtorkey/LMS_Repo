using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.AcademicYears;

/// <summary>An academic year defined at one org unit; descendants inherit it unless they define their own (D17).</summary>
[Audited]
public sealed class AcademicYear : AuditableEntity, IOrgUnitScoped
{
    private AcademicYear()
    {
        NameAr = NameEn = OrgUnitPath = string.Empty;
    }

    public AcademicYear(OrgUnit unit, string nameAr, string nameEn, DateOnly startDate, DateOnly endDate)
    {
        OrganizationId = unit.OrganizationId;
        OrgUnitId = unit.Id;
        OrgUnitPath = unit.Path;
        NameAr = nameAr;
        NameEn = nameEn;
        StartDate = startDate;
        EndDate = endDate;
        Status = AcademicYearStatus.Planned;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public AcademicYearStatus Status { get; private set; }

    public void Update(string nameAr, string nameEn, DateOnly startDate, DateOnly endDate)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        StartDate = startDate;
        EndDate = endDate;
    }

    public void Activate() => Status = AcademicYearStatus.Active;

    /// <summary>Closing a year is FR-YEAR-04 (M7); the state exists now so Closed years are read-only from the start.</summary>
    public void Close() => Status = AcademicYearStatus.Closed;

    public bool Overlaps(DateOnly start, DateOnly end) => StartDate <= end && start <= EndDate;
}
