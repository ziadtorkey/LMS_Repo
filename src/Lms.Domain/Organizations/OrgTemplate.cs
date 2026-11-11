using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Organizations;

public enum TemplateVisibility { Private, Shared }

/// <summary>
/// A saved branch structure that can be applied again under another unit. Not <see cref="IOrgUnitScoped"/>: a Shared
/// template is visible across sibling subtrees, so visibility is applied explicitly in queries. <c>OrgUnitPath</c> is the
/// owner unit's path, which lets a unit move re-path it.
/// </summary>
[Audited]
public sealed class OrgTemplate : AuditableEntity, ITenantOwned
{
    private OrgTemplate()
    {
        NameAr = NameEn = OrgUnitPath = SnapshotJson = Placeholders = string.Empty;
    }

    public OrgTemplate(
        OrgUnit owner, string nameAr, string nameEn, TemplateVisibility visibility, string snapshotJson, int unitCount,
        IEnumerable<string> placeholders)
    {
        OrganizationId = owner.OrganizationId;
        OrgUnitId = owner.Id;
        OrgUnitPath = owner.Path;
        NameAr = nameAr;
        NameEn = nameEn;
        Visibility = visibility;
        SnapshotJson = snapshotJson;
        UnitCount = unitCount;
        Placeholders = string.Join(',', placeholders.Order(StringComparer.Ordinal));
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public TemplateVisibility Visibility { get; private set; }

    /// <summary>The captured branch (a BranchSnapshot) as JSON, with placeholders in names and codes.</summary>
    [AuditIgnore]
    public string SnapshotJson { get; private set; }

    public int UnitCount { get; private set; }

    /// <summary>Placeholder names without braces, comma separated.</summary>
    public string Placeholders { get; private set; }
}
