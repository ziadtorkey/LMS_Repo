using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Enrollment;

/// <summary>One row of the append-only history of a student moving between classes. Never updated or deleted.</summary>
public sealed class StudentTransfer : Entity, IOrgUnitScoped
{
    private StudentTransfer()
    {
        OrgUnitPath = FromOrgUnitPath = Reason = string.Empty;
    }

    public StudentTransfer(Guid studentId, Class from, Class to, DateOnly effectiveOn, string reason, Guid? requestedBy, DateTime createdAt)
    {
        if (from.OrganizationId != to.OrganizationId)
        {
            throw new InvalidOperationException("A transfer stays inside one client.");
        }

        OrganizationId = to.OrganizationId;
        OrgUnitId = to.OrgUnitId;
        OrgUnitPath = to.OrgUnitPath;
        StudentId = studentId;
        FromClassId = from.Id;
        ToClassId = to.Id;
        FromOrgUnitPath = from.OrgUnitPath;
        EffectiveOn = effectiveOn;
        Reason = reason;
        CreatedBy = requestedBy;
        CreatedAt = createdAt;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>The destination unit.</summary>
    public Guid OrgUnitId { get; private set; }

    public string OrgUnitPath { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid FromClassId { get; private set; }
    public Guid ToClassId { get; private set; }

    /// <summary>The source unit path at transfer time: history, so it is deliberately not re-pathed.</summary>
    public string FromOrgUnitPath { get; private set; }

    public DateOnly EffectiveOn { get; private set; }
    public string Reason { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
