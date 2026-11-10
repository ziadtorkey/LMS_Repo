using Lms.Domain.Common;

namespace Lms.Domain.Access;

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

    /// <summary>Version of the built-in default permission set this role has received (0 = provisioned before versioning).</summary>
    public int DefaultsVersion { get; private set; }

    public void MarkDefaultsApplied(int version) => DefaultsVersion = version;
}