using Lms.Domain.Common;

namespace Lms.Domain.Messaging;

/// <summary>One message. A delete for everyone (inside the window) leaves a tombstone: the text is cleared, never kept.</summary>
public sealed class Message : AuditableEntity, ITenantOwned
{
    public const string NormalState = "Normal";
    public const string DeletedState = "Deleted";
    public const string RemovedState = "Removed";

    private Message()
    {
        Body = string.Empty;
        State = NormalState;
    }

    public Message(Conversation conversation, Guid senderUserId, string body, DateTime sentAtUtc)
    {
        OrganizationId = conversation.OrganizationId;
        ConversationId = conversation.Id;
        SenderUserId = senderUserId;
        Body = body;
        State = NormalState;
        SentAtUtc = sentAtUtc;
    }

    public Guid OrganizationId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string Body { get; private set; }
    public string State { get; private set; }
    public DateTime SentAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public bool InDeleteWindow(DateTime nowUtc, int windowMinutes) => nowUtc <= SentAtUtc.AddMinutes(windowMinutes);

    /// <summary>Deletes for everyone: clears the text and keeps the row as a tombstone.</summary>
    public void DeleteForEveryone(DateTime atUtc)
    {
        Body = string.Empty;
        State = DeletedState;
        DeletedAtUtc = atUtc;
    }
}
