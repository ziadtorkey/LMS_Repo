using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The D69 safeguarding-first matrix (M6 plan Task 31, PRD D69, L18, L25): who may write to whom, probed through every entry path. Two layers:
/// <list type="number">
/// <item><b>The oracle</b> is written out here as a literal 7 x 7 grid from the plan table (L4; the PRD states the rule in words) (S = SameUnit, R = Relationship, D = Deny; sender row, recipient column),
/// independent of <see cref="MessagingRuleDefaults"/>, so a drift of the contract default fails here (the corrected SameUnit cells included: staff to staff,
/// manager and Org Admin to guardians). Runnable now.</item>
/// <item><b>The API probes</b> create a direct conversation and search recipients as each pair of callers of the M4a world (teachers A and B, three
/// students, the guardian of student 1) and require the answer the oracle and the relationships of that world give: allowed = 200 or 201 and the person is
/// in the recipient search; denied = 403 <c>messaging_not_allowed</c> and the person is not in the recipient search (never a name, never a reason that says
/// which rule). They wait for <c>POST /api/conversations</c> and <c>GET /api/messaging/recipients</c> (#733, #747) and go live by themselves. The group
/// and add-member entry paths and the <c>?to=</c> deep link (a client route that reads the recipient search) follow with the groups slice.</item>
/// </list>
/// Mutation checks (to run with the backend, recorded in the PR): removing the <c>min(stored, default)</c> in the policy, or the relationship check, fails
/// the denied rows.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6SafeguardingMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private static readonly string[] Order =
        [BuiltInRoles.Student, BuiltInRoles.Guardian, BuiltInRoles.Teacher, BuiltInRoles.Counselor, BuiltInRoles.Supervisor, BuiltInRoles.Manager, BuiltInRoles.OrgAdmin];

    /// <summary>The plan table of D69 (L4) as a grid. Rows: sender; columns in <see cref="Order"/>.</summary>
    private static readonly string[] Grid =
    [
        "DDRRDDD", // Student: only their own teachers and counselor
        "DDRRDDD", // Guardian: only the teachers and counselor of their own children, never another guardian
        "RRSSSSS", // Teacher: own students and their guardians, staff in the same unit
        "RRSSSSS", // Counselor
        "RRSSSSS", // Supervisor
        "DSSSSSS", // Manager: staff and guardians, a student only through a group or an announcement
        "DSSSSSS", // Org Admin
    ];

    private static string Mode(char c) => c switch
    {
        'D' => MessagingRuleModes.Deny,
        'R' => MessagingRuleModes.Relationship,
        'S' => MessagingRuleModes.SameUnit,
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    public static TheoryData<string, string, string> Cells()
    {
        var data = new TheoryData<string, string, string>();
        for (var from = 0; from < Order.Length; from++)
        {
            for (var to = 0; to < Order.Length; to++)
            {
                data.Add(Order[from], Order[to], Mode(Grid[from][to]));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Contract_default_equals_the_plan_matrix(string from, string to, string expected) =>
        Assert.Equal(expected, MessagingRuleDefaults.Default(from, to));

    [Fact]
    public void The_whole_default_matrix_is_the_grid() =>
        Assert.Equal(Order.Length * Order.Length, MessagingRuleDefaults.Matrix().Count);

    /// <summary>The SameUnit cells the review corrected: staff reach staff of the same unit, and a manager or Org Admin reaches guardians.</summary>
    [Theory]
    [InlineData(BuiltInRoles.Teacher, BuiltInRoles.Teacher)]
    [InlineData(BuiltInRoles.Teacher, BuiltInRoles.Counselor)]
    [InlineData(BuiltInRoles.Counselor, BuiltInRoles.Supervisor)]
    [InlineData(BuiltInRoles.Supervisor, BuiltInRoles.Manager)]
    [InlineData(BuiltInRoles.Manager, BuiltInRoles.Teacher)]
    [InlineData(BuiltInRoles.Manager, BuiltInRoles.Guardian)]
    [InlineData(BuiltInRoles.OrgAdmin, BuiltInRoles.Guardian)]
    [InlineData(BuiltInRoles.OrgAdmin, BuiltInRoles.Manager)]
    public void Corrected_cells_are_same_unit(string from, string to) =>
        Assert.Equal(MessagingRuleModes.SameUnit, MessagingRuleDefaults.Default(from, to));

    /// <summary>The safeguarding floor: no cell lets a student write to a student or a guardian to a guardian, and no manager or Org Admin cell reaches a student one to one.</summary>
    [Fact]
    public void The_floor_holds_for_every_stored_value()
    {
        foreach (var looser in new[] { MessagingRuleModes.SameUnit, MessagingRuleModes.Allow })
        {
            var cells = new[]
            {
                new MessagingRuleCellDto(BuiltInRoles.Student, BuiltInRoles.Student, looser),
                new MessagingRuleCellDto(BuiltInRoles.Guardian, BuiltInRoles.Guardian, looser),
                new MessagingRuleCellDto(BuiltInRoles.Student, BuiltInRoles.Guardian, looser),
                new MessagingRuleCellDto(BuiltInRoles.Manager, BuiltInRoles.Student, looser),
                new MessagingRuleCellDto(BuiltInRoles.OrgAdmin, BuiltInRoles.Student, looser),
            };
            Assert.Equal(cells.Length, MessagingRuleDefaults.Loosened(cells).Count);
        }
    }

    /// <summary>A client may narrow every cell to Deny; that is never "looser".</summary>
    [Fact]
    public void Narrowing_to_deny_is_always_accepted() =>
        Assert.Empty(MessagingRuleDefaults.Loosened(MessagingRuleDefaults.Matrix().Select(c => c with { Mode = MessagingRuleModes.Deny })));

    // ---- API probes through the entry paths (wait for the routes) ----

    /// <summary>What the oracle and the relationships of the M4a world say about a start from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static bool Allowed(M4aCaller from, M4aCaller to) => (from, to) switch
    {
        (M4aCaller.StudentGroup1, M4aCaller.TeacherA) => true, // student 1 is taught by A
        (M4aCaller.StudentGroup2, M4aCaller.TeacherA) => true, // so is student 2
        (M4aCaller.StudentOfOtherClass, M4aCaller.TeacherB) => true,
        (M4aCaller.Guardian, M4aCaller.TeacherA) => true, // guardian of student 1
        (M4aCaller.TeacherA, M4aCaller.StudentGroup1 or M4aCaller.StudentGroup2) => true,
        (M4aCaller.TeacherB, M4aCaller.StudentOfOtherClass) => true,
        (M4aCaller.TeacherA, M4aCaller.Guardian) => true,
        (M4aCaller.TeacherA, M4aCaller.TeacherB) or (M4aCaller.TeacherB, M4aCaller.TeacherA) => true, // staff of one unit
        _ => false, // everything else: student to student, student to a teacher who does not teach them, guardian to anyone else, teacher to an unrelated student
    };

    public static TheoryData<M4aCaller, M4aCaller> Pairs()
    {
        var data = new TheoryData<M4aCaller, M4aCaller>();
        foreach (var from in Enum.GetValues<M4aCaller>())
        {
            foreach (var to in Enum.GetValues<M4aCaller>().Where(t => t != from))
            {
                data.Add(from, to);
            }
        }

        return data;
    }
    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Create_direct_follows_the_matrix(M4aCaller from, M4aCaller to)
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.CreateConversation))
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        using var response = await w.SendAsync(from, HttpMethod.Post, Routes.Conversations,
            new CreateConversationRequest(ConversationKinds.Direct, [w.AssignmentOf(to).UserId], null, "hello"));
        var body = await response.Content.ReadAsStringAsync();
        if (Allowed(from, to))
        {
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"{from} → {to} should start a conversation, got {(int)response.StatusCode}: {body}");
        }
        else
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden && body.Contains(ProblemCodes.messaging_not_allowed),
                $"{from} → {to} must be refused with 403 messaging_not_allowed, got {(int)response.StatusCode}: {body}");

            // L18: a block, a rule and an ended relationship are one reason to the sender. A direct start never says "rule" or "relationship" (those come only
            // from an owner adding a member), so the sender learns nothing about which cell refused them.
            var reason = M6Pending.ReasonOf(body);
            Assert.True(reason is MessagingNotAllowedReasons.BlockedOrRule or MessagingNotAllowedReasons.Restricted,
                $"{from} → {to}: the reason must be '{MessagingNotAllowedReasons.BlockedOrRule}' (or '{MessagingNotAllowedReasons.Restricted}' for a restricted sender), got '{reason}'. Body: {body}");
        }
    }

    /// <summary>
    /// Every denied start looks the same to the sender (checklist #762 row 2.3): the same status, the same problem shape and the same body (trace id and instance
    /// aside), so the answer cannot tell a rule from a block from an ended relationship, or one denied person from another, by content or length.
    /// </summary>
    [Fact]
    public async Task Every_denied_start_has_the_same_body()
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.CreateConversation))
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var bodies = new Dictionary<string, string>();
        foreach (var from in Enum.GetValues<M4aCaller>())
        {
            foreach (var to in Enum.GetValues<M4aCaller>().Where(t => t != from && !Allowed(from, t)))
            {
                using var response = await w.SendAsync(from, HttpMethod.Post, Routes.Conversations,
                    new CreateConversationRequest(ConversationKinds.Direct, [w.AssignmentOf(to).UserId], null, "hello"));
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                bodies[$"{from} → {to}"] = M6Pending.Normalize(await response.Content.ReadAsStringAsync());
            }
        }

        Assert.NotEmpty(bodies);
        var first = bodies.First();
        var different = bodies.Where(b => b.Value != first.Value).Select(b => $"{b.Key} (length {b.Value.Length}) differs from {first.Key} (length {first.Value.Length}): {b.Value}").ToList();
        Assert.True(different.Count == 0, "Denied starts must answer with identical bodies:\n" + string.Join('\n', different));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Recipient_search_lists_exactly_the_people_the_matrix_allows(M4aCaller from, M4aCaller to)
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.Recipients))
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        using var response = await w.SendAsync(from, HttpMethod.Get, Routes.MessagingRecipients);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var people = await response.Content.ReadFromJsonAsync<List<RecipientDto>>() ?? [];
        var listed = people.Any(p => p.UserId == w.AssignmentOf(to).UserId);
        Assert.True(listed == Allowed(from, to), $"{from} → {to}: recipient search {(listed ? "lists" : "hides")} a person the matrix {(Allowed(from, to) ? "allows" : "denies")}.");
    }
}
