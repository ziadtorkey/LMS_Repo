using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Configuration;

/// <summary>A setting value defined at one org unit; inherited by its subtree unless a deeper unit overrides it.</summary>
[Audited]
public sealed class OrgUnitSetting : AuditableEntity, IOrgUnitScoped
{
    private OrgUnitSetting()
    {
        OrgUnitPath = Key = ValueJson = string.Empty;
    }

    public OrgUnitSetting(Guid organizationId, Guid orgUnitId, string orgUnitPath, string key, string valueJson, bool isLocked)
    {
        OrganizationId = organizationId;
        OrgUnitId = orgUnitId;
        OrgUnitPath = orgUnitPath;
        Key = key;
        ValueJson = valueJson;
        IsLocked = isLocked;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public string Key { get; private set; }
    public string ValueJson { get; private set; }
    public bool IsLocked { get; private set; }

    public void Change(string valueJson, bool isLocked)
    {
        ValueJson = valueJson;
        IsLocked = isLocked;
    }
}
