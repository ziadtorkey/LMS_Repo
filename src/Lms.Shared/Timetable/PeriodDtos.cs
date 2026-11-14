namespace Lms.Shared.Timetable;

/// <summary>
/// A period of a unit's school day (FR-TT-01). Times are the school's local wall-clock times (the client time zone, D64); periods of a unit
/// must not overlap (<c>period_overlap</c>). A unit sees the periods of its ancestors read only (<c>InheritedFromUnitId</c> set) unless it
/// defines its own set.
/// </summary>
public sealed record PeriodDto(
    Guid Id,
    Guid OrgUnitId,
    string NameAr,
    string NameEn,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Order,
    Guid? InheritedFromUnitId);

/// <summary>POST / PUT of a period: names 1-100 characters, <c>StartTime</c> before <c>EndTime</c>, order 1-20.</summary>
public sealed record SavePeriodRequest(
    string NameAr,
    string NameEn,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int Order);
