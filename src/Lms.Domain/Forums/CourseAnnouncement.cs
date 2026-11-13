using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Forums;

/// <summary>A course announcement (FR-FOR-01): written by moderators of the ClassCourse, read by everyone who sees its forum.</summary>
[Audited]
public sealed class CourseAnnouncement : UnitOwnedEntity
{
    private CourseAnnouncement()
    {
        Title = BodyHtml = string.Empty;
    }

    public CourseAnnouncement(ClassCourse classCourse, string title, string bodyHtml, bool pinned, Guid authorUserId)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        Title = title.Trim();
        BodyHtml = bodyHtml;
        IsPinned = pinned;
        AuthorUserId = authorUserId;
    }

    public Guid ClassCourseId { get; private set; }
    public string Title { get; private set; }
    public string BodyHtml { get; private set; }
    public bool IsPinned { get; private set; }
    public Guid AuthorUserId { get; private set; }

    public void Update(string title, string bodyHtml, bool pinned)
    {
        Title = title.Trim();
        BodyHtml = bodyHtml;
        IsPinned = pinned;
    }
}
