using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Courses;

/// <summary>Makes a course available at a unit (and below). The unit fields are the allocated unit.</summary>
[Audited]
public sealed class CourseAllocation : UnitOwnedEntity, ILiveKeyed
{
    private CourseAllocation()
    {
    }

    public CourseAllocation(Course course, OrgUnit unit)
    {
        if (course.OrganizationId != unit.OrganizationId)
        {
            throw new InvalidOperationException("A course can only be allocated inside its own client.");
        }

        PlaceIn(unit);
        CourseId = course.Id;
        CourseKey = course.Id;
    }

    public Guid CourseId { get; private set; }

    /// <summary>The course id while live; with the unit it makes (unit, course) unique among live allocations.</summary>
    public Guid? CourseKey { get; private set; }

    public void ClearLiveKeys() => CourseKey = null;
}
