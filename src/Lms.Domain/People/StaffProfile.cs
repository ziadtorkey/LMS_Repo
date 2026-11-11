using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.People;

/// <summary>
/// A staff member of one client: the per-client profile of a platform-level user. The home unit decides who manages
/// the profile; role assignments (possibly in other units) decide what the person may do. Identifying values are stored
/// in their <see cref="PersonNormalizer"/> form.
/// </summary>
[Audited]
public sealed class StaffProfile : AuditableEntity, IOrgUnitScoped
{
    private StaffProfile()
    {
        NameAr = NameEn = NameArKey = NameEnKey = Email = OrgUnitPath = string.Empty;
    }

    public StaffProfile(
        OrgUnit homeUnit, Guid userId, string nameAr, string nameEn, string email, string? mobile, string? nationalId,
        string? jobTitleAr, string? jobTitleEn, Guid? photoFileId, DateOnly? expiresOn)
    {
        OrganizationId = homeUnit.OrganizationId;
        OrgUnitId = homeUnit.Id;
        OrgUnitPath = homeUnit.Path;
        UserId = userId;
        Email = email;
        Status = PersonStatus.Active;
        NameAr = NameEn = NameArKey = NameEnKey = string.Empty;
        Update(nameAr, nameEn, mobile, nationalId, jobTitleAr, jobTitleEn, photoFileId, expiresOn);
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>The home unit.</summary>
    public Guid OrgUnitId { get; private set; }

    public string OrgUnitPath { get; private set; }
    public Guid UserId { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public string NameArKey { get; private set; }
    public string NameEnKey { get; private set; }

    /// <summary>Login identity; read-only after creation (M2 gap G7).</summary>
    public string Email { get; private set; }

    public string? Mobile { get; private set; }
    public string? NationalId { get; private set; }
    public string? JobTitleAr { get; private set; }
    public string? JobTitleEn { get; private set; }
    public Guid? PhotoFileId { get; private set; }
    public PersonStatus Status { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public DateOnly? ExpiryNoticeSentOn { get; private set; }

    public void Update(
        string nameAr, string nameEn, string? mobile, string? nationalId, string? jobTitleAr, string? jobTitleEn,
        Guid? photoFileId, DateOnly? expiresOn)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        NameArKey = PersonNormalizer.NameKey(nameAr);
        NameEnKey = PersonNormalizer.NameKey(nameEn);
        Mobile = mobile;
        NationalId = nationalId;
        JobTitleAr = jobTitleAr;
        JobTitleEn = jobTitleEn;
        PhotoFileId = photoFileId;
        if (expiresOn != ExpiresOn)
        {
            ExpiresOn = expiresOn;
            ExpiryNoticeSentOn = null; // a new expiry date gets its own notice
        }
    }

    public void MoveHome(OrgUnit unit)
    {
        if (unit.OrganizationId != OrganizationId)
        {
            throw new InvalidOperationException("A person cannot be moved to a unit of another client.");
        }

        OrgUnitId = unit.Id;
        OrgUnitPath = unit.Path;
    }

    public void SetStatus(PersonStatus status) => Status = status;

    public void MarkExpiryNoticeSent(DateOnly day) => ExpiryNoticeSentOn = day;
}
