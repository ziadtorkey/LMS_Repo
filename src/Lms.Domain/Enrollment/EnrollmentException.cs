using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Enrollment;

/// <summary>
/// A request to enroll a student in a class course although they fail a restriction (FR-CRS-03, D40). It lives at the class unit.
/// <c>PendingKey</c> is <c>classCourse:student</c> while the request is pending and null afterwards; a unique index on it gives one
/// pending request per pair, also under concurrent requests (MySQL lets any number of rows have null).
/// </summary>
[Audited]
public sealed class EnrollmentException : UnitOwnedEntity
{
    public const string PendingStatus = "Pending";
    public const string ApprovedStatus = "Approved";
    public const string RejectedStatus = "Rejected";
    public const string CancelledStatus = "Cancelled";

    private EnrollmentException()
    {
        FailedTypesJson = Reason = Status = string.Empty;
    }

    public EnrollmentException(ClassCourse classCourse, Guid studentId, string failedTypesJson, string reason, Guid requestedBy)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        StudentId = studentId;
        FailedTypesJson = failedTypesJson;
        Reason = reason;
        Status = PendingStatus;
        RequestedBy = requestedBy;
        PendingKey = KeyOf(classCourse.Id, studentId);
    }

    public Guid ClassCourseId { get; private set; }
    public Guid StudentId { get; private set; }
    public string FailedTypesJson { get; private set; }
    public string Reason { get; private set; }
    public string Status { get; private set; }
    public string? PendingKey { get; private set; }
    public Guid RequestedBy { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }

    public bool IsPending => Status == PendingStatus;

    public static string KeyOf(Guid classCourseId, Guid studentId) => $"{classCourseId:N}:{studentId:N}";

    public void Approve(Guid by, DateTime at, string? comment) => Decide(ApprovedStatus, by, at, comment);

    public void Reject(Guid by, DateTime at, string comment) => Decide(RejectedStatus, by, at, comment);

    public void Cancel(DateTime at) => Decide(CancelledStatus, RequestedBy, at, null);

    private void Decide(string status, Guid by, DateTime at, string? comment)
    {
        if (!IsPending)
        {
            throw new InvalidOperationException("The request is already decided.");
        }

        Status = status;
        DecidedBy = by;
        DecidedAt = at;
        DecisionComment = comment;
        PendingKey = null;
    }
}
