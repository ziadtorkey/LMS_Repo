using Lms.Shared;
using Lms.Shared.Messaging;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The conversation routes (M6 plan Task 31, L6): <c>ConversationAccess</c> is the only gate, so every caller of the M1 world is a non-participant
/// (the admins of client A at root and School1, the admin of School2, a teacher who is not in the conversation, the admin of client B, the platform
/// admin) and gets 404 on every route that names a conversation, an Org Admin included (they hold the permission and still see nothing). Anonymous
/// gets 401. A participant's own answers (200 / 201 / 204 / 403 for the read-only and restricted cases) are the participant tests
/// (<see cref="M6ParticipantIsolationTests"/>); this file is the non-participant wall. Rows wait for their route (milestone M6).
/// The platform-admin cell (404) is the plan's reading (a platform admin is not a participant); confirm it when the first route is mapped.
/// </summary>
public sealed class M6ConversationsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M6 });

    /// <summary>Every non-participant caller: 404; anonymous: 401.</summary>
    private static Dictionary<Caller, int> Wall() => Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound);

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("conversation get", HttpMethod.Get, Routes.ConversationPattern,
            async (w, _) => new ProbeRequest(Routes.Conversation(await M6Kit.NewDirectAsync(w))), Wall()),
        new("conversation messages list", HttpMethod.Get, Routes.ConversationMessagesPattern,
            async (w, _) => new ProbeRequest(Routes.ConversationMessages(await M6Kit.NewDirectAsync(w))), Wall()),
        new("conversation message send", HttpMethod.Post, Routes.ConversationMessagesPattern,
            async (w, _) => new ProbeRequest(Routes.ConversationMessages(await M6Kit.NewDirectAsync(w)), new SendMessageRequest("probe", null)), Wall()),
        new("conversation message delete", HttpMethod.Delete, Routes.MessagePattern,
            async (w, _) =>
            {
                var id = await M6Kit.NewDirectAsync(w);
                return new ProbeRequest(Routes.Message(id, await M6Kit.FirstMessageAsync(w, id)));
            }, Wall()),
        new("conversation mute", HttpMethod.Put, Routes.ConversationMutePattern,
            async (w, _) => new ProbeRequest(Routes.ConversationMute(await M6Kit.NewDirectAsync(w)), new MuteConversationRequest(null)), Wall()),
        new("conversation unmute", HttpMethod.Delete, Routes.ConversationMutePattern,
            async (w, _) => new ProbeRequest(Routes.ConversationMute(await M6Kit.NewDirectAsync(w))), Wall()),
        new("conversation leave", HttpMethod.Post, Routes.ConversationLeavePattern,
            async (w, _) => new ProbeRequest(Routes.ConversationLeave(await M6Kit.NewGroupAsync(w))), Wall()),
        new("conversation mark read", HttpMethod.Post, Routes.ConversationReadPattern,
            async (w, _) => new ProbeRequest(Routes.ConversationRead(await M6Kit.NewDirectAsync(w))), Wall()),
        new("conversation participants add", HttpMethod.Post, Routes.ConversationParticipantsPattern,
            async (w, _) => new ProbeRequest(Routes.ConversationParticipants(await M6Kit.NewGroupAsync(w)), new AddParticipantsRequest([Guid.NewGuid()])), Wall()),
        new("conversation participant remove", HttpMethod.Delete, Routes.ConversationParticipantPattern,
            async (w, _) => new ProbeRequest(Routes.ConversationParticipant(await M6Kit.NewGroupAsync(w), Guid.NewGuid())), Wall()),
        new("conversation read-only set", HttpMethod.Put, Routes.ConversationReadOnlyPattern,
            async (w, _) => new ProbeRequest(Routes.ConversationReadOnly(await M6Kit.NewGroupAsync(w))), Wall()),
        new("conversation read-only clear", HttpMethod.Delete, Routes.ConversationReadOnlyPattern,
            async (w, _) => new ProbeRequest(Routes.ConversationReadOnly(await M6Kit.NewGroupAsync(w))), Wall()),
    ];
}
