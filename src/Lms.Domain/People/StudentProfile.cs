using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.People;

/// <summary>
/// A student of one client, enrolled at a home unit. Data model only in M2 (no endpoint uses it until M3); gender,
/// religion and nationality are plain strings validated by the M3 endpoints. The code is unique per client forever.
/// </summary>
[Audited]
public sealed class StudentProfile : AuditableEntity, IOrgUnitScoped
{
    private StudentProfile()
    {
        StudentCode = NameAr = NameEn = NameArKey = NameEnKey = OrgUnitPath = string.Empty;
        Gender = Religion = Nationality = string.Empty;
    }

    public StudentProfile(
        OrgUnit homeUnit, string studentCode, string nameAr, string nameEn, DateOnly dateOfBirth, string gender,
        string religion, string nationality)
    {
        OrganizationId = homeUnit.OrganizationId;
        OrgUnitId = homeUnit.Id;
        OrgUnitPath = homeUnit.Path;
        StudentCode = studentCode;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Religion = religion;
        Nationality = nationality;
        Status = PersonStatus.Active;
        NameAr = NameEn = NameArKey = NameEnKey = string.Empty;
        SetNames(nameAr, nameEn);
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid? UserId { get; private set; }
    public string StudentCode { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public string NameArKey { get; private set; }
    public string NameEnKey { get; private set; }
    public string? NicknameAr { get; private set; }
    public string? NicknameEn { get; private set; }
    public DateOnly DateOfBirth { get; private set; }
    public string Gender { get; private set; }
    public string Religion { get; private set; }
    public string Nationality { get; private set; }
    public string? NationalId { get; private set; }
    public string? Mobile { get; private set; }
    public string? Email { get; private set; }
    public Guid? PhotoFileId { get; private set; }
    public PersonStatus Status { get; private set; }
    public string FeatureFlagOverridesJson { get; private set; } = "{}";

    public void Update(
        string nameAr, string nameEn, string? nicknameAr, string? nicknameEn, DateOnly dateOfBirth, string gender,
        string religion, string nationality, string? nationalId, string? mobile, string? email, Guid? photoFileId)
    {
        SetNames(nameAr, nameEn);
        NicknameAr = nicknameAr;
        NicknameEn = nicknameEn;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Religion = religion;
        Nationality = nationality;
        NationalId = nationalId;
        Mobile = mobile;
        Email = email;
        PhotoFileId = photoFileId;
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

    public void LinkUser(Guid userId) => UserId = userId;

    public void SetFeatureFlagOverrides(string json) => FeatureFlagOverridesJson = json;

    private void SetNames(string nameAr, string nameEn)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        NameArKey = PersonNormalizer.NameKey(nameAr);
        NameEnKey = PersonNormalizer.NameKey(nameEn);
    }
}
