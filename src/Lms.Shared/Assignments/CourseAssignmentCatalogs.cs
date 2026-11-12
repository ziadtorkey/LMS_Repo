namespace Lms.Shared.Assignments;

/// <summary>
/// What happens to a submission after <c>DueAtUtc</c> (FR-ASG-01), judged on the server clock: <c>Allow</c> = taken and
/// flagged late; <c>Penalty</c> = taken, flagged late, and <c>PenaltyPercent</c> is taken off the grade at grading;
/// <c>Block</c> = refused with 409 <c>submission_closed</c>.
/// </summary>
public static class LatePolicies
{
    public const string Allow = "Allow";
    public const string Penalty = "Penalty";
    public const string Block = "Block";

    public static IReadOnlyList<string> All { get; } = [Allow, Penalty, Block];
}

/// <summary>
/// Whether a student may submit again (FR-ASG-01). <c>None</c> = one submission; <c>UntilDue</c> = replace it until
/// <c>DueAtUtc</c>; <c>UntilGraded</c> = replace it until the teacher grades it. Otherwise 409
/// <c>resubmission_not_allowed</c>. A returned submission may always be resubmitted when the rule is not <c>None</c>.
/// </summary>
public static class ResubmissionRules
{
    public const string None = "None";
    public const string UntilDue = "UntilDue";
    public const string UntilGraded = "UntilGraded";

    public static IReadOnlyList<string> All { get; } = [None, UntilDue, UntilGraded];
}

/// <summary>
/// The state of a student's submission. <c>Draft</c> = saved, not handed in (not seen by the teacher);
/// <c>Submitted</c> = handed in on time; <c>Late</c> = handed in after <c>DueAtUtc</c>; <c>Graded</c> = the teacher saved
/// a grade (not yet visible to the student); <c>Returned</c> = grade and feedback released to the student.
/// <c>SubmissionDto.IsLate</c> keeps the late flag after grading.
/// </summary>
public static class SubmissionStatuses
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string Late = "Late";
    public const string Graded = "Graded";
    public const string Returned = "Returned";

    public static IReadOnlyList<string> All { get; } = [Draft, Submitted, Late, Graded, Returned];

    /// <summary>Statuses the teacher sees (a draft stays private to the student).</summary>
    public static IReadOnlyList<string> HandedIn { get; } = [Submitted, Late, Graded, Returned];
}
