using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Assignments;

/// <summary>
/// The teacher's grade and feedback on one submission (FR-ASG-02). One live row per submission. Saving (re)grades it; the
/// student sees it only after it is returned. <see cref="ReturnedRevision"/> counts the released grades that changed, which is
/// the revision of the grade event written at each return (the gradebook never sees an unreleased grade). Unit fields are the
/// submission's.
/// </summary>
[Audited]
public sealed class SubmissionFeedback : UnitOwnedEntity, ILiveKeyed
{
    private SubmissionFeedback()
    {
    }

    public SubmissionFeedback(Submission submission)
    {
        PlaceLike(submission);
        SubmissionId = submission.Id;
        SubmissionKey = submission.Id;
        AssignmentId = submission.AssignmentId;
        ClassCourseId = submission.ClassCourseId;
        StudentId = submission.StudentId;
    }

    public Guid SubmissionId { get; private set; }

    /// <summary>The submission id while live (unique: one feedback row per submission).</summary>
    public Guid? SubmissionKey { get; private set; }

    public Guid AssignmentId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid StudentId { get; private set; }

    /// <summary>The raw grade the teacher entered, 0..MaxGrade.</summary>
    public decimal Points { get; private set; }

    /// <summary>The late penalty taken off the grade (only for a late hand-in under the Penalty policy).</summary>
    public int? PenaltyPercentApplied { get; private set; }

    /// <summary>The grade after the penalty.</summary>
    public decimal FinalGrade { get; private set; }

    public string? FeedbackHtml { get; private set; }
    public Guid? AnnotatedFileAssetId { get; private set; }
    public DateTime GradedAtUtc { get; private set; }
    public DateTime? ReturnedAtUtc { get; private set; }

    /// <summary>The final grade at the last return; null before the first return.</summary>
    public decimal? ReturnedFinalGrade { get; private set; }

    /// <summary>Revision of the last grade event written for this submission (0 = none yet).</summary>
    public int ReturnedRevision { get; private set; }

    public void Set(decimal grade, int? penaltyPercent, decimal finalGrade, string? feedbackHtml, Guid? annotatedFileAssetId, DateTime nowUtc)
    {
        Points = grade;
        PenaltyPercentApplied = penaltyPercent;
        FinalGrade = finalGrade;
        FeedbackHtml = feedbackHtml;
        AnnotatedFileAssetId = annotatedFileAssetId;
        GradedAtUtc = nowUtc;
    }

    /// <summary>
    /// Releases the grade to the student. Returns the revision of the grade event to write, or null when the returned grade did
    /// not change since the last return (nothing new for the gradebook).
    /// </summary>
    public int? Return(DateTime nowUtc)
    {
        ReturnedAtUtc = nowUtc;
        if (ReturnedRevision > 0 && ReturnedFinalGrade == FinalGrade)
        {
            return null;
        }

        ReturnedFinalGrade = FinalGrade;
        ReturnedRevision++;
        return ReturnedRevision;
    }

    public void ClearLiveKeys() => SubmissionKey = null;
}
