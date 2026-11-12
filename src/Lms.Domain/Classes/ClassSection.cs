using Lms.Domain.Common;

namespace Lms.Domain.Classes;

/// <summary>A section of a class lesson plan; <see cref="Key"/> matches the blueprint section it came from.</summary>
public sealed class ClassSection : Entity, IOrgUnitScoped
{
    private ClassSection()
    {
        OrgUnitPath = TitleAr = TitleEn = string.Empty;
    }

    public ClassSection(ClassLessonPlan plan, Guid key, int order, string titleAr, string titleEn)
    {
        OrganizationId = plan.OrganizationId;
        OrgUnitId = plan.OrgUnitId;
        OrgUnitPath = plan.OrgUnitPath;
        PlanId = plan.Id;
        Key = key;
        Order = order;
        TitleAr = titleAr;
        TitleEn = titleEn;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid Key { get; private set; }
    public int Order { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public bool IsHidden { get; private set; }

    public void Update(int order, string titleAr, string titleEn, bool isHidden)
    {
        Order = order;
        TitleAr = titleAr;
        TitleEn = titleEn;
        IsHidden = isHidden;
    }
}
