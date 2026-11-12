using Lms.Domain.Common;

namespace Lms.Domain.Blueprints;

/// <summary>
/// A section (week, unit or chapter) of a blueprint. The <see cref="Key"/> stays the same in every version, so class
/// plans can be merged with a newer version. Rows are replaced wholesale on a draft save, so they are hard-deleted
/// (no soft delete) and not audited: the audit trail is on the <see cref="Blueprint"/>.
/// </summary>
public sealed class BlueprintSection : Entity, IOrgUnitScoped
{
    private BlueprintSection()
    {
        OrgUnitPath = TitleAr = TitleEn = string.Empty;
    }

    public BlueprintSection(Blueprint blueprint, Guid key, int order, string titleAr, string titleEn)
    {
        OrganizationId = blueprint.OrganizationId;
        OrgUnitId = blueprint.OrgUnitId;
        OrgUnitPath = blueprint.OrgUnitPath;
        BlueprintId = blueprint.Id;
        Key = key;
        Order = order;
        TitleAr = titleAr;
        TitleEn = titleEn;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid BlueprintId { get; private set; }
    public Guid Key { get; private set; }
    public int Order { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
}
