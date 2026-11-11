using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Courses;

namespace Lms.Domain.Classes;

/// <summary>A course taught in a class (optional courses are enrolled manually).</summary>
[Audited]
public sealed class ClassCourse : UnitOwnedEntity, ILiveKeyed
{
    private ClassCourse()
    {
    }

    public ClassCourse(Class @class, Course course, bool isOptional)
    {
        PlaceLike(@class);
        ClassId = @class.Id;
        CourseId = course.Id;
        CourseKey = course.Id;
        IsOptional = isOptional;
    }

    public Guid ClassId { get; private set; }
    public Guid CourseId { get; private set; }

    /// <summary>The course id while live; with the class it makes (class, course) unique.</summary>
    public Guid? CourseKey { get; private set; }

    public bool IsOptional { get; private set; }

    public void SetOptional(bool isOptional) => IsOptional = isOptional;

    public void ClearLiveKeys() => CourseKey = null;
}
