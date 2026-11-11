using Lms.Domain.Auditing;
using Lms.Shared.Auditing;

namespace Lms.UnitTests.Contracts;

public sealed class AuditCatalogCoverageTests
{
    [Fact]
    public void Every_Audited_domain_type_is_in_AuditCatalog()
    {
        var audited = typeof(AuditedAttribute).Assembly.GetTypes()
            .Where(t => Attribute.IsDefined(t, typeof(AuditedAttribute), inherit: true))
            .Select(t => t.Name)
            .ToList();

        Assert.NotEmpty(audited);
        var missing = audited.Where(n => !AuditCatalog.EntityTypes.Contains(n)).ToList();
        Assert.Empty(missing);
    }
}
