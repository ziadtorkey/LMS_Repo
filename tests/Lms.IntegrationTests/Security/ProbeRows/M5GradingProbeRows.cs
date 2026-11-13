using static Lms.IntegrationTests.Security.M5ProbeKit;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M5 grading scales, weight templates, the term structure, the gradebook with its publish, import and export routes, the student and guardian
/// grade reads and the reports (plan #621 route table, Tasks 4 to 12). Pending until the wave 1 contract and the named BE task map the route.
/// Patterns are spelled as the plan's table and are adjusted to the mapped text when the contract lands (the M5 matrix fails a mapped M5 route
/// that has no row). Leak probes (hidden items on every student route) are Task 41.
/// </summary>
public sealed class M5GradingProbeRows : IProbeRowModule
{
    private const string Scales = "/api/org-units/{id:guid}/grading-scales";
    private const string Templates = "/api/org-units/{id:guid}/weight-templates";
    private const string Book = "/api/class-courses/{id:guid}/gradebook";

    public IEnumerable<ProbeRow> Rows =>
    [
        Pending("m5 grading scales list", HttpMethod.Get, Scales, $"{Wave1} + Task 4", UnitRead()),
        Pending("m5 grading scales create", HttpMethod.Post, Scales, $"{Wave1} + Task 4", UnitWrite(Created)),
        Pending("m5 grading scales update", HttpMethod.Put, Scales + "/{sid:guid}", $"{Wave1} + Task 4", UnitWrite(Ok)),
        Pending("m5 grading scales delete", HttpMethod.Delete, Scales + "/{sid:guid}", $"{Wave1} + Task 4", UnitWrite(NoContent)),
        Pending("m5 grading scales confirm", HttpMethod.Post, Scales + "/{sid:guid}/confirm", $"{Wave1} + Task 4 (L7)", UnitWrite(Ok)),
        Pending("m5 grading scales set default", HttpMethod.Post, Scales + "/{sid:guid}/default", $"{Wave1} + Task 4", UnitWrite(Ok)),
        Pending("m5 grading scales copy preset", HttpMethod.Post, Scales + "/presets", $"{Wave1} + Task 4 (D59)", UnitWrite(Created)),
        Pending("m5 weight templates list", HttpMethod.Get, Templates, $"{Wave1} + Task 4", UnitRead()),
        Pending("m5 weight templates create", HttpMethod.Post, Templates, $"{Wave1} + Task 4", UnitWrite(Created)),
        Pending("m5 weight templates update", HttpMethod.Put, Templates + "/{tid:guid}", $"{Wave1} + Task 4", UnitWrite(Ok)),
        Pending("m5 weight templates delete", HttpMethod.Delete, Templates + "/{tid:guid}", $"{Wave1} + Task 4", UnitWrite(NoContent)),
        Pending("m5 term structure read", HttpMethod.Get, "/api/academic-years/{id:guid}/term-structure", $"{Wave1} + Task 5 (D62)", UnitRead()),
        Pending("m5 term structure change", HttpMethod.Put, "/api/academic-years/{id:guid}/term-structure", $"{Wave1} + Task 5 (L8)", UnitWrite(Ok)),

        Pending("m5 gradebook grid", HttpMethod.Get, Book, $"{Wave1} + Task 8", ClassCourse(Ok)),
        Pending("m5 gradebook config", HttpMethod.Put, Book + "/config", $"{Wave1} + Task 8", ClassCourse(Ok)),
        Pending("m5 gradebook categories", HttpMethod.Put, Book + "/categories", $"{Wave1} + Task 8", ClassCourse(Ok)),
        Pending("m5 gradebook item create", HttpMethod.Post, Book + "/items", $"{Wave1} + Task 8", ClassCourse(Created)),
        Pending("m5 gradebook item update", HttpMethod.Put, Book + "/items/{iid:guid}", $"{Wave1} + Task 8", ClassCourse(Ok)),
        Pending("m5 gradebook item delete", HttpMethod.Delete, Book + "/items/{iid:guid}", $"{Wave1} + Task 8", ClassCourse(NoContent)),
        Pending("m5 gradebook entries", HttpMethod.Put, Book + "/entries", $"{Wave1} + Task 8 (L12 row version)", ClassCourse(Ok)),
        Pending("m5 gradebook publish", HttpMethod.Post, Book + "/items/publish", $"{Wave1} + Task 11 (L7 provisional scale)", ClassCourse(Ok)),
        Pending("m5 gradebook student view preview", HttpMethod.Get, Book + "/students/{sid:guid}/student-view", $"{Wave1} + Task 9 (L6)", ClassCourse(Ok)),
        Pending("m5 gradebook item export", HttpMethod.Get, Book + "/items/{iid:guid}/export", $"{Wave1} + Task 10 (L9)", ClassCourse(Ok)),
        Pending("m5 gradebook import preview", HttpMethod.Post, Book + "/items/{iid:guid}/import/preview", $"{Wave1} + Task 10 (L9)", ClassCourse(Ok)),
        Pending("m5 gradebook import apply", HttpMethod.Post, Book + "/items/{iid:guid}/import/apply", $"{Wave1} + Task 10 (L9)", ClassCourse(Ok)),

        // The student and guardian reads: published only (L6). Staff and unlinked guardians never reach them.
        Pending("m5 my grades", HttpMethod.Get, "/api/me/grades", $"{Wave1} + Task 9", StudentOnly()),
        // No M1 caller holds Grades.ViewOwn (it is Student and Guardian only), so every staff caller is 403 here; the guardian-link rows (404 for an unlinked guardian) are in M5RelationshipProbeTests.
        Pending("m5 student grades (guardian link)", HttpMethod.Get, "/api/students/{id:guid}/grades", $"{Wave1} + Task 9", StudentOnly()),
        Pending("m5 grade report", HttpMethod.Get, "/api/grade-reports/{kind}", $"{Wave1} + Task 9", StaffOnly()),
        Pending("m5 grade report export", HttpMethod.Get, "/api/grade-reports/{kind}/export", $"{Wave1} + Task 10", StaffOnly()),
    ];
}
