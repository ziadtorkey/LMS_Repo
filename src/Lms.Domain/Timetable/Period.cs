using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Timetable;

/// <summary>
/// A period of a unit's school day (FR-TT-01). Times are local wall-clock times of the client time zone (D64), never UTC. A unit that defines
/// periods uses only its own set; a unit with none inherits the nearest ancestor's set read only (D58 Q4).
/// </summary>
[Audited]
public sealed class Period : UnitOwnedEntity
{
    private Period()
    {
        NameAr = NameEn = string.Empty;
    }

    public Period(OrgUnit unit, string nameAr, string nameEn, TimeOnly startTime, TimeOnly endTime, int order)
    {
        PlaceIn(unit);
        NameAr = nameAr;
        NameEn = nameEn;
        StartTime = startTime;
        EndTime = endTime;
        Order = order;
    }

    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public int Order { get; private set; }

    public void Update(string nameAr, string nameEn, TimeOnly startTime, TimeOnly endTime, int order)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        StartTime = startTime;
        EndTime = endTime;
        Order = order;
    }

    /// <summary>True when the two periods share any minute (touching end to start is not an overlap).</summary>
    public static bool Overlaps(TimeOnly startA, TimeOnly endA, TimeOnly startB, TimeOnly endB) => startA < endB && startB < endA;
}
