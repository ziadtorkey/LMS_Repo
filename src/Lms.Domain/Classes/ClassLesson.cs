using Lms.Domain.Common;

namespace Lms.Domain.Classes;

/// <summary>A lesson of a class lesson plan; <see cref="Key"/> matches the blueprint lesson it came from.</summary>
public sealed class ClassLesson : Entity, IOrgUnitScoped
{
    private ClassLesson()
    {
        OrgUnitPath = TitleAr = TitleEn = string.Empty;
    }

    public ClassLesson(
        ClassSection section, Guid key, int order, string titleAr, string titleEn, string? objectivesAr, string? objectivesEn,
        int? plannedOffsetDays)
    {
        OrganizationId = section.OrganizationId;
        OrgUnitId = section.OrgUnitId;
        OrgUnitPath = section.OrgUnitPath;
        PlanId = section.PlanId;
        SectionId = section.Id;
        Key = key;
        Order = order;
        TitleAr = titleAr;
        TitleEn = titleEn;
        ObjectivesAr = objectivesAr;
        ObjectivesEn = objectivesEn;
        PlannedOffsetDays = plannedOffsetDays;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid SectionId { get; private set; }
    public Guid Key { get; private set; }
    public int Order { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public string? ObjectivesAr { get; private set; }
    public string? ObjectivesEn { get; private set; }
    public int? PlannedOffsetDays { get; private set; }
    public bool IsHidden { get; private set; }

    public void Update(int order, string titleAr, string titleEn, string? objectivesAr, string? objectivesEn, int? plannedOffsetDays, bool isHidden)
    {
        Order = order;
        TitleAr = titleAr;
        TitleEn = titleEn;
        ObjectivesAr = objectivesAr;
        ObjectivesEn = objectivesEn;
        PlannedOffsetDays = plannedOffsetDays;
        IsHidden = isHidden;
    }
}
