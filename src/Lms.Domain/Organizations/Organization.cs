using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Organizations;

/// <summary>A client (school group or university). The tenant root; not itself tenant-owned.</summary>
[Audited]
public sealed class Organization : AuditableEntity
{
    private Organization()
    {
        NameAr = NameEn = string.Empty;
    }

    public Organization(Guid id, string nameAr, string nameEn, OrganizationType type)
    {
        Id = id;
        NameAr = nameAr;
        NameEn = nameEn;
        Type = type;
    }

    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public OrganizationType Type { get; private set; }
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = nameAr;
        NameEn = nameEn;
    }

    public void SetStatus(OrganizationStatus status) => Status = status;
}