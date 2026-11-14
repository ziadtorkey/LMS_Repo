using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Grading;

/// <summary>What produced a grade event.</summary>
public enum GradeSourceKind
{
    Assignment = 1,
    Quiz = 2,

    /// <summary>A SCORM package attempt (M4b Task 10).</summary>
    Scorm = 3,

    /// <summary>An H5P content attempt (M4b Task 11).</summary>
    H5p = 4,
}

/// <summary>
/// An append-only record that a student was scored on an assignment or a quiz (the M5 gradebook seam, D51). It is written in the same
/// transaction as the grade, carries the class course's unit fields, and is unique per (source kind, source, student, revision):
/// a regrade writes the next revision, never an update.
/// </summary>
[Audited]
public sealed class GradeEvent : UnitOwnedEntity
{
    private GradeEvent()
    {
    }

    public GradeEvent(ClassCourse classCourse, GradeEventInput input)
    {
        PlaceLike(classCourse);
        SourceKind = input.SourceKind;
        SourceId = input.SourceId;
        ClassCourseId = classCourse.Id;
        StudentProfileId = input.StudentProfileId;
        Points = input.Points;
        MaxPoints = input.MaxPoints;
        ScoredAtUtc = input.ScoredAtUtc;
        Revision = input.Revision;
    }

    public GradeSourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public decimal Points { get; private set; }
    public decimal MaxPoints { get; private set; }
    public DateTime ScoredAtUtc { get; private set; }
    public int Revision { get; private set; }
}
