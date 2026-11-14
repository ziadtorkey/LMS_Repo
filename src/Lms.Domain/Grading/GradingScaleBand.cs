using Lms.Domain.Common;

namespace Lms.Domain.Grading;

/// <summary>
/// One band of a <see cref="GradingScale"/>. Bands are replaced wholesale on a save, so they are hard-deleted (no soft delete) and not
/// audited: the audit trail is on the scale. They copy the scale's unit fields.
/// </summary>
public sealed class GradingScaleBand : Entity, IOrgUnitScoped
{
    private GradingScaleBand()
    {
        OrgUnitPath = Code = string.Empty;
    }

    public GradingScaleBand(
        GradingScale scale, int order, decimal minPercent, decimal maxPercent, string code,
        string? labelAr, string? labelEn, string? descriptorAr, string? descriptorEn, decimal? points)
    {
        OrganizationId = scale.OrganizationId;
        OrgUnitId = scale.OrgUnitId;
        OrgUnitPath = scale.OrgUnitPath;
        ScaleId = scale.Id;
        Order = order;
        MinPercent = minPercent;
        MaxPercent = maxPercent;
        Code = code;
        LabelAr = labelAr;
        LabelEn = labelEn;
        DescriptorAr = descriptorAr;
        DescriptorEn = descriptorEn;
        Points = points;
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid ScaleId { get; private set; }
    public int Order { get; private set; }
    public decimal MinPercent { get; private set; }
    public decimal MaxPercent { get; private set; }
    public string Code { get; private set; }
    public string? LabelAr { get; private set; }
    public string? LabelEn { get; private set; }
    public string? DescriptorAr { get; private set; }
    public string? DescriptorEn { get; private set; }
    public decimal? Points { get; private set; }
}
