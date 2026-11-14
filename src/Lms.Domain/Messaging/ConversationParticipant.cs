using Lms.Domain.Common;

namespace Lms.Domain.Messaging;

/// <summary>A person in a conversation. A member who left (<see cref="LeftAtUtc"/>) reads what was sent before leaving and sends nothing more.</summary>
public sealed class ConversationParticipant : Entity, ITenantOwned
{
    private ConversationParticipant()
    {
    }

    public ConversationParticipant(Conversation conversation, Guid userId, DateTime joinedAtUtc)
    {
        OrganizationId = conversation.OrganizationId;
        ConversationId = conversation.Id;
        UserId = userId;
        JoinedAtUtc = joinedAtUtc;
        ParticipantKey = $"{conversation.Id:N}:{userId:N}";
    }

    public Guid OrganizationId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }
    public DateTime? LeftAtUtc { get; private set; }

    /// <summary>The newest message the participant has read (messages are ordered by id, which is time ordered).</summary>
    public Guid? LastReadMessageId { get; private set; }

    public DateTime? MutedUntilUtc { get; private set; }
    public bool Muted { get; private set; }

    /// <summary>"conversationId:userId": unique, so a person is in a conversation once.</summary>
    public string ParticipantKey { get; private set; } = string.Empty;

    public bool HasLeft => LeftAtUtc is not null;

    public void MarkRead(Guid messageId)
    {
        if (LastReadMessageId is null || messageId.CompareTo(LastReadMessageId.Value) > 0)
        {
            LastReadMessageId = messageId;
        }
    }

    public void Mute(bool muted, DateTime? untilUtc)
    {
        Muted = muted;
        MutedUntilUtc = untilUtc;
    }

    public void Leave(DateTime atUtc) => LeftAtUtc = atUtc;
}
