using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>The outcome of a group action: the conversation, or the problem code and HTTP status that refuses it.</summary>
public sealed record GroupResult(Conversation? Conversation, string? ProblemCode, int Status)
{
    public static GroupResult Done(Conversation conversation) => new(conversation, null, 200);

    public static GroupResult Refused(string code, int status) => new(null, code, status);
}

/// <summary>
/// Group conversations (FR-MSG-01, D66, D74): only staff start one, the rules matrix is checked owner to member (never pairwise), a group has at least three members
/// and at most <c>Messaging.MaxGroupSize</c>, and only its owner adds, removes or sets it read-only. A removed member keeps reading what was sent until the removal.
/// </summary>
public sealed class GroupService(LmsDbContext db, ICurrentContext context, IClock clock, IMessagingPolicy policy, ConversationService conversations, ISettingsResolver settings, MessagingEvents events)
{
    public const int MinMembers = 3;

    public async Task<GroupResult> CreateAsync(string? title, IReadOnlyList<Guid>? userIds, CancellationToken ct)
    {
        var owner = context.UserId!.Value;
        var name = title?.Trim();
        if (name is { Length: > MessagingLimits.MaxConversationTitleLength })
        {
            return GroupResult.Refused(ProblemCodes.validation_failed, 400);
        }

        var members = (userIds ?? []).Where(u => u != owner).Distinct().ToList();
        if (members.Count + 1 < MinMembers || members.Count + 1 > await MaxSizeAsync(ct))
        {
            return GroupResult.Refused(ProblemCodes.validation_failed, 400);
        }

        if (!await conversations.IsStaffAsync(owner, ct))
        {
            return GroupResult.Refused(ProblemCodes.messaging_not_allowed, 403);
        }

        if (await CheckMembersAsync(owner, members, ct) is { } refused)
        {
            return refused;
        }

        var now = clock.UtcNow;
        var group = Conversation.Group(context.OrganizationId!.Value, owner, string.IsNullOrEmpty(name) ? null : name, null, now);
        db.Add(group);
        db.Add(new ConversationParticipant(group, owner, now));
        foreach (var member in members)
        {
            db.Add(new ConversationParticipant(group, member, now));
        }

        await db.SaveChangesAsync(ct);
        await events.ChangedAsync(group, null, ct);
        return GroupResult.Done(group);
    }

    /// <summary>Owner only (a participant who is not the owner is told 404, like any miss).</summary>
    public async Task<GroupResult> AddAsync(ConversationAccessResult access, IReadOnlyList<Guid>? userIds, CancellationToken ct)
    {
        if (!IsOwnerOfGroup(access))
        {
            return GroupResult.Refused("not_found", 404);
        }

        var owner = access.Me.UserId;
        var wanted = (userIds ?? []).Where(u => u != owner).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return GroupResult.Refused(ProblemCodes.validation_failed, 400);
        }

        var rows = await db.Set<ConversationParticipant>().Where(p => p.ConversationId == access.Conversation.Id).ToListAsync(ct);
        var active = rows.Count(p => !p.HasLeft);
        var toAdd = wanted.Where(u => rows.FirstOrDefault(p => p.UserId == u) is not { HasLeft: false }).ToList(); // already a member: nothing to do
        if (active + toAdd.Count > await MaxSizeAsync(ct))
        {
            return GroupResult.Refused(ProblemCodes.validation_failed, 400);
        }

        if (await CheckMembersAsync(owner, toAdd, ct) is { } refused)
        {
            return refused;
        }

        var now = clock.UtcNow;
        foreach (var user in toAdd)
        {
            if (rows.FirstOrDefault(p => p.UserId == user) is { } former)
            {
                former.Rejoin();
            }
            else
            {
                db.Add(new ConversationParticipant(access.Conversation, user, now));
            }
        }

        await db.SaveChangesAsync(ct);
        await events.ChangedAsync(access.Conversation, null, ct);
        return GroupResult.Done(access.Conversation);
    }

    /// <summary>The owner removes a member; the owner leaves with the leave route. A group never drops below three members.</summary>
    public async Task<GroupResult> RemoveAsync(ConversationAccessResult access, Guid userId, CancellationToken ct)
    {
        if (!IsOwnerOfGroup(access))
        {
            return GroupResult.Refused("not_found", 404);
        }

        var rows = await db.Set<ConversationParticipant>().Where(p => p.ConversationId == access.Conversation.Id && !p.LeftAtUtc.HasValue).ToListAsync(ct);
        var target = rows.FirstOrDefault(p => p.UserId == userId);
        if (target is null || userId == access.Me.UserId)
        {
            return GroupResult.Refused("not_found", 404);
        }

        if (rows.Count - 1 < MinMembers)
        {
            return GroupResult.Refused(ProblemCodes.validation_failed, 400);
        }

        target.Leave(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await events.ChangedAsync(access.Conversation, [userId], ct); // the removed member refetches too
        return GroupResult.Done(access.Conversation);
    }

    public async Task<GroupResult> SetReadOnlyAsync(ConversationAccessResult access, bool readOnly, CancellationToken ct)
    {
        if (!IsOwnerOfGroup(access))
        {
            return GroupResult.Refused("not_found", 404);
        }

        access.Conversation.SetReadOnly(readOnly);
        await db.SaveChangesAsync(ct);
        await events.ChangedAsync(access.Conversation, null, ct);
        return GroupResult.Done(access.Conversation);
    }

    private static bool IsOwnerOfGroup(ConversationAccessResult access) =>
        access.Conversation is { Kind: Conversation.GroupKind } group && group.OwnerUserId == access.Me.UserId && !access.Me.HasLeft;

    private async Task<int> MaxSizeAsync(CancellationToken ct) =>
        (await settings.ResolveAsync(SettingKeys.MessagingMaxGroupSize, context.ScopePath!, ct)).Value;

    /// <summary>A person outside this client or one the rules do not let the owner add gets one answer: 403 <c>messaging_not_allowed</c>, no name, no reason.</summary>
    private async Task<GroupResult?> CheckMembersAsync(Guid owner, IReadOnlyList<Guid> members, CancellationToken ct)
    {
        foreach (var member in members)
        {
            if (!await conversations.ExistsInClientAsync(member, ct) || !(await policy.CanAddToGroupAsync(owner, member, ct)).Allowed)
            {
                return GroupResult.Refused(ProblemCodes.messaging_not_allowed, 403);
            }
        }

        return null;
    }
}
