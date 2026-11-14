using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Grading;

/// <summary>
/// The categories and weights a teacher applies to a gradebook (applying copies the values, so a later edit of the template changes no
/// gradebook). Owned by an org unit and visible to its descendants. Weights should sum to 100: a warning, not a refusal.
/// </summary>
[Audited]
public sealed class WeightTemplate : UnitOwnedEntity
{
    private WeightTemplate()
    {
        NameAr = NameEn = string.Empty;
    }

    public WeightTemplate(OrgUnit unit, string nameAr, string nameEn)
    {
        PlaceIn(unit);
        NameAr = nameAr;
        NameEn = nameEn;
    }

    public string NameAr { get; private set; }
    public string NameEn { get; private set; }

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = nameAr;
        NameEn = nameEn;
    }
}

/// <summary>One category of a <see cref="WeightTemplate"/>. Replaced wholesale on a save (hard delete, not audited).</summary>
public sealed class WeightTemplateItem : Entity, IOrgUnitScoped
{
    private WeightTemplateItem()
    {
        OrgUnitPath = NameAr = NameEn = string.Empty;
    }

    public WeightTemplateItem(WeightTemplate template, int order, string nameAr, string nameEn, decimal weight)
    {
        OrganizationId = template.OrganizationId;
        OrgUnitId = template.OrgUnitId;
        OrgUnitPath = template.OrgUnitPath;
        TemplateId = template.Id;
        Order = order;
        NameAr = nameAr;
        NameEn = nameEn;
        Weight = weight;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid TemplateId { get; private set; }
    public int Order { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public decimal Weight { get; private set; }
}
