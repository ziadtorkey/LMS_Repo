using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Builders for the M5 probe rows that wait for their route (M5 plan #621 Task 40). A pending row carries the route exactly as the route table
/// of the plan spells it, the expected status per M1 caller (the oracle of that table), and a <see cref="ProbeRow.Blocker"/> naming the wave
/// contract and the BE task that add the route. It cannot be sent: its builder throws until the owning task replaces the row by a live one
/// (same row name and pattern, a real <c>Build</c>, no <c>Blocker</c>). When the route is mapped, <see cref="M1EndpointMatrixTests"/> picks the
/// row up and calls the builder, so a pending row for a mapped route fails loudly instead of passing silently.
/// </summary>
internal static class M5ProbeKit
{
    public const string Wave1 = "wave 1 contract (Task 1)";
    public const string Wave2 = "wave 2 contract (Task 2)";
    public const string Wave3 = "wave 3 contract (Task 3)";

    public static ProbeRow Pending(string name, HttpMethod method, string pattern, string blocker, IReadOnlyDictionary<Caller, int> expected) =>
        new(name, method, pattern,
            (_, _) => Task.FromException<ProbeRequest>(new NotSupportedException($"M5 probe row '{name}' is pending ({blocker}): replace it with a live row.")),
            expected)
        {
            Milestone = ProbeMilestones.M5,
            Blocker = blocker,
        };

    // The oracles, in the order root admin, School1 admin, School2 admin, School1 teacher, client B admin, platform admin (anonymous is 401).

    /// <summary>A class-course route: the teacher of the M1 world has no assignment on a fresh class course (404), another unit or client is 404.</summary>
    public static Dictionary<Caller, int> ClassCourse(int status) => Expect(status, status, NotFound, NotFound, NotFound, status);

    /// <summary>A unit-scoped configuration write: the teacher lacks the permission (403), another unit or client is 404.</summary>
    public static Dictionary<Caller, int> UnitWrite(int status) => Expect(status, status, NotFound, Forbidden, NotFound, status);

    /// <summary>A unit-scoped read a Teacher may make (a View permission), other units and clients 404.</summary>
    public static Dictionary<Caller, int> UnitRead() => Expect(Ok, Ok, NotFound, Ok, NotFound, Ok);

    /// <summary>A staff-only read or action behind a permission the Teacher lacks (reports, review queue): other units and clients 404.</summary>
    public static Dictionary<Caller, int> StaffOnly(int status = Ok) => Expect(status, status, NotFound, Forbidden, NotFound, status);

    /// <summary>A list filtered by the caller's own scope: every caller holding the permission gets 200 (possibly empty); the teacher may lack it.</summary>
    public static Dictionary<Caller, int> ScopedList(int teacher) => Expect(Ok, Ok, Ok, teacher, Ok, Ok);

    /// <summary>A route bound to one student that only a linked guardian or the student reaches: no admin or teacher has the link.</summary>
    public static Dictionary<Caller, int> OwnerOnly(int teacher = Forbidden) => Expect(NotFound, NotFound, NotFound, teacher, NotFound, NotFound);

    /// <summary>A "me" route that answers only a student (staff get 403).</summary>
    public static Dictionary<Caller, int> StudentOnly() => Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Forbidden);
}
