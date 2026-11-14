using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The messaging safeguarding routes (M6 Task 11): the client rules (Org Admin at the client root only: a School admin and a Teacher are 403), the caller's own rules summary,
/// limits, recipient search and blocks (every signed-in user with a Messaging role sees only their own data), and block or unblock (an unknown user is the neutral 404).
/// </summary>
public sealed class MessagingPolicyProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("messaging rules read", HttpMethod.Get, Routes.CommsMessagingRules,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.CommsMessagingRules)),
            Expect(Ok, Forbidden, Forbidden, Forbidden, Ok, Forbidden)),
        new("messaging rules save", HttpMethod.Put, Routes.CommsMessagingRules,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.CommsMessagingRules,
                new SaveMessagingRulesRequest([new MessagingRuleCellDto(BuiltInRoles.Manager, BuiltInRoles.Guardian, MessagingRuleModes.Deny)]))),
            Expect(Ok, Forbidden, Forbidden, Forbidden, Ok, Forbidden)),
        new("messaging rules summary", HttpMethod.Get, Routes.MessagingRulesSummary,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MessagingRulesSummary)),
            Expect(Ok, Ok, Ok, Ok, Ok, NotFound)),
        new("messaging limits", HttpMethod.Get, Routes.MessagingLimits,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MessagingLimits)),
            Expect(Ok, Ok, Ok, Ok, Ok, NotFound)),
        new("messaging recipients", HttpMethod.Get, Routes.MessagingRecipients,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MessagingRecipientsSearch("probe"))),
            Expect(Ok, Ok, Ok, Ok, Ok, NotFound)),
        new("my blocks", HttpMethod.Get, Routes.MyBlocks,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyBlocks)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
        new("block a user", HttpMethod.Put, Routes.MyBlockPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyBlock(Guid.NewGuid()))),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)),
        new("unblock a user", HttpMethod.Delete, Routes.MyBlockPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyBlock(Guid.NewGuid()))),
            Expect(NoContent, NoContent, NoContent, NoContent, NoContent, NoContent)),
    ];
}
