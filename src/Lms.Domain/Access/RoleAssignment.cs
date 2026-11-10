using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Access;

/// <summary>A user holding a role at an org unit, optionally within a validity window.</summary>
[Audited]
public sealed class RoleAssignment : AuditableEntity, IOrgUnitScoped
{
    private RoleAssignment()
    {
        OrgUnitPath = string.Empty;
    }

    public RoleAssignment(OrgUnit unit, Guid userId, Guid roleId, DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        OrganizationId = unit.OrganizationId;
        OrgUnitId = unit.Id;
        OrgUnitPath = unit.Path;
        UserId = userId;
        RoleId = roleId;
        ValidFrom = validFrom;
        ValidTo = validTo;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateOnly? ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }

    public bool IsValidOn(DateOnly day) =>
        (ValidFrom is null || ValidFrom <= day) && (ValidTo is null || ValidTo >= day);
}