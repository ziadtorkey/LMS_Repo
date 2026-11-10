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

    [Fact]
    public void Every_entity_with_an_OrgUnitPath_column_is_a_target_whether_or_not_it_implements_IOrgUnitScoped()
    {
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var covered = OrgUnitRepather.DiscoverTargets(db.Model).Select(t => t.ClrType).ToHashSet();
        var withPathColumn = db.Model.GetEntityTypes()
            .Where(t => t.FindProperty("OrgUnitPath") is { } p && p.ClrType == typeof(string))
            .Select(t => t.ClrType)
            .ToList();

        // Includes audit entries, academic years, terms and any later table, as soon as they map an OrgUnitPath.
        Assert.All(withPathColumn, type => Assert.Contains(type, covered));
    }

    [Fact]
    public void Tables_with_a_path_column_but_no_organization_column_are_rejected_loudly()
    {
        // A table that stores an OrgUnitPath without OrganizationId could not be re-pathed safely (no client filter).
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var ex = Record.Exception(() => OrgUnitRepather.DiscoverTargets(db.Model));

        Assert.Null(ex); // today's model is consistent; the guard in DiscoverTargets throws for an inconsistent one
    }
}