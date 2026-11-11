using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.AcademicYears;

/// <summary>A term inside an academic year. Unit fields are copied from the year.</summary>
[Audited]
public sealed class Term : AuditableEntity, IOrgUnitScoped
{
    private Term()
    {
        NameAr = NameEn = OrgUnitPath = string.Empty;
    }

    public Term(AcademicYear year, string nameAr, string nameEn, DateOnly startDate, DateOnly endDate)
    {
        OrganizationId = year.OrganizationId;
        OrgUnitId = year.OrgUnitId;
        OrgUnitPath = year.OrgUnitPath;
        AcademicYearId = year.Id;
        NameAr = nameAr;
        NameEn = nameEn;
        StartDate = startDate;
        EndDate = endDate;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid AcademicYearId { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public int Order { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    public void Update(string nameAr, string nameEn, DateOnly startDate, DateOnly endDate)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        StartDate = startDate;
        EndDate = endDate;
    }

    public void SetOrder(int order) => Order = order;

    public bool Overlaps(DateOnly start, DateOnly end) => StartDate <= end && start <= EndDate;
}
