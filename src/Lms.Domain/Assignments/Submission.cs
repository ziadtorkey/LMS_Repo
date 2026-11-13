using Lms.Domain.Common;

namespace Lms.Domain.Assignments;

/// <summary>
/// A student's work on an assignment. One live row per (assignment, student): <see cref="StudentKey"/> holds the student id
/// while the row is live, so a unique index gives the rule also under concurrent submits. A resubmission replaces the
/// content and increments <see cref="Number"/>. Unit fields are the assignment's.
/// </summary>
public sealed class Submission : UnitOwnedEntity, ILiveKeyed
{
    // Same values as Lms.Shared.Assignments.SubmissionStatuses (the domain does not reference the contract project).
    public const string DraftStatus = "Draft";
    public const string SubmittedStatus = "Submitted";
    public const string LateStatus = "Late";
    public const string GradedStatus = "Graded";
    public const string ReturnedStatus = "Returned";

    private Submission()
    {
        Status = string.Empty;
    }

    public Submission(Assignment assignment, Guid studentId)
    {
        PlaceLike(assignment);
        AssignmentId = assignment.Id;
        ClassCourseId = assignment.ClassCourseId;
        StudentId = studentId;
        StudentKey = studentId;
        Status = DraftStatus;
    }

    public Guid AssignmentId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid StudentId { get; private set; }

    /// <summary>The student id while the row is live (unique with the assignment).</summary>
    public Guid? StudentKey { get; private set; }

    public string Status { get; private set; }
    public string? TextHtml { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public bool IsLate { get; private set; }

    /// <summary>Hand-ins so far (1 = first, 2 = first resubmission); a draft does not count.</summary>
    public int Number { get; private set; }

    public void SaveDraft(string? textHtml)
    {
        TextHtml = textHtml;
        Status = DraftStatus;
    }

    public void HandIn(string? textHtml, bool isLate, DateTime nowUtc)
    {
        TextHtml = textHtml;
        IsLate = isLate;
        SubmittedAtUtc = nowUtc;
        Status = isLate ? LateStatus : SubmittedStatus;
        Number++;
    }

    /// <summary>The teacher saved a grade; the student does not see it until it is returned.</summary>
    public void MarkGraded() => Status = GradedStatus;

    /// <summary>The grade and feedback are released to the student.</summary>
    public void MarkReturned() => Status = ReturnedStatus;

    public void ClearLiveKeys() => StudentKey = null;
}
