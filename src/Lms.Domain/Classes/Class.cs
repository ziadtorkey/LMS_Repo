using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Classes;

/// <summary>A class (group of students) of one academic year at a unit.</summary>
[Audited]
public sealed class Class : UnitOwnedEntity, ILiveKeyed
{
    public const string OpenStatus = "Open";
    public const string ClosedStatus = "Closed";

    private Class()
    {
        Code = NameAr = NameEn = Status = string.Empty;
    }

    public Class(OrgUnit unit, Guid academicYearId, string code, string nameAr, string nameEn, int? capacity)
    {
        PlaceIn(unit);
        AcademicYearId = academicYearId;
        Status = OpenStatus;
        Code = NameAr = NameEn = string.Empty;
        Update(code, nameAr, nameEn, capacity, null);
    }

    public Guid AcademicYearId { get; private set; }
    public string Code { get; private set; }

    /// <summary>Upper-cased code while live; unique per client and academic year.</summary>
    public string? CodeKey { get; private set; }

    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public int? Capacity { get; private set; }
    public Guid? HomeroomTeacherUserId { get; private set; }
    public string Status { get; private set; }

    public void Update(string code, string nameAr, string nameEn, int? capacity, Guid? homeroomTeacherUserId)
    {
        Code = code.Trim();
        CodeKey = IsDeleted ? null : Code.ToUpperInvariant();
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        Capacity = capacity;
        HomeroomTeacherUserId = homeroomTeacherUserId;
    }

    public void SetStatus(string status) => Status = status;

    public void ClearLiveKeys() => CodeKey = null;
}
