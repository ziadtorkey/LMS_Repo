using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Grading;

/// <summary>
/// The gradebook of one class course in one term (FR-GRD-01, M5 Task 7). Unique per (class course, term); created lazily by the first grade
/// event of the term or the first teacher use. It keeps its own settings (<c>ScaleId</c> is a reference by id to the scale chosen at
/// creation; editing the scale later is the scale module's business). Unit fields are the class course's.
/// </summary>
[Audited]
public sealed class Gradebook : UnitOwnedEntity, ILiveKeyed
{
    public const string OpenState = "Open";
    public const string LockedState = "Locked";

    private Gradebook()
    {
        CategoryMethod = MissingPolicy = RoundingMode = State = string.Empty;
    }

    public Gradebook(ClassCourse classCourse, Guid termId, Guid? scaleId, int decimalPlaces, string categoryMethod, string missingPolicy)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        ClassCourseKey = classCourse.Id;
        TermId = termId;
        ScaleId = scaleId;
        DecimalPlaces = decimalPlaces;
        CategoryMethod = categoryMethod;
        MissingPolicy = missingPolicy;
        RoundingMode = "HalfUp";
        State = OpenState;
    }

    public Guid ClassCourseId { get; private set; }

    /// <summary>The class course id while live: unique together with the term (one gradebook per class course and term).</summary>
    public Guid? ClassCourseKey { get; private set; }

    public Guid TermId { get; private set; }

    /// <summary>The scale in use (by id; the scale module owns it); null until one is chosen.</summary>
    public Guid? ScaleId { get; private set; }

    public int DecimalPlaces { get; private set; }
    public string CategoryMethod { get; private set; }
    public string MissingPolicy { get; private set; }
    public string RoundingMode { get; private set; }
    public string State { get; private set; }
    public DateTime? LockedAtUtc { get; private set; }

    public bool IsLocked => State == LockedState;

    public void Configure(Guid? scaleId, int decimalPlaces, string categoryMethod, string missingPolicy)
    {
        ScaleId = scaleId;
        DecimalPlaces = decimalPlaces;
        CategoryMethod = categoryMethod;
        MissingPolicy = missingPolicy;
    }

    public void Lock(DateTime nowUtc)
    {
        State = LockedState;
        LockedAtUtc = nowUtc;
    }

    public void Unlock()
    {
        State = OpenState;
        LockedAtUtc = null;
    }

    public void ClearLiveKeys() => ClassCourseKey = null;
}
