using System.Net;
using System.Net.Http.Json;
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

/// <summary>Message search (M6 Task 12, FR-MSG-01, L21): the caller own conversations only, Arabic and English, a 200-hit window, a rate limit.</summary>
[Collection(ApiCollection.Name)]
public sealed class MessageSearchTests(LmsApiFactory factory)
{
    private sealed record Setup(AccessTree Tree, RoleAssignment A, RoleAssignment B, RoleAssignment Outsider, Guid ConversationId);

    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(FeatureFlags.Messaging, tree.Root.Id, true, false);
        }

        async Task<RoleAssignment> StaffAsync(Lms.Domain.Organizations.OrgUnit unit, string role) =>
            await AccessTestData.AssignAsync(factory, tree, unit, role, (await PeopleTestKit.AddStaffAsync(factory, unit)).UserId);

        var a = await StaffAsync(tree.School1, BuiltInRoles.Teacher);
        var b = await StaffAsync(tree.School1, BuiltInRoles.Counselor);
        var outsider = await StaffAsync(tree.School1, BuiltInRoles.Teacher);
        var created = await SendAsync(factory, HttpMethod.Post, Routes.Conversations, a, new CreateConversationRequest(ConversationKinds.Direct, [b.UserId], null, null));
        return new Setup(tree, a, b, outsider, (await created.Content.ReadFromJsonAsync<ConversationDto>(Web))!.Id);
    }

    private async Task PostAsync(RoleAssignment who, Guid conversationId, string text) =>
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversationId), who, new SendMessageRequest(text, null))).StatusCode);

    private async Task<List<MessageSearchResultDto>> SearchAsync(RoleAssignment who, string q, Guid? conversationId = null, int page = 1)
    {
        var response = await SendAsync(factory, HttpMethod.Get, Routes.MessageSearchUrl(q, conversationId, page), who);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<MessageSearchResultDto>>(Web))!;
    }

    [Fact]
    public async Task Search_finds_arabic_and_english_words_and_never_leaves_the_callers_conversations()
    {
        var s = await SetUpAsync("SR1");
        await PostAsync(s.A, s.ConversationId, "Please bring the signed permission slip tomorrow");
        await PostAsync(s.A, s.ConversationId, "من فضلك أحضر الاستمارة الموقعة غدا");

        Assert.Single(await SearchAsync(s.B, "permission slip"));
        Assert.Single(await SearchAsync(s.B, "الاستمارة"));
        Assert.Single(await SearchAsync(s.A, "permission"));
        Assert.Empty(await SearchAsync(s.Outsider, "permission")); // a staff member in no conversation finds nothing
        Assert.Empty(await SearchAsync(s.B, "unrelatedword"));

        var response = await SendAsync(factory, HttpMethod.Get, Routes.MessageSearchUrl("permission"), s.B);
        Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Contains("permission", (await response.Content.ReadFromJsonAsync<List<MessageSearchResultDto>>(Web))!.Single().Snippet);
    }

    [Fact]
    public async Task Deleted_messages_are_not_searchable_and_a_one_character_query_is_400()
    {
        var s = await SetUpAsync("SR2");
        var sent = await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(s.ConversationId), s.A, new SendMessageRequest("secret zebra plan", null));
        var message = (await sent.Content.ReadFromJsonAsync<MessageDto>(Web))!;
        Assert.Single(await SearchAsync(s.B, "zebra"));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.Message(s.ConversationId, message.Id), s.A)).StatusCode);

        Assert.Empty(await SearchAsync(s.B, "zebra"));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Get, Routes.MessageSearchUrl("z"), s.B)).StatusCode);
    }

    [Fact]
    public async Task Search_window_stops_at_200_hits_and_the_conversation_filter_applies_on_every_page()
    {
        var s = await SetUpAsync("SR3");
        var other = await SendAsync(factory, HttpMethod.Post, Routes.Conversations, s.A, new CreateConversationRequest(ConversationKinds.Direct, [s.Outsider.UserId], null, "needle in another conversation"));
        var otherId = (await other.Content.ReadFromJsonAsync<ConversationDto>(Web))!.Id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var conversation = await db.Set<Conversation>().SingleAsync(c => c.Id == s.ConversationId);
            for (var i = 0; i < 210; i++)
            {
                db.Add(new Message(conversation, s.A.UserId, $"needle number {i}", DateTime.UtcNow.AddSeconds(i)));
            }

            await db.SaveChangesAsync();
        }

        Assert.Equal(50, (await SearchAsync(s.A, "needle")).Count);
        Assert.Equal(50, (await SearchAsync(s.A, "needle", page: 4)).Count);
        Assert.Empty(await SearchAsync(s.A, "needle", page: 5)); // 210 messages, but a search shows 200
        Assert.All(await SearchAsync(s.A, "needle", otherId), r => Assert.Equal(otherId, r.ConversationId));
        Assert.Single(await SearchAsync(s.A, "needle", otherId));
        Assert.Empty(await SearchAsync(s.B, "needle", otherId)); // B is not in that conversation
    }

    [Fact]
    public async Task Search_is_rate_limited_to_429()
    {
        var s = await SetUpAsync("SR4");

        HttpResponseMessage? last = null;
        for (var i = 0; i < MessageSearchLimit + 1; i++)
        {
            last = await SendAsync(factory, HttpMethod.Get, Routes.MessageSearchUrl("anything"), s.B);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.True(last.Headers.Contains("Retry-After"));
    }

    private const int MessageSearchLimit = Lms.Infrastructure.Messaging.MessageSearchThrottle.PerMinute;
}
