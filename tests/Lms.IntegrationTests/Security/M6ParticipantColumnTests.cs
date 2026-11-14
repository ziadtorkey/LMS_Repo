using System.Net;
using Lms.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The positive column of the conversation rows (QA1's review of #770, item 2): <see cref="M6ConversationsProbeRows"/> shows the wall (every non-participant
/// gets 404), and a route that answers 404 to everyone would pass it. Here the same row, built the same way, is sent by the person it belongs to (the owner
/// teacher or a student member) and must <b>work</b>. Together the two show the wall standing around a working route. The expectation per row is written
/// out below, not derived: a row that is added to <see cref="M6ConversationsProbeRows"/> without one fails <see cref="Every_conversation_row_has_a_participant_expectation"/>.
/// Waits for <c>POST /api/conversations</c> (#733) through <see cref="M6Pending"/>: skipped while pinned, run once the route is on the branch.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6ParticipantColumnTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>Row name → who sends it (a participant of the conversation the row builds) and the statuses that mean "it worked".</summary>
    private static readonly Dictionary<string, (M4aCaller Who, int[] Ok)> Expected = new()
    {
        ["conversation get"] = (M4aCaller.StudentGroup1, [200]),
        ["conversation messages list"] = (M4aCaller.StudentGroup1, [200]),
        ["conversation message send"] = (M4aCaller.TeacherA, [200, 201]),
        ["conversation message delete"] = (M4aCaller.TeacherA, [200, 204]), // the sender, inside the 15-minute window
        ["conversation mute"] = (M4aCaller.StudentGroup1, [200, 204]),
        ["conversation unmute"] = (M4aCaller.StudentGroup1, [200, 204]),
        ["conversation leave"] = (M4aCaller.StudentGroup2, [200, 204]), // a member leaves the group (it keeps three members)
        ["conversation mark read"] = (M4aCaller.StudentGroup1, [200, 204]),
        ["conversation participants add"] = (M4aCaller.TeacherA, [200, 201]), // the owner adds an allowed member (see Body)
        ["conversation participant remove"] = (M4aCaller.TeacherA, [200, 204]), // the owner removes a member (see Target)
        ["conversation read-only set"] = (M4aCaller.TeacherA, [200]),
        ["conversation read-only clear"] = (M4aCaller.TeacherA, [200]),
    };

    private static IEnumerable<ProbeRow> Rows => new M6ConversationsProbeRows().Rows;

    public static TheoryData<string> RowNames()
    {
        var data = new TheoryData<string>();
        foreach (var row in Rows)
        {
            data.Add(row.Name);
        }

        return data;
    }

    [Fact]
    public void Every_conversation_row_has_a_participant_expectation()
    {
        var missing = Rows.Select(r => r.Name).Where(n => !Expected.ContainsKey(n)).ToList();
        var stale = Expected.Keys.Where(k => Rows.All(r => r.Name != k)).ToList();
        Assert.True(missing.Count == 0, "Conversation rows without a participant expectation: " + string.Join(", ", missing));
        Assert.True(stale.Count == 0, "Participant expectations without a row: " + string.Join(", ", stale));
    }

    [Theory]
    [MemberData(nameof(RowNames))]
    public async Task The_participant_gets_a_working_route(string rowName)
    {
        if (await M6Pending.SkipAsync(factory, output, M6Pending.CreateConversation))
        {
            return;
        }

        var row = Rows.Single(r => r.Name == rowName);
        var (who, ok) = Expected[rowName];
        var w = await M6Kit.WorldAsync(factory);
        var request = await row.Build(w.World, Caller.RootAdmin);
        var body = request.Body;
        var url = request.Url;
        if (rowName == "conversation participants add")
        {
            // The row's body names a made-up user (right for the wall: the gate answers first). For the owner the person must be real and allowed: teacher B (staff of the same unit).
            body = new Lms.Shared.Messaging.AddParticipantsRequest([w.AssignmentOf(M4aCaller.TeacherB).UserId]);
        }
        else if (rowName == "conversation participant remove")
        {
            url = url[..url.LastIndexOf('/')] + "/" + w.AssignmentOf(M4aCaller.StudentGroup2).UserId;
        }

        using var response = await w.SendAsync(who, row.Method, url, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(ok.Contains((int)response.StatusCode),
            $"{row.Name}: {row.Method} {url} as its participant {who} → {(int)response.StatusCode} (expected {string.Join(" or ", ok)}). The wall rows prove 404 for outsiders; this proves the route works. Body: {text}");
    }
}
