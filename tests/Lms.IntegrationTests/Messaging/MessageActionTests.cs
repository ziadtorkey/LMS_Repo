using System.Net;
using System.Net.Http.Headers;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Messaging;

/// <summary>Delete for everyone, mute, leave and attachments (M6 Task 10, L12, L20).</summary>
[Collection(ApiCollection.Name)]
public sealed class MessageActionTests(LmsApiFactory factory)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();

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
        var outsider = await StaffAsync(tree.School2, BuiltInRoles.Teacher);
        var created = await SendAsync(factory, HttpMethod.Post, Routes.Conversations, a, new CreateConversationRequest(ConversationKinds.Direct, [b.UserId], null, null));
        var conversation = (await created.Content.ReadFromJsonAsync<ConversationDto>(Web))!;
        return new Setup(tree, a, b, outsider, conversation.Id);
    }

    private async Task<MessageDto> SendTextAsync(RoleAssignment who, Guid conversationId, string text, IReadOnlyList<Guid>? files = null)
    {
        var response = await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversationId), who, new SendMessageRequest(text, files));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessageDto>(Web))!;
    }

    private async Task<HttpResponseMessage> UploadAsync(RoleAssignment who, Guid conversationId, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        var request = new HttpRequestMessage(HttpMethod.Post, MessageActionEndpointsRoutes.Attachments(conversationId)) { Content = form };
        request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, who.Id.ToString());
        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task SetSentAsync(Guid messageId, DateTime sentAtUtc)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var message = await db.Set<Message>().SingleAsync(m => m.Id == messageId);
        db.Entry(message).Property(nameof(Message.SentAtUtc)).CurrentValue = sentAtUtc;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Delete_own_message_within_the_window_leaves_a_tombstone_and_after_it_is_409()
    {
        var s = await SetUpAsync("MA1");
        var mine = await SendTextAsync(s.A, s.ConversationId, "oops");
        var theirs = await SendTextAsync(s.B, s.ConversationId, "reply");
        Assert.True(mine.CanDelete);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.Message(s.ConversationId, theirs.Id), s.A)).StatusCode); // not yours, not revealed
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.Message(s.ConversationId, mine.Id), s.A)).StatusCode);

        var page = (await (await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(s.ConversationId), s.B)).Content.ReadFromJsonAsync<MessagePageDto>(Web))!;
        var tombstone = page.Items.Single(m => m.Id == mine.Id);
        Assert.Equal((MessageStates.Deleted, null), (tombstone.State, tombstone.Body));

        var late = await SendTextAsync(s.A, s.ConversationId, "too late");
        await SetSentAsync(late.Id, DateTime.UtcNow.AddMinutes(-16));
        var refused = await SendAsync(factory, HttpMethod.Delete, Routes.Message(s.ConversationId, late.Id), s.A);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ProblemCodes.message_delete_window_closed, await CodeAsync(refused));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.Message(s.ConversationId, mine.Id), s.Outsider)).StatusCode);
    }

    [Fact]
    public async Task Mute_needs_a_future_date_and_leave_applies_to_groups_only()
    {
        var s = await SetUpAsync("MA2");

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Put, Routes.ConversationMute(s.ConversationId), s.B, new MuteConversationRequest(DateTime.UtcNow.AddMinutes(-1)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Put, Routes.ConversationMute(s.ConversationId), s.B, new MuteConversationRequest(null))).StatusCode);
        var inbox = (await (await SendAsync(factory, HttpMethod.Get, Routes.Conversations, s.B)).Content.ReadFromJsonAsync<List<ConversationListItemDto>>(Web))!;
        Assert.True(inbox.Single(c => c.Id == s.ConversationId).Muted);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Delete, Routes.ConversationMute(s.ConversationId), s.B)).StatusCode);
        Assert.False((await (await SendAsync(factory, HttpMethod.Get, Routes.Conversations, s.B)).Content.ReadFromJsonAsync<List<ConversationListItemDto>>(Web))!.Single(c => c.Id == s.ConversationId).Muted);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, Routes.ConversationMute(s.ConversationId), s.Outsider, new MuteConversationRequest(null))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationLeave(s.ConversationId), s.B)).StatusCode); // a direct conversation is muted, not left (409 conversation_closed)

        Guid group;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var conversation = Conversation.Group(s.Tree.Org.Id, s.A.UserId, "Staff", null, DateTime.UtcNow);
            db.Add(conversation);
            db.Add(new ConversationParticipant(conversation, s.A.UserId, DateTime.UtcNow));
            db.Add(new ConversationParticipant(conversation, s.B.UserId, DateTime.UtcNow));
            await db.SaveChangesAsync();
            group = conversation.Id;
        }

        await SendTextAsync(s.A, group, "before");
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationLeave(group), s.B)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationLeave(group), s.B)).StatusCode); // twice
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(group), s.B, new SendMessageRequest("after leaving", null))).StatusCode);
    }

    [Fact]
    public async Task An_attachment_is_uploaded_attached_and_served_through_a_short_lived_participant_only_url()
    {
        var s = await SetUpAsync("MA3");

        var upload = await UploadAsync(s.A, s.ConversationId, "notes.pdf", Pdf);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var file = (await upload.Content.ReadFromJsonAsync<MessageAttachmentDto>(Web))!;
        var sent = await SendTextAsync(s.A, s.ConversationId, "see attached", [file.FileId]);
        Assert.Equal(file.FileId, sent.Attachments.Single().FileId);

        var seen = (await (await SendAsync(factory, HttpMethod.Get, Routes.ConversationMessages(s.ConversationId), s.B)).Content.ReadFromJsonAsync<MessagePageDto>(Web))!;
        Assert.Equal("notes.pdf", seen.Items.Single().Attachments.Single().FileName);

        var url = await SendAsync(factory, HttpMethod.Get, MessageActionEndpointsRoutes.AttachmentUrl(s.ConversationId, sent.Id, file.FileId), s.B);
        Assert.Equal(HttpStatusCode.OK, url.StatusCode);
        Assert.True(url.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, MessageActionEndpointsRoutes.AttachmentUrl(s.ConversationId, sent.Id, file.FileId), s.Outsider)).StatusCode);

        // Deleted for everyone: the files are no longer served.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.Message(s.ConversationId, sent.Id), s.A)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, MessageActionEndpointsRoutes.AttachmentUrl(s.ConversationId, sent.Id, file.FileId), s.B)).StatusCode);
    }

    private sealed class RefusingScanner : Lms.Domain.Files.IFileScanner
    {
        public Task<bool> IsCleanAsync(string fileName, Stream content, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    [Fact]
    public async Task A_failed_scan_is_400_and_nothing_is_stored()
    {
        var s = await SetUpAsync("MA5");
        using var scanning = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<Lms.Domain.Files.IFileScanner>(new RefusingScanner()));
        }));
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "virus.pdf");
        var request = new HttpRequestMessage(HttpMethod.Post, MessageActionEndpointsRoutes.Attachments(s.ConversationId)) { Content = form };
        request.Headers.Add(TestAuthHandler.UserHeader, s.A.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, s.A.Id.ToString());

        var response = await scanning.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<MessageAttachment>().AnyAsync(a => a.ConversationId == s.ConversationId && a.FileName == "virus.pdf"));
    }

    [Fact]
    public async Task Upload_rules_refuse_active_types_non_participants_and_left_members_and_a_pending_file_of_another_user_is_404_at_send()
    {
        var s = await SetUpAsync("MA4");

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s.A, s.ConversationId, "page.html", "<html></html>"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s.A, s.ConversationId, "image.svg", "<svg/>"u8.ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s.A, s.ConversationId, "bundle.zip", [0x50, 0x4B, 3, 4])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s.A, s.ConversationId, "macro.xlsm", Pdf)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(s.Outsider, s.ConversationId, "notes.pdf", Pdf)).StatusCode);

        var mine = (await (await UploadAsync(s.A, s.ConversationId, "mine.pdf", Pdf)).Content.ReadFromJsonAsync<MessageAttachmentDto>(Web))!;
        // Another participant cannot attach the file of A, and a file of another conversation is not found either.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(s.ConversationId), s.B, new SendMessageRequest("steal", [mine.FileId]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(s.ConversationId), s.A, new SendMessageRequest("ghost", [Guid.NewGuid()]))).StatusCode);
        // A file attaches to one message only.
        await SendTextAsync(s.A, s.ConversationId, "first", [mine.FileId]);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(s.ConversationId), s.A, new SendMessageRequest("again", [mine.FileId]))).StatusCode);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<ConversationParticipant>().SingleAsync(p => p.ConversationId == s.ConversationId && p.UserId == s.B.UserId)).Leave(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var left = await UploadAsync(s.B, s.ConversationId, "late.pdf", Pdf);
        Assert.Equal(HttpStatusCode.Forbidden, left.StatusCode); // checked at attach time: a left member may not post, so may not upload
        Assert.Equal(ProblemCodes.messaging_not_allowed, await CodeAsync(left));
    }
}

/// <summary>The attachment routes live in the API project until the Lead adds them to <c>Routes.Messaging</c>.</summary>
internal static class MessageActionEndpointsRoutes
{
    public static string Attachments(Guid conversationId) => $"{Routes.Conversation(conversationId)}/attachments";

    public static string AttachmentUrl(Guid conversationId, Guid messageId, Guid fileId) => $"{Routes.Message(conversationId, messageId)}/attachments/{fileId}/url";
}
