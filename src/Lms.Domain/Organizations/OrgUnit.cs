using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Organizations;

[Audited]
public sealed class OrgUnit : AuditableEntity, IOrgUnitScoped
{
    private OrgUnit()
    {
        NameAr = NameEn = Code = Path = string.Empty;
    }

    public Guid OrganizationId { get; private set; }
    public Guid? ParentId { get; private set; }
    public Guid LevelTypeId { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public string Code { get; private set; }
    public string Path { get; private set; }
    public int Depth { get; private set; }
    public OrgUnitStatus Status { get; private set; } = OrgUnitStatus.Active;
    public bool IsLevelTransfer { get; private set; }
    public Guid? NextTransferUnitId { get; private set; }
    public bool IsCourseManagementLevel { get; private set; }

    // Both are computed aliases; the mapped column is Path (filters read it directly).
    string IOrgUnitScoped.OrgUnitPath => Path;
    Guid IOrgUnitScoped.OrgUnitId => Id;
    public Guid OrgUnitId => Id;
    public string OrgUnitPath => Path;

    public static OrgUnit CreateRoot(Guid organizationId, Guid levelTypeId, string nameAr, string nameEn, string code)
    {
        var unit = new OrgUnit
        {
            OrganizationId = organizationId,
            LevelTypeId = levelTypeId,
            NameAr = nameAr,
            NameEn = nameEn,
            Code = code,
            Depth = 0,
        };
        unit.Path = Organizations.OrgUnitPath.Append("/", unit.Id);
        return unit;
    }

    public static OrgUnit CreateChild(OrgUnit parent, Guid levelTypeId, string nameAr, string nameEn, string code)
    {
        var unit = new OrgUnit
        {
            OrganizationId = parent.OrganizationId,
            ParentId = parent.Id,
            LevelTypeId = levelTypeId,
            NameAr = nameAr,
            NameEn = nameEn,
            Code = code,
            Depth = parent.Depth + 1,
        };
        unit.Path = Organizations.OrgUnitPath.Append(parent.Path, unit.Id);
        return unit;
    }

    public void Update(Guid levelTypeId, string nameAr, string nameEn, string code)
    {
        LevelTypeId = levelTypeId;
        NameAr = nameAr;
        NameEn = nameEn;
        Code = code;
    }

    /// <summary>Re-parents the unit: sets ParentId, Path and Depth. Rows below it are re-pathed by <see cref="IOrgUnitRepather"/>.</summary>
    public void MoveUnder(OrgUnit newParent)
    {
        ParentId = newParent.Id;
        Depth = newParent.Depth + 1;
        Path = Organizations.OrgUnitPath.Append(newParent.Path, Id);
    }

    public void Deactivate() => Status = OrgUnitStatus.Inactive;

    public void Activate() => Status = OrgUnitStatus.Active;

    public void MarkAsLevelTransfer(Guid? nextTransferUnitId)
    {
        IsLevelTransfer = true;
        NextTransferUnitId = nextTransferUnitId;
    }

    public void MarkAsCourseManagementLevel() => IsCourseManagementLevel = true;
}