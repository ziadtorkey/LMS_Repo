using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Configuration;

/// <summary>A policy switched on at one org unit; blocks the permissions the PolicyCatalog maps to it in the whole subtree.</summary>
[Audited]
public sealed class OrgUnitPolicy : AuditableEntity, IOrgUnitScoped
{
    private OrgUnitPolicy()
    {
        OrgUnitPath = PolicyKey = string.Empty;
    }

    public OrgUnitPolicy(Guid organizationId, Guid orgUnitId, string orgUnitPath, string policyKey)
    {
        OrganizationId = organizationId;
        OrgUnitId = orgUnitId;
        OrgUnitPath = orgUnitPath;
        PolicyKey = policyKey;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public string PolicyKey { get; private set; }
}
