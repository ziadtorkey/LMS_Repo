using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.People;

public enum ChangeRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// A student's request to change critical data (FR-STU-04, D27). It lives where the student lives (the unit fields are copied and
/// follow a home move). <c>ChangesJson</c> is <c>[{Field, Before, After}]</c>. While the request is pending
/// <c>PendingStudentId</c> holds the student id; a unique index on it allows one pending request per student even under two
/// concurrent requests (it is null once decided, and MySQL lets any number of rows have null).
/// </summary>
[Audited]
public sealed class StudentChangeRequest : AuditableEntity, IOrgUnitScoped
{
    private StudentChangeRequest()
    {
        OrgUnitPath = ChangesJson = string.Empty;
    }

    public StudentChangeRequest(StudentProfile student, Guid requestedBy, string changesJson, string? note)
    {
        OrganizationId = student.OrganizationId;
        OrgUnitId = student.OrgUnitId;
        OrgUnitPath = student.OrgUnitPath;
        StudentId = student.Id;
        PendingStudentId = student.Id;
        RequestedBy = requestedBy;
        ChangesJson = changesJson;
        Note = note;
        Status = ChangeRequestStatus.Pending;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid? PendingStudentId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string ChangesJson { get; private set; }
    public string? Note { get; private set; }
    public ChangeRequestStatus Status { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }

    public bool IsPending => Status == ChangeRequestStatus.Pending;

    public void Approve(Guid decidedBy, DateTime at, string? comment) => Decide(ChangeRequestStatus.Approved, decidedBy, at, comment);

    public void Reject(Guid decidedBy, DateTime at, string comment) => Decide(ChangeRequestStatus.Rejected, decidedBy, at, comment);

    /// <summary>The student withdraws a request that is still pending.</summary>
    public void Cancel(DateTime at) => Decide(ChangeRequestStatus.Cancelled, RequestedBy, at, null);

    /// <summary>The request follows the student when the home unit changes.</summary>
    public void MoveHome(OrgUnit unit)
    {
        if (unit.OrganizationId != OrganizationId)
        {
            throw new InvalidOperationException("A request cannot be moved to a unit of another client.");
        }

        OrgUnitId = unit.Id;
        OrgUnitPath = unit.Path;
    }

    private void Decide(ChangeRequestStatus status, Guid by, DateTime at, string? comment)
    {
        if (!IsPending)
        {
            throw new InvalidOperationException("The request is already decided.");
        }

        Status = status;
        DecidedBy = by;
        DecidedAt = at;
        DecisionComment = comment;
        PendingStudentId = null;
    }
}
