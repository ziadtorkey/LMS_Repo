using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.People;

namespace Lms.Domain.Excuses;

/// <summary>
/// An absence excuse (M5 Task 19, FR-EXC-02/03, D61). It lives at the student's home unit, so the counselor queue is a unit-subtree query.
/// Every status change goes through <see cref="ExcuseStateMachine"/>; the covered sessions are frozen at submission (<see cref="AbsenceExcuseSession"/>).
/// </summary>
[Audited]
public sealed class AbsenceExcuse : UnitOwnedEntity
{
    private AbsenceExcuse()
    {
        Reason = Status = string.Empty;
    }

    public AbsenceExcuse(
        StudentProfile student, Guid submittedByUserId, string reason, string? reasonText, DateOnly? dateFrom, DateOnly? dateTo,
        Guid? attachmentFileId, DateTime nowUtc)
    {
        PlaceLike(student);
        StudentProfileId = student.Id;
        SubmittedByUserId = submittedByUserId;
        Reason = reason;
        ReasonText = reasonText;
        DateFrom = dateFrom;
        DateTo = dateTo;
        AttachmentFileId = attachmentFileId;
        Status = ExcuseStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        Version = 1;
    }

    public Guid StudentProfileId { get; private set; }

    /// <summary>The guardian (or the student, where the client allows it) who submitted it.</summary>
    public Guid SubmittedByUserId { get; private set; }

    /// <summary>An <c>ExcuseReasons</c> value.</summary>
    public string Reason { get; private set; }

    public string? ReasonText { get; private set; }

    public DateOnly? DateFrom { get; private set; }

    public DateOnly? DateTo { get; private set; }

    /// <summary>The private attachment in the file store, if any.</summary>
    public Guid? AttachmentFileId { get; private set; }

    /// <summary>An <see cref="ExcuseStatus"/> value.</summary>
    public string Status { get; private set; }

    public string? DecisionComment { get; private set; }

    public Guid? ReviewerUserId { get; private set; }

    public Guid? EscalatedToUserId { get; private set; }

    public Guid? EscalatedByUserId { get; private set; }

    /// <summary>The comment of the escalation (the decision comment stays the final decision's).</summary>
    public string? EscalationComment { get; private set; }

    public DateTime? EscalatedAtUtc { get; private set; }

    public DateTime SubmittedAtUtc { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>The row version, a concurrency token: every status change moves it, so approve, reject, escalate and withdraw cannot all win the same excuse.</summary>
    public int Version { get; private set; }

    /// <summary>The submitter withdraws it while it is still <c>Submitted</c>.</summary>
    public void Withdraw(Guid byUserId, DateTime nowUtc)
    {
        if (byUserId != SubmittedByUserId)
        {
            throw new InvalidOperationException("Only the submitter withdraws an excuse.");
        }

        Status = ExcuseStateMachine.Apply(Status, ExcuseAction.Withdraw);
        DecidedAtUtc = nowUtc;
        Version++;
    }

    /// <summary>A reviewer rejects it (a comment is required); the covered records stay Absent.</summary>
    public void Reject(Guid reviewerUserId, string comment, DateTime nowUtc)
    {
        EnsureMayReview(reviewerUserId);
        Status = ExcuseStateMachine.Apply(Status, ExcuseAction.Reject);
        ReviewerUserId = reviewerUserId;
        DecisionComment = comment;
        DecidedAtUtc = nowUtc;
        Version++;
    }

    /// <summary>
    /// A reviewer approves it. This only moves the excuse; the approve endpoint flips the covered attendance records to <c>Excused</c> in the same save.
    /// </summary>
    public void Approve(Guid reviewerUserId, string? comment, DateTime nowUtc)
    {
        EnsureMayReview(reviewerUserId);
        Status = ExcuseStateMachine.Apply(Status, ExcuseAction.Approve);
        ReviewerUserId = reviewerUserId;
        DecisionComment = comment;
        DecidedAtUtc = nowUtc;
        Version++;
    }

    /// <summary>A counselor hands it to another staff member (L14); the target is validated by the caller.</summary>
    public void Escalate(Guid reviewerUserId, Guid targetUserId, string? comment, DateTime nowUtc)
    {
        EnsureMayReview(reviewerUserId);
        Status = ExcuseStateMachine.Apply(Status, ExcuseAction.Escalate);
        EscalatedToUserId = targetUserId;
        EscalatedByUserId = reviewerUserId;
        EscalatedAtUtc = nowUtc;
        EscalationComment = comment;
        Version++;
    }

    private void EnsureMayReview(Guid reviewerUserId)
    {
        if (!ExcuseStateMachine.MayReview(reviewerUserId, SubmittedByUserId))
        {
            throw new ExcuseReviewerIsSubmitterException();
        }
    }
}

/// <summary>The reviewer is the person who submitted the excuse (409 <c>excuse_reviewer_is_submitter</c>).</summary>
public sealed class ExcuseReviewerIsSubmitterException() : InvalidOperationException("A reviewer cannot decide their own submission.");
