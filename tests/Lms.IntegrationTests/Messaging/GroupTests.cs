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
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Messaging;

/// <summary>Group conversations, members, read-only and the paged inbox (M6 Task 10, FR-MSG-01, D66, D74, L6).</summary>
[Collection(ApiCollection.Name)]
public sealed class GroupTests(LmsApiFactory factory)
{
    private sealed record Setup(AccessTree Tree, RoleAssignment Owner, RoleAssignment B, RoleAssignment C, RoleAssignment D, RoleAssignment Outsider);

    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(FeatureFlags.Messaging, tree.Root.Id, true, false);
        }

        async Task<RoleAssignment> StaffAsync(Lms.Domain.Organizations.OrgUnit unit, string role) =>
            await AccessTestData.AssignAsync(factory, tree, unit, role, (await PeopleTestKit.AddStaffAsync(factory, unit)).UserId);

        return new Setup(tree, await StaffAsync(tree.School1, BuiltInRoles.Teacher), await StaffAsync(tree.School1, BuiltInRoles.Counselor), await StaffAsync(tree.School1, BuiltInRoles.Teacher),
            await StaffAsync(tree.School1, BuiltInRoles.Teacher), await StaffAsync(tree.School2, BuiltInRoles.Teacher));
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>(Web))!;

    private Task<HttpResponseMessage> CreateGroupAsync(RoleAssignment who, string? title, params Guid[] members) =>
        SendAsync(factory, HttpMethod.Post, Routes.Conversations, who, new CreateConversationRequest(ConversationKinds.Group, members, title, null));

    [Fact]
    public async Task A_group_needs_three_members_and_every_member_is_a_participant()
    {
        var s = await SetUpAsync("MG1");

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateGroupAsync(s.Owner, "Two", s.B.UserId)).StatusCode); // owner and one other: a private pair is not a group

        var created = await CreateGroupAsync(s.Owner, "Grade 5 staff", s.B.UserId, s.C.UserId);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var group = await ReadAsync<ConversationDto>(created);
        Assert.Equal((ConversationKinds.Group, s.Owner.UserId, 3), (group.Kind, group.OwnerUserId, group.Participants.Count));

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Get, Routes.Conversation(group.Id), s.B)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.Conversation(group.Id), s.D)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.Conversation(group.Id), s.Outsider)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateGroupAsync(s.Owner, "Ghost", s.B.UserId, Guid.NewGuid())).StatusCode); // someone outside the client: no name, no reason
    }

    [Fact]
    public async Task Only_the_owner_adds_and_removes_members_and_a_group_never_drops_below_three()
    {
        var s = await SetUpAsync("MG2");
        var group = await ReadAsync<ConversationDto>(await CreateGroupAsync(s.Owner, "Team", s.B.UserId, s.C.UserId));

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationParticipants(group.Id), s.B, new AddParticipantsRequest([s.D.UserId]))).StatusCode); // a member is not the owner
        var added = await SendAsync(factory, HttpMethod.Post, Routes.ConversationParticipants(group.Id), s.Owner, new AddParticipantsRequest([s.D.UserId]));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(4, (await ReadAsync<ConversationDto>(added)).Participants.Count);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Get, Routes.Conversation(group.Id), s.D)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.ConversationParticipant(group.Id, s.C.UserId), s.B)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.ConversationParticipant(group.Id, s.D.UserId), s.Owner)).StatusCode);
        var refused = await SendAsync(factory, HttpMethod.Delete, Routes.ConversationParticipant(group.Id, s.C.UserId), s.Owner); // would leave two
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // A removed member still reads what was sent before, posts nothing more, and can be added again.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(group.Id), s.D, new SendMessageRequest("hi", null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationParticipants(group.Id), s.Owner, new AddParticipantsRequest([s.D.UserId]))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(group.Id), s.D, new SendMessageRequest("back", null))).StatusCode);
    }

    [Fact]
    public async Task A_read_only_group_refuses_posts_until_the_owner_opens_it_again()
    {
        var s = await SetUpAsync("MG3");
        var group = await ReadAsync<ConversationDto>(await CreateGroupAsync(s.Owner, null, s.B.UserId, s.C.UserId));

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, Routes.ConversationReadOnly(group.Id), s.B)).StatusCode);
        var locked = await SendAsync(factory, HttpMethod.Put, Routes.ConversationReadOnly(group.Id), s.Owner);
        Assert.True((await ReadAsync<ConversationDto>(locked)).IsReadOnly);
        var refused = await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(group.Id), s.B, new SendMessageRequest("hi", null));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(ProblemCodes.group_read_only, (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        Assert.False((await ReadAsync<ConversationDto>(await SendAsync(factory, HttpMethod.Delete, Routes.ConversationReadOnly(group.Id), s.Owner))).IsReadOnly);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(group.Id), s.B, new SendMessageRequest("hi", null))).StatusCode);
    }

    [Fact]
    public async Task The_inbox_is_ordered_by_last_message_and_paged_without_overlap()
    {
        var s = await SetUpAsync("MG4");
        var ids = new List<Guid>();
        foreach (var other in new[] { s.B, s.C, s.D })
        {
            var created = await SendAsync(factory, HttpMethod.Post, Routes.Conversations, s.Owner, new CreateConversationRequest(ConversationKinds.Direct, [other.UserId], null, "first"));
            ids.Add((await ReadAsync<ConversationDto>(created)).Id);
        }

        await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(ids[0]), s.Owner, new SendMessageRequest("newest", null)); // the oldest conversation moves to the top

        var inbox = await ReadAsync<List<ConversationListItemDto>>(await SendAsync(factory, HttpMethod.Get, Routes.Conversations, s.Owner));
        Assert.Equal([ids[0], ids[2], ids[1]], inbox.Select(c => c.Id));

        var first = await ReadAsync<List<ConversationListItemDto>>(await SendAsync(factory, HttpMethod.Get, Routes.Conversations + "?page=1&pageSize=2", s.Owner));
        var second = await ReadAsync<List<ConversationListItemDto>>(await SendAsync(factory, HttpMethod.Get, Routes.Conversations + "?page=2&pageSize=2", s.Owner));
        Assert.Equal([ids[0], ids[2]], first.Select(c => c.Id));
        Assert.Equal([ids[1]], second.Select(c => c.Id));
    }
}
