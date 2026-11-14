using Lms.Shared;
using Lms.Shared.Messaging;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Conversations and messages (M6 Task 10): participant-only (L6) and behind the Messaging flag. The probe world has the flag off, so every caller that holds the
/// permission is refused with 403 feature_disabled (never data), a platform admin has no client (404); the participant gate (404 for a non-participant, an Org Admin included) is tested in
/// <c>ConversationTests</c> with the flag on.
/// </summary>
public sealed class MessagingProbeRows : IProbeRowModule
{
    private static readonly Guid Unknown = Guid.Parse("0198a6f2-0000-7000-8000-0000000000aa");

    public IEnumerable<ProbeRow> Rows =>
    [
        new("conversation create", HttpMethod.Post, Routes.Conversations,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Conversations, new CreateConversationRequest(ConversationKinds.Direct, [Unknown], null, "hi"))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation list", HttpMethod.Get, Routes.Conversations,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Conversations)),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation read", HttpMethod.Get, Routes.ConversationPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Conversation(Unknown))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation messages", HttpMethod.Get, Routes.ConversationMessagesPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ConversationMessages(Unknown))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation send", HttpMethod.Post, Routes.ConversationMessagesPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ConversationMessages(Unknown), new SendMessageRequest("hi", null))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation read mark", HttpMethod.Post, Routes.ConversationReadPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ConversationRead(Unknown))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("message delete", HttpMethod.Delete, Routes.MessagePattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Message(Unknown, Unknown))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation mute", HttpMethod.Put, Routes.ConversationMutePattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ConversationMute(Unknown), new MuteConversationRequest(null))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation unmute", HttpMethod.Delete, Routes.ConversationMutePattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ConversationMute(Unknown))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("conversation leave", HttpMethod.Post, Routes.ConversationLeavePattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ConversationLeave(Unknown))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("message attachment url", HttpMethod.Get, Lms.Api.Features.Messaging.MessageActionEndpoints.AttachmentUrlPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Message(Unknown, Unknown) + "/attachments/" + Unknown + "/url")),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("message attachment upload", HttpMethod.Post, Lms.Api.Features.Messaging.MessageActionEndpoints.AttachmentsPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Conversation(Unknown) + "/attachments", new { })),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("messaging unread total", HttpMethod.Get, Routes.MyUnreadTotal,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyUnreadTotal)),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
    ];
}
