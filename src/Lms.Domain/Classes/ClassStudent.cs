using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Classes;

/// <summary>A placement of a student in a class. At most one active placement per student and academic year (live key).</summary>
[Audited]
public sealed class ClassStudent : UnitOwnedEntity, ILiveKeyed
{
    public const string ActiveStatus = "Active";
    public const string RemovedStatus = "Removed";
    public const string TransferredStatus = "Transferred";

    private ClassStudent()
    {
        Status = string.Empty;
    }

    public ClassStudent(Class @class, Guid studentId, DateOnly from)
    {
        PlaceLike(@class);
        ClassId = @class.Id;
        StudentId = studentId;
        AcademicYearId = @class.AcademicYearId;
        From = from;
        Status = ActiveStatus;
        ActiveYearKey = AcademicYearId;
    }

    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid AcademicYearId { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly? To { get; private set; }
    public string Status { get; private set; }

    /// <summary>The academic year id while the placement is active; unique with the student, so one active class per year.</summary>
    public Guid? ActiveYearKey { get; private set; }

    public string? EndReason { get; private set; }

    /// <summary>Ends the placement (removed or transferred) and frees the student for another class that year.</summary>
    public void End(string status, DateOnly to, string? reason)
    {
        Status = status;
        To = to;
        EndReason = reason;
        ActiveYearKey = null;
    }

    public void ClearLiveKeys() => ActiveYearKey = null;
}
