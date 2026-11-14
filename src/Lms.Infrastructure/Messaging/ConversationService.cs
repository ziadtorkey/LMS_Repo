using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>Conversations of the caller: the direct pair (one per two people, also under a parallel create) and the inbox (FR-MSG-01).</summary>
public sealed class ConversationService(LmsDbContext db, ICurrentContext context, IClock clock)
{
    /// <summary>The direct conversation with <paramref name="otherUserId"/> and whether it was created now. A second create returns the existing one.</summary>
    public async Task<(Conversation Conversation, bool Created)> GetOrCreateDirectAsync(Guid otherUserId, CancellationToken ct)
    {
        var me = context.UserId!.Value;
        var organizationId = context.OrganizationId!.Value;
        var key = Conversation.BuildDirectKey(organizationId, me, otherUserId);
        var existing = await db.Set<Conversation>().SingleOrDefaultAsync(c => c.DirectKey == key, ct);
        if (existing is not null)
        {
            await EnsureMemberAsync(existing, me, ct);
            return (existing, false);
        }

        var now = clock.UtcNow;
        var conversation = Conversation.Direct(organizationId, me, otherUserId, now);
        db.Add(conversation);
        db.Add(new ConversationParticipant(conversation, me, now));
        db.Add(new ConversationParticipant(conversation, otherUserId, now));
        try
        {
            await db.SaveChangesAsync(ct);
            return (conversation, true);
        }
        catch (DbUpdateException e) when (e.InnerException is MySqlConnector.MySqlException { Number: 1062 } or MySql.Data.MySqlClient.MySqlException { Number: 1062 })
        {
            // A parallel create won: the unique pair key refused ours. Return theirs.
            db.ChangeTracker.Clear();
            var winner = await db.Set<Conversation>().SingleAsync(c => c.DirectKey == key, ct);
            return (winner, false);
        }
    }

    /// <summary>True when the user has a profile in this client (the people one may message; the rules of D69 narrow it further, Task 11).</summary>
    public async Task<bool> ExistsInClientAsync(Guid userId, CancellationToken ct) =>
        await db.Set<StudentProfile>().AnyAsync(s => s.UserId == userId, ct)
        || await db.Set<StaffProfile>().AnyAsync(s => s.UserId == userId, ct)
        || await db.Set<GuardianProfile>().AnyAsync(g => g.UserId == userId, ct);

    /// <summary>Only staff start groups (D74).</summary>
    public Task<bool> IsStaffAsync(Guid userId, CancellationToken ct) => db.Set<StaffProfile>().AnyAsync(s => s.UserId == userId, ct);

    private async Task EnsureMemberAsync(Conversation conversation, Guid me, CancellationToken ct)
    {
        if (!await db.Set<ConversationParticipant>().AnyAsync(p => p.ConversationId == conversation.Id && p.UserId == me, ct))
        {
            throw new InvalidOperationException("The caller is not in the direct conversation of their own pair.");
        }
    }
}
