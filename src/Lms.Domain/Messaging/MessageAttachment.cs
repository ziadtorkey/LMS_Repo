using Lms.Domain.Common;

namespace Lms.Domain.Messaging;

/// <summary>
/// A file attached to a message (L12). It starts pending (<see cref="MessageId"/> null), bound to its uploader and one conversation: only that person, in that
/// conversation, can attach it to a message (anything else is 404 at send). A pending upload nobody attaches is purged after 24 hours.
/// </summary>
public sealed class MessageAttachment : Entity, ITenantOwned
{
    private MessageAttachment()
    {
        FileName = ContentType = string.Empty;
    }

    public MessageAttachment(Conversation conversation, Guid uploaderUserId, Guid fileId, string fileName, string contentType, long sizeBytes, DateTime nowUtc)
    {
        OrganizationId = conversation.OrganizationId;
        ConversationId = conversation.Id;
        UploaderUserId = uploaderUserId;
        FileId = fileId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        CreatedAtUtc = nowUtc;
    }

    public Guid OrganizationId { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid UploaderUserId { get; private set; }
    public Guid? MessageId { get; private set; }
    public Guid FileId { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public bool IsPending => MessageId is null;

    public void AttachTo(Message message) => MessageId = message.Id;
}
