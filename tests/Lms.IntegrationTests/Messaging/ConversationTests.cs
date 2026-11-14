using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Messaging;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Messaging;

/// <summary>Direct conversations, messages, unread state and the participant gate (M6 Task 10, FR-MSG-01, D66, L6).</summary>
[Collection(ApiCollection.Name)]
public sealed class ConversationTests(LmsApiFactory factory)
{
    private sealed record Setup(AccessTree Tree, RoleAssignment Teacher, RoleAssignment Counselor, RoleAssignment Admin, RoleAssignment Outsider);

    /// <summary>A teacher and a counselor at School 1 (both with a staff profile), an Org Admin, and a teacher of School 2 who is in no conversation. Messaging is switched on.</summary>
    private async Task<Setup> SetUpAsync(string name, bool flagOn = true)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        if (flagOn)
        {
            using var scope = new EngineScope(factory.Services);
            await scope.Settings.SetAsync(FeatureFlags.Messaging, tree.Root.Id, true, false);
        }

        async Task<RoleAssignment> StaffAsync(Lms.Domain.Organizations.OrgUnit unit, string role)
        {
            var staff = await PeopleTestKit.AddStaffAsync(factory, unit);
            return await AccessTestData.AssignAsync(factory, tree, unit, role, staff.UserId);
        }

        return new Setup(tree, await StaffAsync(tree.School1, BuiltInRoles.Teacher), await StaffAsync(tree.School1, BuiltInRoles.Counselor),
            await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()), await StaffAsync(tree.School2, BuiltInRoles.Teacher));
    }

    private Task<HttpResponseMessage> CreateAsync(RoleAssignment who, Guid other, string? first = null) =>
        SendAsync(factory, HttpMethod.Post, Routes.Conversations, who, new CreateConversationRequest(ConversationKinds.Direct, [other], null, first));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Web))!;

    [Fact]
    public async Task Direct_pair_create_returns_the_existing_conversation_with_200()
    {
        var s = await SetUpAsync("MS1");

        var first = await CreateAsync(s.Teacher, s.Counselor.UserId, "hello");
        var again = await CreateAsync(s.Teacher, s.Counselor.UserId);
        var fromOther = await CreateAsync(s.Counselor, s.Teacher.UserId); // the same pair started from the other side

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromOther.StatusCode);
        var id = (await ReadAsync<ConversationDto>(first)).Id;
        Assert.Equal(id, (await ReadAsync<ConversationDto>(again)).Id);
        Assert.Equal(id, (await ReadAsync<ConversationDto>(fromOther)).Id);
        Assert.True(first.Headers.CacheControl is { NoStore: true, Private: true });
    }

    [Fact]
    public async Task Parallel_direct_creates_make_one_conversation()
    {
        var s = await SetUpAsync("MS2");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? CreateAsync(s.Teacher, s.Counselor.UserId) : CreateAsync(s.Counselor, s.Teacher.UserId)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, r.StatusCode.ToString()));
        Assert.Single((await Task.WhenAll(responses.Select(ReadAsync<ConversationDto>))).Select(c => c.Id).Distinct());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<Conversation>().CountAsync(c => c.DirectKey == Conversation.BuildDirectKey(s.Tree.Org.Id, s.Teacher.UserId, s.Counselor.UserId)));
    }

    [Fact]
    public async Task A_message_reaches_the_other_participant_and_unread_follows_the_last_read()
    {
        var s = await SetUpAsync("MS3");
        var conversation = await ReadAsync<ConversationDto>(await CreateAsync(s.Teacher, s.Counselor.UserId));

        var sent = await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Teacher, new SendMessageRequest("hello counselor", null));
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Teacher, new SendMessageRequest("second", null));

        var inbox = await ReadAsync<List<ConversationListItemDto>>(await SendAsync(factory, HttpMethod.Get, Routes.Conversations, s.Counselor));
        var row = inbox.Single(c => c.Id == conversation.Id);
        Assert.Equal((2, "second"), (row.UnreadCount, row.LastMessagePreview));
        Assert.Equal(0, (await ReadAsync<List<ConversationListItemDto>>(await SendAsync(factory, HttpMethod.Get, Routes.Conversations, s.Teacher))).Single(c => c.Id == conversation.Id).UnreadCount);
        Assert.Equal(1, (await ReadAsync<UnreadTotalDto>(await SendAsync(factory, HttpMethod.Get, Routes.MyUnreadTotal, s.Counselor))).Conversations);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Put, Routes.ConversationRead(conversation.Id), s.Counselor)).StatusCode);
        Assert.Equal(0, (await ReadAsync<List<ConversationListItemDto>>(await SendAsync(factory, HttpMethod.Get, Routes.Conversations, s.Counselor))).Single(c => c.Id == conversation.Id).UnreadCount);
        Assert.Equal(0, (await ReadAsync<UnreadTotalDto>(await SendAsync(factory, HttpMethod.Get, Routes.MyUnreadTotal, s.Counselor))).Conversations);
    }

    [Fact]
    public async Task Non_participant_org_admin_gets_404_on_every_conversation_route()
    {
        var s = await SetUpAsync("MS4");
        var conversation = await ReadAsync<ConversationDto>(await CreateAsync(s.Teacher, s.Counselor.UserId, "private words"));

        foreach (var who in new[] { s.Admin, s.Outsider })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.Conversation(conversation.Id), who)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(conversation.Id), who)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), who, new SendMessageRequest("hi", null))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, Routes.ConversationRead(conversation.Id), who)).StatusCode);
            Assert.DoesNotContain(await ReadAsync<List<ConversationListItemDto>>(await SendAsync(factory, HttpMethod.Get, Routes.Conversations, who)), c => c.Id == conversation.Id);
        }
    }

    [Fact]
    public async Task Keyset_pages_are_stable_under_new_messages_and_a_body_over_4000_is_refused()
    {
        var s = await SetUpAsync("MS5");
        var conversation = await ReadAsync<ConversationDto>(await CreateAsync(s.Teacher, s.Counselor.UserId));
        for (var i = 1; i <= 5; i++)
        {
            await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Teacher, new SendMessageRequest($"m{i}", null));
        }

        var first = await ReadAsync<MessagePageDto>(await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(conversation.Id, null, 2), s.Counselor));
        Assert.Equal(["m5", "m4"], first.Items.Select(m => m.Body));
        await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Teacher, new SendMessageRequest("m6", null)); // arrives between the pages
        var second = await ReadAsync<MessagePageDto>(await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(conversation.Id, first.NextBeforeId, 2), s.Counselor));
        Assert.Equal(["m3", "m2"], second.Items.Select(m => m.Body)); // not shifted by the new message

        var tooLong = await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Teacher, new SendMessageRequest(new string('x', 4001), null));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(ProblemCodes.message_too_long, (await tooLong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_left_member_reads_up_to_leaving_and_cannot_send_and_the_flag_off_is_403()
    {
        var s = await SetUpAsync("MS6");
        var conversation = await ReadAsync<ConversationDto>(await CreateAsync(s.Teacher, s.Counselor.UserId));
        await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Teacher, new SendMessageRequest("before", null));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<ConversationParticipant>().SingleAsync(p => p.ConversationId == conversation.Id && p.UserId == s.Counselor.UserId)).Leave(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        await Task.Delay(50);
        await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Teacher, new SendMessageRequest("after", null));

        var page = await ReadAsync<MessagePageDto>(await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(conversation.Id), s.Counselor));
        Assert.Equal(["before"], page.Items.Select(m => m.Body));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversation.Id), s.Counselor, new SendMessageRequest("hi", null))).StatusCode);

        var off = await SetUpAsync("MS7", flagOn: false);
        var refused = await CreateAsync(off.Teacher, off.Counselor.UserId);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(ProblemCodes.feature_disabled, (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Conversation_rows_are_isolated_by_client_and_by_participant()
    {
        var a = await SetUpAsync("MS8A");
        var b = await SetUpAsync("MS8B");
        var conversation = await ReadAsync<ConversationDto>(await CreateAsync(a.Teacher, a.Counselor.UserId, "x"));

        // Another client sees nothing, and a user of the same client who is not a participant sees nothing through the table either way (the gate is the service).
        await using var other = factory.CreateDbContext(TestCurrentContext.For(b.Tree.Org.Id, b.Tree.Root.Path));
        Assert.False(await other.Set<Conversation>().AnyAsync(c => c.Id == conversation.Id));
        Assert.False(await other.Set<ConversationParticipant>().AnyAsync(p => p.ConversationId == conversation.Id));
        Assert.False(await other.Set<Message>().AnyAsync(m => m.ConversationId == conversation.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.Conversation(conversation.Id), b.Teacher)).StatusCode);
        // A person of another client is never reachable (same answer as a forbidden pair).
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(a.Teacher, b.Counselor.UserId)).StatusCode);
    }
}
