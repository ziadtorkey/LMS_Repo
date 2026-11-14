using Lms.Domain.Common;

namespace Lms.Domain.Announcements;

/// <summary>
/// One person an announcement reached, frozen when it was published (the audience snapshot). <c>Audience</c> is the chip they were reached as
/// (<c>Staff</c>, <c>Students</c>, <c>Guardians</c>). Read state is per recipient. A history row: never soft-deleted, and only its owner ever reads it.
/// </summary>
public sealed class AnnouncementRecipient : Entity, ITenantOwned
{
    private AnnouncementRecipient()
    {
        Audience = string.Empty;
    }

    public AnnouncementRecipient(Guid organizationId, Guid announcementId, Guid userId, string audience, DateTime now)
    {
        OrganizationId = organizationId;
        AnnouncementId = announcementId;
        UserId = userId;
        Audience = audience;
        CreatedAtUtc = now;
    }

    public Guid OrganizationId { get; private set; }
    public Guid AnnouncementId { get; private set; }
    public Guid UserId { get; private set; }
    public string Audience { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    public void MarkRead(DateTime now) => ReadAtUtc ??= now;
}
