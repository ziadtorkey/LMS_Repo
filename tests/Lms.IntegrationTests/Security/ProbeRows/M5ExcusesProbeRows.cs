using static Lms.IntegrationTests.Security.M5ProbeKit;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M5 absence excuses: policy, submission, tracking, the counselor review queue, escalation and attachments (plan #621 route table, Task 19, wave 3
/// contract). Submission is for a linked guardian or, under <c>Excuses.Submitters = GuardiansAndStudents</c>, the student, so no admin or teacher
/// of the M1 world reaches it; the review routes are for staff holding <c>Excuses.Review</c> in the student's subtree. The attachment URL answers the
/// submitter or a linked guardian (Submit) <i>or</i> a reviewer of the subtree (Review), per the QA review of the plan. Pending until the wave 3
/// contract and Task 19 map the routes; the attack tests on attachments are Task 41.
/// </summary>
public sealed class M5ExcusesProbeRows : IProbeRowModule
{
    private const string Excuse = "/api/excuses/{id:guid}";

    public IEnumerable<ProbeRow> Rows =>
    [
        Pending("m5 excuse policy of a student", HttpMethod.Get, "/api/students/{sid:guid}/excuse-policy", $"{Wave3} + Task 19", OwnerOnly()),
        Pending("m5 excusable sessions of a student", HttpMethod.Get, "/api/students/{sid:guid}/excusable-sessions", $"{Wave3} + Task 19", OwnerOnly()),
        Pending("m5 excuse submit", HttpMethod.Post, "/api/excuses", $"{Wave3} + Task 19 (D61 submitters)", OwnerOnly()),
        Pending("m5 my excuses", HttpMethod.Get, "/api/me/excuses", $"{Wave3} + Task 19", StudentOnly()),
        Pending("m5 excuse withdraw", HttpMethod.Post, Excuse + "/withdraw", $"{Wave3} + Task 19", OwnerOnly()),
        Pending("m5 excuse attachment url", HttpMethod.Get, Excuse + "/attachment-url", $"{Wave3} + Task 19 (L10)", StaffOnly()),

        Pending("m5 excuse review queue", HttpMethod.Get, "/api/excuses/review", $"{Wave3} + Task 19", ScopedList(Forbidden)),
        Pending("m5 excuse approve", HttpMethod.Post, Excuse + "/approve", $"{Wave3} + Task 19", StaffOnly()),
        Pending("m5 excuse reject", HttpMethod.Post, Excuse + "/reject", $"{Wave3} + Task 19", StaffOnly()),
        Pending("m5 excuse escalate", HttpMethod.Post, Excuse + "/escalate", $"{Wave3} + Task 19 (L14)", StaffOnly()),
        Pending("m5 excuse escalation targets", HttpMethod.Get, Excuse + "/escalation-targets", $"{Wave3} + Task 19 (L14)", StaffOnly()),
    ];
}
