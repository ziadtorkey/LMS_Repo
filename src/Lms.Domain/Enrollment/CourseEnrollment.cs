using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Enrollment;

/// <summary>A student enrolled in a class course. Only the enrollment engine writes these.</summary>
[Audited]
public sealed class CourseEnrollment : UnitOwnedEntity, ILiveKeyed
{
    public const string ActiveStatus = "Active";
    public const string DroppedStatus = "Dropped";

    private CourseEnrollment()
    {
        Source = Status = string.Empty;
    }

    public CourseEnrollment(ClassCourse classCourse, ClassStudent placement, string source, DateOnly from)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        StudentId = placement.StudentId;
        ClassStudentId = placement.Id;
        Source = source;
        Status = ActiveStatus;
        From = from;
        ActiveKey = classCourse.Id;
    }

    public Guid ClassCourseId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid ClassStudentId { get; private set; }
    public string Source { get; private set; }
    public string Status { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly? To { get; private set; }
    public string? DropReason { get; private set; }
    public Guid? CarriedFromEnrollmentId { get; private set; }

    /// <summary>The class course id while active; with the student it makes one active enrollment per class course.</summary>
    public Guid? ActiveKey { get; private set; }

    public void CarryFrom(Guid enrollmentId) => CarriedFromEnrollmentId = enrollmentId;

    public void Drop(DateOnly to, string? reason)
    {
        Status = DroppedStatus;
        To = to;
        DropReason = reason;
        ActiveKey = null;
    }

    public void ClearLiveKeys() => ActiveKey = null;
}
