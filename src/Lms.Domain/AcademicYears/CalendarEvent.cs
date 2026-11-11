using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.AcademicYears;

/// <summary>A holiday or event of an academic year, defined at the year's unit or any descendant of it.</summary>
[Audited]
public sealed class CalendarEvent : AuditableEntity, IOrgUnitScoped
{
    private CalendarEvent()
    {
        TitleAr = TitleEn = OrgUnitPath = string.Empty;
    }

    public CalendarEvent(
        AcademicYear year, OrgUnit unit, string titleAr, string titleEn, CalendarEventKind kind, DateOnly startDate, DateOnly endDate)
    {
        OrganizationId = unit.OrganizationId;
        OrgUnitId = unit.Id;
        OrgUnitPath = unit.Path;
        AcademicYearId = year.Id;
        TitleAr = titleAr;
        TitleEn = titleEn;
        Kind = kind;
        StartDate = startDate;
        EndDate = endDate;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid AcademicYearId { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public CalendarEventKind Kind { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    public void Update(string titleAr, string titleEn, CalendarEventKind kind, DateOnly startDate, DateOnly endDate)
    {
        TitleAr = titleAr;
        TitleEn = titleEn;
        Kind = kind;
        StartDate = startDate;
        EndDate = endDate;
    }
}
