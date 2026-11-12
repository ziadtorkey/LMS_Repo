using Lms.Domain.Common;

namespace Lms.Domain.Blueprints;

/// <summary>A lesson of a blueprint section; the <see cref="Key"/> is stable across versions. Hard-deleted on draft saves.</summary>
public sealed class BlueprintLesson : Entity, IOrgUnitScoped
{
    private BlueprintLesson()
    {
        OrgUnitPath = TitleAr = TitleEn = string.Empty;
    }

    public BlueprintLesson(
        BlueprintSection section, Guid key, int order, string titleAr, string titleEn, string? objectivesAr, string? objectivesEn,
        int? plannedOffsetDays)
    {
        OrganizationId = section.OrganizationId;
        OrgUnitId = section.OrgUnitId;
        OrgUnitPath = section.OrgUnitPath;
        BlueprintId = section.BlueprintId;
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
    public Guid BlueprintId { get; private set; }
    public Guid SectionId { get; private set; }
    public Guid Key { get; private set; }
    public int Order { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public string? ObjectivesAr { get; private set; }
    public string? ObjectivesEn { get; private set; }

    /// <summary>Days from the academic year start (0..400); null = not scheduled.</summary>
    public int? PlannedOffsetDays { get; private set; }
}
