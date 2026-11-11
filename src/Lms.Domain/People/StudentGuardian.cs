using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.People;

/// <summary>
/// Link between a student and a guardian. It lives where the student lives: the unit fields are copied from the
/// student (and re-pointed when the student moves), so the link follows the student's visibility.
/// </summary>
[Audited]
public sealed class StudentGuardian : AuditableEntity, IOrgUnitScoped
{
    private StudentGuardian()
    {
        Relation = OrgUnitPath = string.Empty;
    }

    private StudentGuardian(StudentProfile student, GuardianProfile guardian, string relation, bool isPrimary)
    {
        if (student.OrganizationId != guardian.OrganizationId)
        {
            throw new InvalidOperationException("A student and a guardian must belong to the same client.");
        }

        OrganizationId = student.OrganizationId;
        StudentId = student.Id;
        GuardianId = guardian.Id;
        Relation = relation;
        IsPrimary = isPrimary;
        OrgUnitId = student.OrgUnitId;
        OrgUnitPath = student.OrgUnitPath;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid GuardianId { get; private set; }
    public string Relation { get; private set; }
    public bool IsPrimary { get; private set; }

    public static StudentGuardian For(StudentProfile student, GuardianProfile guardian, string relation, bool isPrimary) =>
        new(student, guardian, relation, isPrimary);

    public void Update(string relation, bool isPrimary)
    {
        Relation = relation;
        IsPrimary = isPrimary;
    }

    /// <summary>Follows the student to a new home unit.</summary>
    public void Repoint(StudentProfile student)
    {
        OrgUnitId = student.OrgUnitId;
        OrgUnitPath = student.OrgUnitPath;
    }
}
