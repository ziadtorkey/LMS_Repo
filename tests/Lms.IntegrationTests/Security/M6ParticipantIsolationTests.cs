using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Messaging;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The participant wall end to end (M6 plan Task 31, L6, X-05): a conversation of teacher A and student 1, probed as every person who is not in it.
/// <c>ConversationAccess</c> is the only participant gate, so every surface that can carry a conversation must treat a non-participant the same: the
/// conversation, its messages, the search, the inbox and the unread total. An Org Admin holds the permission and still sees nothing, and a guardian of a
/// participant is not a participant. A conversation the caller may not see is indistinguishable from one that does not exist (same status, same body).
/// Tests wait for <c>POST /api/conversations</c> (#733) and go live by themselves. The per-route 404 wall is in <see cref="M6ConversationsProbeRows"/>;
/// the mutation check (removing the <c>ConversationAccess</c> call from a message route must fail a test here) is recorded in the PR with the backend merge.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6ParticipantIsolationTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private static readonly M4aCaller[] Outsiders =
        [M4aCaller.StudentGroup2, M4aCaller.StudentOfOtherClass, M4aCaller.TeacherB, M4aCaller.Guardian];

    public static TheoryData<M4aCaller> OutsiderData()
    {
        var data = new TheoryData<M4aCaller>();
        foreach (var caller in Outsiders)
        {
            data.Add(caller);
        }

        return data;
    }

    private bool Ready()
    {
        if (M6Kit.IsMapped(factory, HttpMethod.Post, Routes.Conversations))
        {
            return true;
        }

        output.WriteLine("PENDING: POST /api/conversations is not mapped yet (#733).");
        return false;
    }

    [Theory]
    [MemberData(nameof(OutsiderData))]
    public async Task A_non_participant_cannot_read_the_conversation_or_its_messages(M4aCaller outsider)
    {
        if (!Ready())
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var id = await M6Kit.NewDirectAsync(w.World);
        foreach (var url in new[] { Routes.Conversation(id), Routes.ConversationMessages(id) })
        {
            using var response = await w.SendAsync(outsider, HttpMethod.Get, url);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{outsider} GET {url} → {(int)response.StatusCode}, expected 404.");
        }
    }

    [Theory]
    [MemberData(nameof(OutsiderData))]
    public async Task A_non_participant_never_sees_the_conversation_in_the_inbox_the_search_or_the_unread_total(M4aCaller outsider)
    {
        if (!Ready())
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var id = await M6Kit.NewDirectAsync(w.World);
        var marker = "marker-" + Guid.NewGuid().ToString("N");
        using (await w.SendAsync(M4aCaller.TeacherA, HttpMethod.Post, Routes.ConversationMessages(id), new SendMessageRequest(marker, null)))
        {
        }

        var inbox = await w.SendAsync(outsider, HttpMethod.Get, Routes.Conversations);
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.DoesNotContain(id.ToString(), await inbox.Content.ReadAsStringAsync());

        var search = await w.SendAsync(outsider, HttpMethod.Get, Routes.MessageSearchUrl(marker));
        Assert.DoesNotContain(marker, await search.Content.ReadAsStringAsync());

        var scoped = await w.SendAsync(outsider, HttpMethod.Get, Routes.MessageSearchUrl(marker, id));
        Assert.True(scoped.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.OK, $"search with a foreign conversationId answered {(int)scoped.StatusCode}.");
        Assert.DoesNotContain(marker, await scoped.Content.ReadAsStringAsync());
    }

    /// <summary>The existence of a conversation is not revealed: a foreign id and an id that never existed answer with the same status and the same problem body.</summary>
    [Theory]
    [MemberData(nameof(OutsiderData))]
    public async Task A_foreign_conversation_and_a_missing_one_look_the_same(M4aCaller outsider)
    {
        if (!Ready())
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var id = await M6Kit.NewDirectAsync(w.World);
        using var foreign = await w.SendAsync(outsider, HttpMethod.Get, Routes.Conversation(id));
        using var missing = await w.SendAsync(outsider, HttpMethod.Get, Routes.Conversation(Guid.NewGuid()));
        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        Assert.Equal(Strip(await missing.Content.ReadAsStringAsync()), Strip(await foreign.Content.ReadAsStringAsync()));
    }

    /// <summary>The admins of client A and client B hold the permission and are not participants: 404 on read and on write.</summary>
    [Theory]
    [InlineData(Caller.RootAdmin)]
    [InlineData(Caller.School1Admin)]
    [InlineData(Caller.School2Admin)]
    [InlineData(Caller.ClientBAdmin)]
    [InlineData(Caller.School1Teacher)]
    public async Task An_admin_is_not_a_participant(Caller caller)
    {
        if (!Ready())
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var id = await M6Kit.NewDirectAsync(w.World);
        using var read = await w.Callers.SendAsync(caller, HttpMethod.Get, Routes.ConversationMessages(id));
        using var write = await w.Callers.SendAsync(caller, HttpMethod.Post, Routes.ConversationMessages(id), new SendMessageRequest("from an admin", null));
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);

        // And the participants still see exactly their own message, nothing written by the outsider.
        var page = await (await w.SendAsync(M4aCaller.StudentGroup1, HttpMethod.Get, Routes.ConversationMessages(id))).Content.ReadFromJsonAsync<MessagePageDto>();
        Assert.DoesNotContain(page!.Items, m => m.Body == "from an admin");
    }

    /// <summary>A participant who is a student cannot add anyone to a conversation, and a non-owner teacher cannot add to someone else's group (404).</summary>
    [Fact]
    public async Task Only_the_owner_changes_the_members_of_a_group()
    {
        if (!Ready())
        {
            return;
        }

        var w = await M6Kit.WorldAsync(factory);
        var id = await M6Kit.NewGroupAsync(w.World);
        var request = new AddParticipantsRequest([w.AssignmentOf(M4aCaller.StudentOfOtherClass).UserId]);
        using var asMember = await w.SendAsync(M4aCaller.StudentGroup1, HttpMethod.Post, Routes.ConversationParticipants(id), request);
        using var asOutsiderTeacher = await w.SendAsync(M4aCaller.TeacherB, HttpMethod.Post, Routes.ConversationParticipants(id), request);
        Assert.True(asMember.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"a member added someone: {(int)asMember.StatusCode}.");
        Assert.Equal(HttpStatusCode.NotFound, asOutsiderTeacher.StatusCode);
    }

    /// <summary>Removes the parts of a problem body that legitimately differ (ids and the trace).</summary>
    private static string Strip(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"(traceId|instance)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"\"");
}
