using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Courses;

/// <summary>A user who supervises a course. The unit fields are the course owner unit.</summary>
[Audited]
public sealed class CourseSupervisor : UnitOwnedEntity, ILiveKeyed
{
    private CourseSupervisor()
    {
    }

    public CourseSupervisor(Course course, Guid userId)
    {
        PlaceLike(course);
        CourseId = course.Id;
        UserId = userId;
        LiveKey = course.Id;
    }

    public Guid CourseId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? LiveKey { get; private set; }

    public void ClearLiveKeys() => LiveKey = null;
}
