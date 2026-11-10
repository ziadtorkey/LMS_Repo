using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Access;

[Audited]
public sealed class Role : AuditableEntity, ITenantOwned
{
    private Role()
    {
        NameAr = NameEn = string.Empty;
    }

    public Role(Guid organizationId, string? builtInKey, string nameAr, string nameEn, RoleBaseKind baseKind)
    {
        OrganizationId = organizationId;
        BuiltInKey = builtInKey;
        NameAr = nameAr;
        NameEn = nameEn;
        BaseKind = baseKind;
        IsBuiltIn = builtInKey is not null;
    }

    public Guid OrganizationId { get; private set; }
    public string? BuiltInKey { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public RoleBaseKind BaseKind { get; private set; }
    public bool IsBuiltIn { get; private set; }
}