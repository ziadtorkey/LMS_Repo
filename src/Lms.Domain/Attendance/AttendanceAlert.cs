using Lms.Domain.Common;
using Lms.Domain.People;

namespace Lms.Domain.Attendance;

/// <summary>
/// A student who crossed the absence threshold (FR-EXC-04, D61): the marker that stops the alert repeating until the count drops below the threshold and crosses again.
/// Unit fields are the student's home unit, so the counselor list is a unit-subtree query. At most one active alert per student (<see cref="ActiveKey"/>).
/// </summary>
public sealed class AttendanceAlert : UnitOwnedEntity
{
    private AttendanceAlert()
    {
    }

    public AttendanceAlert(StudentProfile student, int absences, int lates, int windowDays, decimal score, decimal threshold, DateTime atUtc)
    {
        PlaceLike(student);
        StudentProfileId = student.Id;
        Absences = absences;
        Lates = lates;
        WindowDays = windowDays;
        Score = score;
        Threshold = threshold;
        AlertedAtUtc = atUtc;
        ActiveKey = student.Id.ToString("N");
    }

    public Guid StudentProfileId { get; private set; }
    public int Absences { get; private set; }
    public int Lates { get; private set; }
    public int WindowDays { get; private set; }
    public decimal Score { get; private set; }
    public decimal Threshold { get; private set; }
    public DateTime AlertedAtUtc { get; private set; }
    public DateTime? ClearedAtUtc { get; private set; }

    /// <summary>The student id while the alert is active; unique, so two runs cannot both open an alert for one student.</summary>
    public string? ActiveKey { get; private set; }

    public void Clear(DateTime atUtc)
    {
        ClearedAtUtc = atUtc;
        ActiveKey = null;
    }
}
