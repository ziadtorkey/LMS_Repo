using Lms.Domain.Common;

namespace Lms.Domain.Announcements;

/// <summary>One target of an announcement (<c>AnnouncementTargetKinds</c>). <c>TargetId</c> is null for the whole client; <c>IncludeSubtree</c> applies to units.</summary>
public sealed class AnnouncementTarget : UnitOwnedEntity
{
    private AnnouncementTarget()
    {
        Kind = string.Empty;
    }

    public AnnouncementTarget(Announcement announcement, string kind, Guid? targetId, bool includeSubtree)
    {
        PlaceLike(announcement);
        AnnouncementId = announcement.Id;
        Kind = kind;
        TargetId = targetId;
        IncludeSubtree = includeSubtree;
    }

    public Guid AnnouncementId { get; private set; }
    public string Kind { get; private set; }
    public Guid? TargetId { get; private set; }
    public bool IncludeSubtree { get; private set; }
}
