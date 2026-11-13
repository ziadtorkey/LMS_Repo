using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Forums;

/// <summary>A thread of the implicit forum of a ClassCourse (FR-FOR-01). Moderation (pin, lock, delete) is audited.</summary>
[Audited]
public sealed class ForumThread : UnitOwnedEntity
{
    private ForumThread()
    {
        Title = string.Empty;
    }

    public ForumThread(ClassCourse classCourse, string title, Guid authorUserId, DateTime nowUtc)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        Title = title.Trim();
        AuthorUserId = authorUserId;
        LastActivityAtUtc = nowUtc;
    }

    public Guid ClassCourseId { get; private set; }
    public string Title { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public bool IsPinned { get; private set; }
    public bool IsLocked { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }

    public void Moderate(bool pinned, bool locked)
    {
        IsPinned = pinned;
        IsLocked = locked;
    }

    public void Touch(DateTime nowUtc) => LastActivityAtUtc = nowUtc;
}
