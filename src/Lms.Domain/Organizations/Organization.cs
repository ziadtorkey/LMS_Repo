using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Organizations;

/// <summary>A client (school group or university). The tenant root; not itself tenant-owned.</summary>
[Audited]
public sealed class Organization : AuditableEntity
{
    private Organization()
    {
        NameAr = NameEn = ShortCode = string.Empty;
    }

    public Organization(Guid id, string nameAr, string nameEn, OrganizationType type, string? shortCode = null)
    {
        Id = id;
        ShortCode = shortCode ?? "c" + id.ToString("N")[..12]; // callers that care (client creation) pass a chosen one
        NameAr = nameAr;
        NameEn = nameEn;
        Type = type;
    }

    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public OrganizationType Type { get; private set; }

    /// <summary>D47: the client's login suffix (<c>studentcode@shortcode</c>); 2–20 of [a-z0-9-], unique platform-wide, fixed after creation.</summary>
    public string ShortCode { get; private set; }

    public static bool IsValidShortCode(string? value) =>
        value is { Length: >= 2 and <= 20 } && value.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = nameAr;
        NameEn = nameEn;
    }

    public void SetStatus(OrganizationStatus status) => Status = status;
}