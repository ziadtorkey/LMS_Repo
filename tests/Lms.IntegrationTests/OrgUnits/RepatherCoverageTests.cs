using Lms.Domain.Common;
using Lms.Infrastructure.Organizations;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.OrgUnits;

[Collection(ApiCollection.Name)]
public sealed class RepatherCoverageTests(LmsApiFactory factory)
{
    [Fact]
    public void Repather_covers_every_entity_with_OrgUnitPath_in_the_model()
    {
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var covered = OrgUnitRepather.DiscoverTargets(db.Model).Select(t => t.ClrType).ToHashSet();
        var scoped = db.Model.GetEntityTypes().Select(t => t.ClrType).Where(t => typeof(IOrgUnitScoped).IsAssignableFrom(t)).ToList();

        Assert.NotEmpty(scoped);
        Assert.All(scoped, type => Assert.Contains(type, covered));
    }

    [Fact]
    public void Every_target_has_an_organization_column_and_a_path_column()
    {
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        foreach (var target in OrgUnitRepather.DiscoverTargets(db.Model))
        {
            Assert.False(string.IsNullOrWhiteSpace(target.Table));
            Assert.False(string.IsNullOrWhiteSpace(target.PathColumn));
            Assert.False(string.IsNullOrWhiteSpace(target.OrganizationColumn));
        }
    }
}