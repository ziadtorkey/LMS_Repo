using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.People;

/// <summary>A guardian of students of one client. Client-wide (not tied to a unit): one guardian can have children in several schools.</summary>
[Audited]
public sealed class GuardianProfile : AuditableEntity, ITenantOwned
{
    private GuardianProfile()
    {
        NameAr = NameEn = NameArKey = NameEnKey = Mobile = string.Empty;
    }

    public GuardianProfile(
        Guid organizationId, string nameAr, string nameEn, string mobile, string? email, string? nationalId)
    {
        OrganizationId = organizationId;
        NameAr = NameEn = NameArKey = NameEnKey = string.Empty;
        Mobile = mobile;
        Update(nameAr, nameEn, mobile, email, nationalId);
    }

    public Guid OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public string NameArKey { get; private set; }
    public string NameEnKey { get; private set; }
    public string Mobile { get; private set; }
    public string? Email { get; private set; }
    public string? NationalId { get; private set; }

    public void Update(string nameAr, string nameEn, string mobile, string? email, string? nationalId)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        NameArKey = PersonNormalizer.NameKey(nameAr);
        NameEnKey = PersonNormalizer.NameKey(nameEn);
        Mobile = mobile;
        Email = email;
        NationalId = nationalId;
    }

    public void LinkUser(Guid userId) => UserId = userId;
}
