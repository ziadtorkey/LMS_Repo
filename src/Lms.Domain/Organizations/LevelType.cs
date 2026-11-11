using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Organizations;

/// <summary>A kind of node in a client's tree (School, Stage, Section, Faculty...).</summary>
[Audited]
public sealed class LevelType : AuditableEntity, ITenantOwned
{
    private LevelType()
    {
        NameAr = NameEn = string.Empty;
    }

    public LevelType(Guid organizationId, string nameAr, string nameEn, int order, bool canHostClasses)
    {
        OrganizationId = organizationId;
        NameAr = nameAr;
        NameEn = nameEn;
        Order = order;
        CanHostClasses = canHostClasses;
    }

    public Guid OrganizationId { get; private set; }
    public string NameAr { get; private set; }
    public string NameEn { get; private set; }
    public int Order { get; private set; }
    public bool CanHostClasses { get; private set; }

    public void Update(string nameAr, string nameEn, bool canHostClasses)
    {
        NameAr = nameAr;
        NameEn = nameEn;
        CanHostClasses = canHostClasses;
    }

    public void MoveTo(int order) => Order = order;
}