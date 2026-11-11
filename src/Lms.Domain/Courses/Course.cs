using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Courses;

/// <summary>A subject a unit owns and may allocate down the tree. The unit fields are the owner unit.</summary>
[Audited]
public sealed class Course : UnitOwnedEntity, ILiveKeyed
{
    public const string ActiveStatus = "Active";
    public const string InactiveStatus = "Inactive";

    private Course()
    {
        Code = Status = NameAr = NameEn = Language = string.Empty;
    }

    public Course(
        OrgUnit owner, string code, string nameAr, string nameEn, string? descriptionAr, string? descriptionEn,
        string language, int weeklyPeriods)
    {
        PlaceIn(owner);
        Status = ActiveStatus;
        NameAr = NameEn = Language = string.Empty;
        Code = string.Empty;
        Update(code, nameAr, nameEn, descriptionAr, descriptionEn, language, weeklyPeriods);
    }

    public string Code { get; private set; }

    /// <summary>Upper-cased code while the course is live; unique per client. Cleared on soft delete so the code is reusable.</summary>
    public string? CodeKey { get; private set; }

    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public string? DescriptionAr { get; private set; }
    public string? DescriptionEn { get; private set; }
    public string Language { get; private set; }
    public int WeeklyPeriods { get; private set; }
    public string Status { get; private set; }

    public void Update(
        string code, string nameAr, string nameEn, string? descriptionAr, string? descriptionEn, string language, int weeklyPeriods)
    {
        Code = code.Trim();
        CodeKey = IsDeleted ? null : KeyOf(Code);
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        DescriptionAr = descriptionAr;
        DescriptionEn = descriptionEn;
        Language = language;
        WeeklyPeriods = weeklyPeriods;
    }

    public void SetStatus(string status) => Status = status;

    public void ClearLiveKeys() => CodeKey = null;

    public static string KeyOf(string code) => code.Trim().ToUpperInvariant();
}
