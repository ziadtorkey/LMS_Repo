using static Lms.IntegrationTests.Security.M5ProbeKit;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M5 attendance sessions, taking, history, the live proposal, reports and the own and guardian reads (plan #621 route table, Tasks 16 to 20, wave 3
/// contract). Substitute and former-teacher rows (dates, L15) are in <c>M5RelationshipProbeTests</c>; these rows use the M1 callers. Pending until the
/// wave 3 contract and the named task map the route.
/// </summary>
public sealed class M5AttendanceProbeRows : IProbeRowModule
{
    private const string Session = "/api/attendance-sessions/{id:guid}";

    /// <summary>A route of one attendance session: only its teacher of record or a substitute of that date; another unit or client 404.</summary>
    private static Dictionary<Caller, int> OfSession(int status) => Expect(status, status, NotFound, NotFound, NotFound, status);

    public IEnumerable<ProbeRow> Rows =>
    [
        Pending("m5 attendance sessions of a day", HttpMethod.Get, "/api/attendance/sessions", $"{Wave3} + Task 16", Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
        Pending("m5 attendance ad hoc session", HttpMethod.Post, "/api/class-courses/{id:guid}/attendance-sessions", $"{Wave3} + Task 16", ClassCourse(Created)),
        Pending("m5 attendance records read", HttpMethod.Get, Session + "/records", $"{Wave3} + Task 17", OfSession(Ok)),
        Pending("m5 attendance take", HttpMethod.Put, Session + "/records", $"{Wave3} + Task 17 (L12, L14 edit window)", OfSession(Ok)),
        Pending("m5 attendance history", HttpMethod.Get, Session + "/history", $"{Wave3} + Task 17", OfSession(Ok)),
        Pending("m5 attendance confirm live proposal", HttpMethod.Post, Session + "/confirm", $"{Wave3} + Task 18 (L11)", OfSession(Ok)),
        Pending("m5 attendance live mappings", HttpMethod.Put, Session + "/live-mappings", $"{Wave3} + Task 18 (L11)", OfSession(Ok)),
        Pending("m5 attendance report", HttpMethod.Get, "/api/attendance-reports/{kind}", $"{Wave3} + Task 20", StaffOnly()),
        Pending("m5 attendance report export", HttpMethod.Get, "/api/attendance-reports/{kind}/export", $"{Wave3} + Task 20 (L9 escaping)", StaffOnly()),
        Pending("m5 attendance alerts", HttpMethod.Get, "/api/attendance/alerts", $"{Wave3} + Task 20", ScopedList(Forbidden)),

        Pending("m5 my attendance", HttpMethod.Get, "/api/me/attendance", $"{Wave3} + Task 17", StudentOnly()),
        Pending("m5 student attendance (guardian link)", HttpMethod.Get, "/api/students/{sid:guid}/attendance", $"{Wave3} + Task 17", OwnerOnly()),
    ];
}
