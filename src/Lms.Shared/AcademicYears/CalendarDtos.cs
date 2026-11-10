namespace Lms.Shared.AcademicYears;

/// <summary>Kind of a calendar entry; a Holiday is a non-working day (FR-YEAR-03).</summary>
public static class CalendarEventKinds
{
    public const string Holiday = "Holiday";
    public const string Event = "Event";
}

/// <summary>A calendar entry of a year; <c>IsInherited</c> = defined at an ancestor of the unit being viewed.</summary>
public sealed record CalendarEventDto(
    Guid Id,
    Guid AcademicYearId,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    string TitleAr,
    string TitleEn,
    string Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsInherited);

/// <summary>Body of POST and PUT /api/calendar-events.</summary>
public sealed record SaveCalendarEventRequest(
    Guid AcademicYearId,
    Guid OrgUnitId,
    string TitleAr,
    string TitleEn,
    string Kind,
    DateOnly StartDate,
    DateOnly EndDate);
