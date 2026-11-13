using static Lms.IntegrationTests.Security.M5ProbeKit;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M5 periods, the timetable builder and views, and dated exceptions (plan #621 route table, Tasks 13 to 15, wave 2 contract). Rooms are free text
/// (D60), so there is no room route. The teacher's own timetable across classes is bypass site 3: its cross-client rows (a teacher of another client
/// and of a sibling school) are added to <c>M5RelationshipProbeTests</c> when Task 14 lands. Pending until the wave 2 contract and the named task map the route.
/// </summary>
public sealed class M5TimetableProbeRows : IProbeRowModule
{
    private const string Periods = "/api/org-units/{id:guid}/periods";
    private const string Table = "/api/classes/{id:guid}/timetable";

    public IEnumerable<ProbeRow> Rows =>
    [
        Pending("m5 periods list", HttpMethod.Get, Periods, $"{Wave2} + Task 13", UnitRead()),
        Pending("m5 periods create", HttpMethod.Post, Periods, $"{Wave2} + Task 13", UnitWrite(Created)),
        Pending("m5 periods update", HttpMethod.Put, Periods + "/{pid:guid}", $"{Wave2} + Task 13", UnitWrite(Ok)),
        Pending("m5 periods delete", HttpMethod.Delete, Periods + "/{pid:guid}", $"{Wave2} + Task 13", UnitWrite(NoContent)),

        Pending("m5 timetable read", HttpMethod.Get, Table, $"{Wave2} + Task 14", UnitRead()),
        Pending("m5 timetable slot create", HttpMethod.Post, Table + "/slots", $"{Wave2} + Task 14", UnitWrite(Created)),
        Pending("m5 timetable slot update", HttpMethod.Put, Table + "/slots/{sid:guid}", $"{Wave2} + Task 14 (L12 row version)", UnitWrite(Ok)),
        Pending("m5 timetable slot delete", HttpMethod.Delete, Table + "/slots/{sid:guid}", $"{Wave2} + Task 14", UnitWrite(NoContent)),
        Pending("m5 timetable validate", HttpMethod.Post, Table + "/validate", $"{Wave2} + Task 14", UnitWrite(Ok)),
        Pending("m5 timetable publish", HttpMethod.Post, Table + "/publish", $"{Wave2} + Task 14", UnitWrite(Ok)),
        Pending("m5 timetable copy term", HttpMethod.Post, Table + "/copy-term", $"{Wave2} + Task 14", UnitWrite(Ok)),
        Pending("m5 timetable export", HttpMethod.Get, Table + "/export", $"{Wave2} + Task 14 (L9 escaping)", UnitRead()),

        Pending("m5 timetable exception create", HttpMethod.Post, "/api/timetable-slots/{id:guid}/exceptions", $"{Wave2} + Task 15", UnitWrite(Created)),
        Pending("m5 timetable exception update", HttpMethod.Put, "/api/timetable-slots/{id:guid}/exceptions/{eid:guid}", $"{Wave2} + Task 15", UnitWrite(Ok)),
        Pending("m5 timetable exception delete", HttpMethod.Delete, "/api/timetable-slots/{id:guid}/exceptions/{eid:guid}", $"{Wave2} + Task 15", UnitWrite(NoContent)),

        // Own views: a user without slots gets an empty week, never another person's.
        Pending("m5 my timetable", HttpMethod.Get, "/api/me/timetable", $"{Wave2} + Task 14 (bypass site 3)", Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
        Pending("m5 student timetable (guardian link)", HttpMethod.Get, "/api/students/{id:guid}/timetable", $"{Wave2} + Task 14", OwnerOnly(NotFound)),
    ];
}
