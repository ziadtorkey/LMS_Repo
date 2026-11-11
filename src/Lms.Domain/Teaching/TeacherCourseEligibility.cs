using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;

namespace Lms.Domain.Teaching;

/// <summary>A teacher who may teach a course at a unit (and below). The unit fields are that unit.</summary>
[Audited]
public sealed class TeacherCourseEligibility : UnitOwnedEntity, ILiveKeyed
{
    private TeacherCourseEligibility()
    {
    }

    public TeacherCourseEligibility(OrgUnit unit, Guid userId, Course course)
    {
        PlaceIn(unit);
        UserId = userId;
        CourseId = course.Id;
        LiveKey = course.Id;
    }

    public Guid UserId { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid? LiveKey { get; private set; }

    public void ClearLiveKeys() => LiveKey = null;
}
