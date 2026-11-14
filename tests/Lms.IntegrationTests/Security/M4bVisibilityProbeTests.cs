using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.LiveSessions;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The relationship and visibility column of the M4b route table (M4b plan Task 20, Review Focus 1; PRD §4.2.3, FR-SEC-05, D42, D51). <see cref="M4bEndpointMatrixTests"/>
/// and the M1 matrix send every row with the M1 callers; this class sends each route with the callers the table names: the teacher of the class course, a teacher
/// of another class course, a student of group 1, a student of group 2, a student who is not enrolled, a guardian, the client's Org Admin and the admin of
/// another client. The oracle is the "Route, permission and relationship table" of the plan: a relationship or visibility miss is 404, a missing permission 403,
/// and a permission miss answers before a relationship miss (a stranger student on authoring is 403). Rows whose route is not mapped yet skip through
/// <see cref="M4bPending"/> (pinned, visible as PENDING) and run once the route lands. A row that is added without an expectation for a caller skips that caller.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4bVisibilityProbeTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private sealed record Target(string Url, object? Body);

    private sealed record Row(
        string Name, string? Pin, HttpMethod Method, Func<M4bWorld, Task<Target>> Build, IReadOnlyDictionary<M4bCaller, int[]> Expected);

    private static int[] X(params int[] statuses) => statuses;

    private static Dictionary<M4bCaller, int[]> Expect(
        int[] teacher, int[] otherTeacher, int[] group1, int[] group2, int[] stranger, int[] guardian, int[] admin, int[] clientB) => new()
    {
        [M4bCaller.Teacher] = teacher, [M4bCaller.OtherTeacher] = otherTeacher, [M4bCaller.StudentGroup1] = group1, [M4bCaller.StudentGroup2] = group2,
        [M4bCaller.StudentStranger] = stranger, [M4bCaller.Guardian] = guardian, [M4bCaller.Admin] = admin, [M4bCaller.ClientBAdmin] = clientB,
    };

    private static readonly int[] Ok = X(200), Created = X(201), Gone = X(200, 204), Nf = X(404), Forbidden = X(403), Conflict = X(409);

    private static ModuleSettingsInput Module() => LiveSessionTestKit.Module("Probe");

    private static readonly IReadOnlyList<Row> Rows =
    [
        // ---- mapped on main: class-course live sessions (M4b Task 4) ----
        new("class course live sessions list", null, HttpMethod.Get,
            w => Task.FromResult(new Target(Routes.ClassCourseLiveSessions(w.Content.ClassCourse.Id), null)),
            Expect(Ok, Nf, Ok, Ok, Nf, Forbidden, Ok, Nf)),
        new("class course live session create", null, HttpMethod.Post,
            w => Task.FromResult(new Target(Routes.ClassCourseLiveSessions(w.Content.ClassCourse.Id), LiveSessionTestKit.Single("Probe", LiveSessionTestKit.NextSunday(Random.Shared.Next(2, 40))))),
            Expect(Created, Nf, Forbidden, Forbidden, Forbidden, Forbidden, Created, Nf)),
        new("live session get (group 1 module)", null, HttpMethod.Get,
            async w => new Target(Routes.LiveSession(await w.NewGroup1SessionAsync()), null),
            Expect(Ok, Nf, Ok, Nf, Nf, Forbidden, Ok, Nf)),
        new("live session update", null, HttpMethod.Put,
            async w => new Target(Routes.LiveSession(await w.NewClassSessionAsync()), new UpdateLiveSessionRequest(Module(), LiveSessionTestKit.NextSunday(3), 45)),
            Expect(Ok, Nf, Forbidden, Forbidden, Forbidden, Forbidden, Ok, Nf)),
        new("live session delete", null, HttpMethod.Delete,
            async w => new Target(Routes.LiveSession(await w.NewClassSessionAsync()), null),
            Expect(Gone, Nf, Forbidden, Forbidden, Forbidden, Forbidden, Gone, Nf)),

        // ---- waiting for #544: join window, recording, participants, upcoming ----
        new("live session join (group 1 module, window not open)", M4bPending.Join, HttpMethod.Post,
            async w => new Target(Routes.LiveSessionJoin(await w.NewGroup1SessionAsync()), null),
            Expect(Ok, Nf, Conflict, Nf, Nf, Forbidden, Ok, Nf)),
        new("live session recording set", M4bPending.Recording, HttpMethod.Put,
            async w => new Target(Routes.LiveSessionRecording(await w.NewClassSessionAsync()), new RecordingLinkRequest("https://example.com/recording")),
            Expect(Gone, Nf, Forbidden, Forbidden, Forbidden, Forbidden, Gone, Nf)),
        new("live session participants", M4bPending.Participants, HttpMethod.Get,
            async w => new Target(Routes.LiveSessionParticipants(await w.NewClassSessionAsync()), null),
            Expect(Ok, Nf, Forbidden, Forbidden, Forbidden, Forbidden, Ok, Nf)),
        new("live sessions upcoming", M4bPending.Upcoming, HttpMethod.Get,
            w => Task.FromResult(new Target(Routes.LiveSessionsUpcoming, null)),
            Expect(Ok, Ok, Ok, Ok, Ok, Forbidden, Ok, Ok)), // filtered by relationship, never 404

        // ---- waiting for #576: package launch, preview, attempts (the fixtures need a Ready package and an attempt) ----
        new("package launch", M4bPending.Launch, HttpMethod.Post,
            _ => throw new NotImplementedException("write with #576: a Ready package attached as a visible module of the class course"),
            Expect(Nf, Nf, Ok, Nf, Nf, Forbidden, Nf, Nf)), // staff 404 (they use preview), group 2 and the stranger 404
        new("package preview", M4bPending.Preview, HttpMethod.Post,
            _ => throw new NotImplementedException("write with #576: a Ready package attached as a module of the class course"),
            Expect(Ok, Nf, Forbidden, Forbidden, Forbidden, Forbidden, Ok, Nf)),
        new("package attempt commit", M4bPending.Commit, HttpMethod.Post,
            _ => throw new NotImplementedException("write with #576: an in-progress attempt of student 1 (launch as group 1)"),
            Expect(Nf, Nf, Ok, Nf, Nf, Forbidden, Nf, Nf)), // only the attempt's student; another student, staff and preview tokens 404
        new("package attempt finish", M4bPending.Finish, HttpMethod.Post,
            _ => throw new NotImplementedException("write with #576: an in-progress attempt of student 1 (launch as group 1)"),
            Expect(Nf, Nf, Ok, Nf, Nf, Forbidden, Nf, Nf)),
    ];

    public static TheoryData<string> RowNames()
    {
        var data = new TheoryData<string>();
        foreach (var row in Rows)
        {
            data.Add(row.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RowNames))]
    public async Task Every_caller_gets_the_answer_of_the_route_table(string rowName)
    {
        var row = Rows.Single(r => r.Name == rowName);
        if (row.Pin is not null && M4bPending.Skip(factory, output, row.Pin))
        {
            return;
        }

        using var world = await M4bWorld.CreateAsync(factory, "M4V" + Math.Abs(rowName.GetHashCode()) % 10000);
        var wrong = new List<string>();
        Target? target = null;
        foreach (var (caller, accepted) in row.Expected)
        {
            target ??= await row.Build(world);
            using var response = await world.SendAsync(caller, row.Method, target.Url, target.Body);
            var status = (int)response.StatusCode;
            if (!accepted.Contains(status))
            {
                wrong.Add($"{caller}: {row.Method} {target.Url} → {status}, expected {string.Join(" or ", accepted)}. Body: {Truncate(await response.Content.ReadAsStringAsync())}");
            }

            // A successful change used the target up (a deleted session): the next caller gets a fresh one.
            if (row.Method != HttpMethod.Get && response.IsSuccessStatusCode)
            {
                target = null;
            }
        }

        Assert.True(wrong.Count == 0, $"{row.Name}: the route table (M4b plan) is not what the API does:\n" + string.Join('\n', wrong));
    }

    [Fact]
    public async Task A_student_lists_only_the_sessions_their_group_may_see_and_never_a_join_url()
    {
        using var world = await M4bWorld.CreateAsync(factory, "M4VL");
        var forGroup1 = await world.NewGroup1SessionAsync();
        var forAll = await world.NewClassSessionAsync();

        var one = await ListAsync(world, M4bCaller.StudentGroup1);
        var two = await ListAsync(world, M4bCaller.StudentGroup2);
        Assert.Contains(forGroup1, one.Select(s => s.Id));
        Assert.Contains(forAll, one.Select(s => s.Id));
        Assert.DoesNotContain(forGroup1, two.Select(s => s.Id)); // group 2 never learns the session exists
        Assert.Contains(forAll, two.Select(s => s.Id));

        // No DTO a student reads carries a meeting address (join and start URLs come from the join route only, Review Focus 2).
        foreach (var caller in new[] { M4bCaller.StudentGroup1, M4bCaller.StudentGroup2 })
        {
            foreach (var url in new[] { Routes.ClassCourseLiveSessions(world.Content.ClassCourse.Id), Routes.LiveSession(forAll) })
            {
                using var response = await world.SendAsync(caller, HttpMethod.Get, url);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("http", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("joinUrl", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("startUrl", body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static async Task<List<LiveSessionListItemDto>> ListAsync(M4bWorld world, M4bCaller caller)
    {
        using var response = await world.SendAsync(caller, HttpMethod.Get, Routes.ClassCourseLiveSessions(world.Content.ClassCourse.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Lms.Shared.PagedResult<LiveSessionListItemDto>>())!.Items.ToList();
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
