namespace Lms.IntegrationTests.Security;

/// <summary>
/// The permission each M5 route must declare, as the route table of the M5 plan (#621) and the wave contracts (#625 for wave 1) fix it: the oracle
/// of <see cref="M5EndpointMatrixTests.Mapped_M5_routes_require_the_planned_permission"/>. A route may list several permissions where the
/// contract says "A or B" (the route declares one of them). Patterns are the mapped route text; when a wave contract spells a pattern differently
/// from the plan, change the key here and the row in <c>Security/ProbeRows/M5*ProbeRows.cs</c> together (<see cref="M5EndpointMatrixTests"/> fails when
/// the two lists drift). The permission names are strings on purpose: wave 2 and 3 constants do not exist yet.
/// </summary>
internal static class M5PermissionTable
{
    private const string Scales = "/api/org-units/{id:guid}/grading-scales";
    private const string Templates = "/api/org-units/{id:guid}/weight-templates";
    private const string Book = "/api/class-courses/{id:guid}/gradebook";
    private const string Table = "/api/classes/{id:guid}/timetable";
    private const string Session = "/api/attendance-sessions/{id:guid}";
    private const string Excuse = "/api/excuses/{id:guid}";

    private const string Configure = "Grades.ConfigureScales";
    private const string View = "Grades.View";
    private const string Manage = "Grades.Manage";

    public static readonly IReadOnlyDictionary<string, string[]> Planned = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        // Wave 1 (contract #625).
        [$"GET {Scales}"] = [View],
        [$"POST {Scales}"] = [Configure],
        [$"PUT {Scales}/{{sid:guid}}"] = [Configure],
        [$"DELETE {Scales}/{{sid:guid}}"] = [Configure],
        [$"POST {Scales}/{{sid:guid}}/confirm"] = [Configure],
        [$"POST {Scales}/{{sid:guid}}/default"] = [Configure],
        [$"POST {Scales}/presets"] = [Configure],
        [$"GET {Templates}"] = [View],
        [$"POST {Templates}"] = [Configure],
        [$"PUT {Templates}/{{tid:guid}}"] = [Configure],
        [$"DELETE {Templates}/{{tid:guid}}"] = [Configure],
        ["GET /api/academic-years/{id:guid}/term-structure"] = ["AcademicYears.View"],
        ["PUT /api/academic-years/{id:guid}/term-structure"] = ["AcademicYears.Update"],
        [$"GET {Book}"] = [Manage, View],
        [$"PUT {Book}/config"] = [Manage],
        [$"PUT {Book}/categories"] = [Manage],
        [$"POST {Book}/items"] = [Manage],
        [$"PUT {Book}/items/{{iid:guid}}"] = [Manage],
        [$"DELETE {Book}/items/{{iid:guid}}"] = [Manage],
        [$"PUT {Book}/entries"] = [Manage],
        [$"POST {Book}/items/publish"] = ["Grades.Publish"],
        [$"GET {Book}/students/{{sid:guid}}/student-view"] = [Manage],
        [$"GET {Book}/items/{{iid:guid}}/export"] = [Manage],
        [$"POST {Book}/items/{{iid:guid}}/import/preview"] = [Manage],
        [$"POST {Book}/items/{{iid:guid}}/import/apply"] = [Manage],
        ["GET /api/me/grades"] = ["Grades.ViewOwn"],
        ["GET /api/students/{id:guid}/grades"] = ["Grades.ViewOwn"],
        ["GET /api/grade-reports/{kind}"] = ["Grades.ViewReports"],
        ["GET /api/grade-reports/{kind}/export"] = ["Grades.ViewReports"],

        // Wave 2 (plan route table; confirm against contract Task 2).
        ["GET /api/org-units/{id:guid}/periods"] = ["Timetable.Configure", "Timetable.View"],
        ["POST /api/org-units/{id:guid}/periods"] = ["Timetable.Configure"],
        ["PUT /api/org-units/{id:guid}/periods/{pid:guid}"] = ["Timetable.Configure"],
        ["DELETE /api/org-units/{id:guid}/periods/{pid:guid}"] = ["Timetable.Configure"],
        [$"GET {Table}"] = ["Timetable.View"],
        [$"POST {Table}/slots"] = ["Timetable.Manage"],
        [$"PUT {Table}/slots/{{sid:guid}}"] = ["Timetable.Manage"],
        [$"DELETE {Table}/slots/{{sid:guid}}"] = ["Timetable.Manage"],
        [$"POST {Table}/validate"] = ["Timetable.Manage"],
        [$"POST {Table}/publish"] = ["Timetable.Manage"],
        [$"POST {Table}/copy-term"] = ["Timetable.Manage"],
        [$"GET {Table}/export"] = ["Timetable.View"],
        ["POST /api/timetable-slots/{id:guid}/exceptions"] = ["Timetable.Manage"],
        ["PUT /api/timetable-slots/{id:guid}/exceptions/{eid:guid}"] = ["Timetable.Manage"],
        ["DELETE /api/timetable-slots/{id:guid}/exceptions/{eid:guid}"] = ["Timetable.Manage"],
        ["GET /api/me/timetable"] = ["Timetable.ViewOwn"],
        ["GET /api/students/{id:guid}/timetable"] = ["Timetable.ViewOwn"],

        // Wave 3 (plan route table; confirm against contract Task 3).
        ["GET /api/attendance/sessions"] = ["Attendance.Take"],
        ["POST /api/class-courses/{id:guid}/attendance-sessions"] = ["Attendance.Take"],
        [$"GET {Session}/records"] = ["Attendance.Take", "Attendance.Edit", "Attendance.View"],
        [$"PUT {Session}/records"] = ["Attendance.Take", "Attendance.Edit"],
        [$"GET {Session}/history"] = ["Attendance.Take", "Attendance.Edit", "Attendance.View"],
        [$"POST {Session}/confirm"] = ["Attendance.Take"],
        [$"PUT {Session}/live-mappings"] = ["Attendance.Take"],
        ["GET /api/attendance-reports/{kind}"] = ["Attendance.View"],
        ["GET /api/attendance-reports/{kind}/export"] = ["Attendance.View"],
        ["GET /api/attendance/alerts"] = ["Excuses.Review", "Attendance.View"],
        ["GET /api/me/attendance"] = ["Attendance.ViewOwn"],
        ["GET /api/students/{id:guid}/attendance"] = ["Attendance.ViewOwn"],
        ["GET /api/students/{id:guid}/excuse-policy"] = ["Excuses.Submit"],
        ["GET /api/students/{id:guid}/excusable-sessions"] = ["Excuses.Submit"],
        ["POST /api/excuses"] = ["Excuses.Submit"],
        ["GET /api/me/excuses"] = ["Excuses.Submit"],
        [$"POST {Excuse}/withdraw"] = ["Excuses.Submit"],
        [$"GET {Excuse}/attachment-url"] = ["Excuses.Submit", "Excuses.Review"],
        ["GET /api/excuses/review"] = ["Excuses.Review"],
        [$"POST {Excuse}/approve"] = ["Excuses.Review"],
        [$"POST {Excuse}/reject"] = ["Excuses.Review"],
        [$"POST {Excuse}/escalate"] = ["Excuses.Escalate"],
        [$"GET {Excuse}/escalation-targets"] = ["Excuses.Review", "Excuses.Escalate"],
    };
}
