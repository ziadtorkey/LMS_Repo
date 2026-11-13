using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Assignments;

/// <summary>
/// An assignment of a class course (FR-ASG-01). Its lesson module (Kind <c>Assignment</c>, <c>RefId</c> = the assignment id)
/// owns title, visibility, target and completion rule; this row owns instructions, due date, grade and the late and
/// resubmission policy. Unit fields are the class course's.
/// </summary>
[Audited]
public sealed class Assignment : UnitOwnedEntity
{
    private Assignment()
    {
        LatePolicy = ResubmissionRule = string.Empty;
    }

    public Assignment(ClassCourse classCourse)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        LatePolicy = ResubmissionRule = string.Empty;
    }

    public Guid ClassCourseId { get; private set; }
    public string? InstructionsHtmlAr { get; private set; }
    public string? InstructionsHtmlEn { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public decimal MaxGrade { get; private set; }
    public string LatePolicy { get; private set; }
    public int? PenaltyPercent { get; private set; }
    public string ResubmissionRule { get; private set; }
    public bool AllowText { get; private set; }
    public bool AllowFiles { get; private set; }

    public void Apply(
        string? instructionsAr, string? instructionsEn, DateTime? dueAtUtc, decimal maxGrade, string latePolicy, int? penaltyPercent,
        string resubmissionRule, bool allowText, bool allowFiles)
    {
        InstructionsHtmlAr = instructionsAr;
        InstructionsHtmlEn = instructionsEn;
        DueAtUtc = dueAtUtc;
        MaxGrade = maxGrade;
        LatePolicy = latePolicy;
        PenaltyPercent = penaltyPercent;
        ResubmissionRule = resubmissionRule;
        AllowText = allowText;
        AllowFiles = allowFiles;
    }
}
