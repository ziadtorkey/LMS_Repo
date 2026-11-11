using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Teaching;

/// <summary>A teacher of a class course with dated history. At most one open primary teacher per class course.</summary>
[Audited]
public sealed class ClassCourseTeacher : UnitOwnedEntity, ILiveKeyed
{
    public const string PrimaryRole = "Primary";
    public const string AssistantRole = "Assistant";

    private ClassCourseTeacher()
    {
        Role = string.Empty;
    }

    public ClassCourseTeacher(ClassCourse classCourse, Guid userId, string role, DateOnly from)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        UserId = userId;
        Role = role;
        From = from;
        OpenPrimaryKey = role == PrimaryRole ? classCourse.Id : null;
    }

    public Guid ClassCourseId { get; private set; }
    public Guid UserId { get; private set; }
    public string Role { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly? To { get; private set; }

    /// <summary>The class course id while this is the open (not ended) primary teacher.</summary>
    public Guid? OpenPrimaryKey { get; private set; }

    public void End(DateOnly to)
    {
        To = to;
        OpenPrimaryKey = null;
    }

    public void ClearLiveKeys() => OpenPrimaryKey = null;
}
