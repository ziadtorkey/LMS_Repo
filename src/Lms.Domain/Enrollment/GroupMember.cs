using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Enrollment;

/// <summary>Membership of an enrolled student in a group. The database keeps it to one group per class course (D6).</summary>
[Audited]
public sealed class GroupMember : UnitOwnedEntity, ILiveKeyed
{
    private GroupMember()
    {
    }

    public GroupMember(StudentGroup group, CourseEnrollment enrollment)
    {
        PlaceLike(group);
        GroupId = group.Id;
        ClassCourseId = group.ClassCourseId;
        StudentId = enrollment.StudentId;
        EnrollmentId = enrollment.Id;
        LiveKey = group.ClassCourseId;
    }

    public Guid GroupId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid EnrollmentId { get; private set; }

    /// <summary>The class course id while live; with the student it makes (class course, student) unique.</summary>
    public Guid? LiveKey { get; private set; }

    public void ClearLiveKeys() => LiveKey = null;
}
