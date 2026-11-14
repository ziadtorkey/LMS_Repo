using Lms.Domain.Messaging;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>
/// Who may post in a group (D74, D77), one place for the send, attach and <c>CanPost</c> paths. Staff post in every group, an owner-set read-only included. A <b>student</b> never posts in a
/// group (they read it and reply privately to the teacher). A <b>guardian</b> posts only in a teacher-run group (a class or course group, which carries a <c>ClassCourseId</c>) that is not
/// read-only. Anyone else is blocked only by the read-only switch. A direct conversation has no group rule here (the D69 policy decides it). The reasons are
/// <see cref="ConversationReadOnlyReasons"/>.
/// </summary>
public sealed class GroupPostingRules(LmsDbContext db)
{
    /// <summary>The reason this person cannot post in the conversation, or null when the group rules allow it.</summary>
    public async Task<string?> BlockReasonAsync(Conversation conversation, Guid userId, CancellationToken ct)
    {
        if (conversation.Kind != Conversation.GroupKind || await db.Set<StaffProfile>().AnyAsync(s => s.UserId == userId, ct))
        {
            return null;
        }

        if (await db.Set<StudentProfile>().AnyAsync(s => s.UserId == userId, ct))
        {
            return ConversationReadOnlyReasons.GroupStudentsReadOnly;
        }

        if (await db.Set<GuardianProfile>().AnyAsync(g => g.UserId == userId, ct))
        {
            return conversation.ClassCourseId is not null && !conversation.IsReadOnly ? null : ConversationReadOnlyReasons.GroupGuardiansReadOnly;
        }

        return conversation.IsReadOnly ? ConversationReadOnlyReasons.BlockedOrRule : null;
    }
}
