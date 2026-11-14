using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging.Policy;

/// <summary>
/// The messaging policy (M6 Task 11, D69, L4, L25). Each rule is <c>min(stored, default)</c>, so a stored row looser than the D69 default never counts. A user with
/// several roles is judged pair by pair: <b>each role pair carries its own relationship requirement and the best pair wins, never a mix</b> (an Org Admin who is also a
/// Teacher reaches students only as their teacher). Blocks and restrictions are read from the caller's client only. A refusal to start is always the neutral
/// <see cref="MessagingReasons.BlockedOrRule"/>.
/// </summary>
public sealed class MessagingPolicy(LmsDbContext db, RelationshipLookup relationships, IClock clock) : IMessagingPolicy
{
    public async Task<MessagingDecision> CanStartAsync(Guid organizationId, Guid initiatorUserId, Guid otherUserId, CancellationToken cancellationToken = default)
    {
        if (initiatorUserId == otherUserId || await IsBlockedBetweenAsync(organizationId, initiatorUserId, otherUserId, cancellationToken))
        {
            return MessagingDecision.Deny(MessagingReasons.BlockedOrRule);
        }

        var rule = await CreatingRuleHoldsAsync(organizationId, initiatorUserId, otherUserId, cancellationToken);
        return rule.Allowed ? rule : MessagingDecision.Deny(MessagingReasons.BlockedOrRule);
    }

    public async Task<MessagingDecision> CanAddToGroupAsync(Guid organizationId, Guid ownerUserId, Guid memberUserId, CancellationToken cancellationToken = default)
    {
        // Owner to member, never pairwise. A block does not matter here: blocks stop direct messages only, groups are unchanged (D66).
        if (ownerUserId == memberUserId)
        {
            return MessagingDecision.Allow;
        }

        var rule = await CreatingRuleHoldsAsync(organizationId, ownerUserId, memberUserId, cancellationToken);
        return rule.Allowed ? rule : MessagingDecision.Deny(MessagingReasons.BlockedOrRule);
    }

    public async Task<MessagingDecision> CreatingRuleHoldsAsync(Guid organizationId, Guid fromUserId, Guid toUserId, CancellationToken cancellationToken = default)
    {
        var today = Today();
        var roles = MessagingRuleDefaults.Roles;
        var from = await relationships.RolesAsync(organizationId, fromUserId, today, [.. roles], cancellationToken);
        var to = await relationships.RolesAsync(organizationId, toUserId, today, [.. roles], cancellationToken);
        if (from.Count == 0 || to.Count == 0)
        {
            return MessagingDecision.Deny(MessagingReasons.Rule);
        }

        var stored = await StoredAsync(organizationId, cancellationToken);
        foreach (var fromRole in from.Keys)
        {
            foreach (var toRole in to.Keys)
            {
                var mode = Effective(stored, fromRole, toRole);
                if (await SatisfiedAsync(mode, organizationId, fromRole, fromUserId, toRole, toUserId, today, from, to, cancellationToken))
                {
                    return MessagingDecision.Allow; // the best pair wins
                }
            }
        }

        return MessagingDecision.Deny(MessagingReasons.Rule);
    }

    /// <summary>The day relationships are judged on. It is the UTC date until the client calendar (M5, #639) is on main; the end of a teacher assignment can differ by a few hours around midnight Riyadh.</summary>
    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow);

    public async Task<bool> IsBlockedBetweenAsync(Guid organizationId, Guid userA, Guid userB, CancellationToken cancellationToken = default) =>
        await db.Set<UserBlock>().AnyAsync(
            b => b.OrganizationId == organizationId && (b.BlockerUserId == userA && b.BlockedUserId == userB || b.BlockerUserId == userB && b.BlockedUserId == userA), cancellationToken);

    public async Task<bool> IsRestrictedAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return await db.Set<MessagingRestriction>().AnyAsync(r => r.OrganizationId == organizationId && r.UserId == userId && (r.UntilUtc == null || r.UntilUtc > now), cancellationToken);
    }

    /// <summary>The mode of one cell: the lower of the stored row and the D69 default (a missing row is the default).</summary>
    public static string Effective(IReadOnlyDictionary<(string From, string To), string> stored, string fromRole, string toRole)
    {
        var def = MessagingRuleDefaults.Default(fromRole, toRole);
        return stored.TryGetValue((fromRole, toRole), out var mode) && MessagingRuleModes.Rank(mode) is >= 0 && MessagingRuleModes.Rank(mode) < MessagingRuleModes.Rank(def) ? mode : def;
    }

    private async Task<Dictionary<(string From, string To), string>> StoredAsync(Guid organizationId, CancellationToken ct) =>
        (await db.Set<MessagingRule>().Where(r => r.OrganizationId == organizationId).Select(r => new { r.FromRole, r.ToRole, r.Mode }).ToListAsync(ct))
        .ToDictionary(r => (r.FromRole, r.ToRole), r => r.Mode);

    private async Task<bool> SatisfiedAsync(
        string mode, Guid organizationId, string fromRole, Guid fromUser, string toRole, Guid toUser, DateOnly today,
        IReadOnlyDictionary<string, List<string>> from, IReadOnlyDictionary<string, List<string>> to, CancellationToken ct) => mode switch
        {
            MessagingRuleModes.Allow => true,
            MessagingRuleModes.SameUnit => await relationships.SameUnitAsync(organizationId, fromUser, toUser, from, to, ct),
            MessagingRuleModes.Relationship => await relationships.RelatedAsync(organizationId, fromRole, fromUser, toRole, toUser, today, from, to, ct),
            _ => false,
        };
}
