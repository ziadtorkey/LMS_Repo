using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Messaging;

/// <summary>
/// One cell of a client's narrowed rules matrix (M6 Task 11, D69, L4): the mode a sender role has towards a recipient role. A client may only narrow the defaults, and
/// the policy evaluates <c>min(stored, default)</c> on every call, so a row that is looser than the default (an old or hand-edited one) never counts. One row per
/// (client, from, to). Client level: only the Org Admin at the client root writes it.
/// </summary>
[Audited]
public sealed class MessagingRule : AuditableEntity, ITenantOwned
{
    private MessagingRule()
    {
        FromRole = ToRole = Mode = string.Empty;
    }

    public MessagingRule(Guid organizationId, string fromRole, string toRole, string mode)
    {
        OrganizationId = organizationId;
        FromRole = fromRole;
        ToRole = toRole;
        Mode = mode;
    }

    public Guid OrganizationId { get; private set; }

    public string FromRole { get; private set; }

    public string ToRole { get; private set; }

    /// <summary>A <c>MessagingRuleModes</c> value.</summary>
    public string Mode { get; private set; }

    public void SetMode(string mode) => Mode = mode;
}

/// <summary>
/// A user blocking another (M6 Task 11, D66). One direction in storage; for a direct conversation it works both ways (neither can start or post), for a group nothing
/// changes, and the blocked person is never told. Blocks belong to the blocker: no moderation route reads or removes them, and the audit catalog has no entry for them.
/// </summary>
public sealed class UserBlock : AuditableEntity, ITenantOwned, ILiveKeyed
{
    private UserBlock()
    {
    }

    public UserBlock(Guid organizationId, Guid blockerUserId, Guid blockedUserId)
    {
        OrganizationId = organizationId;
        BlockerUserId = blockerUserId;
        BlockedUserId = blockedUserId;
        BlockKey = KeyOf(blockerUserId, blockedUserId);
    }

    public Guid OrganizationId { get; private set; }

    public Guid BlockerUserId { get; private set; }

    public Guid BlockedUserId { get; private set; }

    /// <summary>"blocker:blocked" while live, so blocking the same person twice is one row and an unblock frees the key.</summary>
    public string? BlockKey { get; private set; }

    public static string KeyOf(Guid blocker, Guid blocked) => $"{blocker:N}:{blocked:N}";

    public void ClearLiveKeys() => BlockKey = null;
}

/// <summary>
/// A moderator's restriction of a user's sending (M6 Task 11, Task 15): the user cannot send until <see cref="UntilUtc"/> (null = until lifted). Checked on every send.
/// </summary>
[Audited]
public sealed class MessagingRestriction : AuditableEntity, ITenantOwned
{
    private MessagingRestriction()
    {
    }

    public MessagingRestriction(Guid organizationId, Guid userId, DateTime? untilUtc)
    {
        OrganizationId = organizationId;
        UserId = userId;
        UntilUtc = untilUtc;
    }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTime? UntilUtc { get; private set; }

    public bool IsActive(DateTime nowUtc) => UntilUtc is null || UntilUtc > nowUtc;

    /// <summary>A new restriction of the same user extends or replaces this one.</summary>
    public void RestrictUntil(DateTime? untilUtc) => UntilUtc = untilUtc;

    /// <summary>A moderator lifts it: it ends now.</summary>
    public void Lift(DateTime nowUtc) => UntilUtc = nowUtc;
}
