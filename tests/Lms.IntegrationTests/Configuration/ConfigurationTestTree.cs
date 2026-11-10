using Lms.Domain.Organizations;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.Configuration;

/// <summary>One client: Root → School1 → Section, and Root → School2.</summary>
internal sealed record ConfigurationTestTree(
    Organization Org, OrgUnit Root, OrgUnit School1, OrgUnit Section, OrgUnit School2)
{
    public static async Task<ConfigurationTestTree> SeedAsync(LmsApiFactory factory, string name)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var org = new Organization(Guid.NewGuid(), "مجموعة " + name, name, OrganizationType.SchoolGroup);
        var level = new LevelType(org.Id, "مستوى", "Level", 1, true);
        var root = OrgUnit.CreateRoot(org.Id, level.Id, "جذر", "Root " + name, "R");
        var school1 = OrgUnit.CreateChild(root, level.Id, "مدرسة 1", "School1 " + name, "S1");
        var section = OrgUnit.CreateChild(school1, level.Id, "فصل", "Section " + name, "SEC");
        var school2 = OrgUnit.CreateChild(root, level.Id, "مدرسة 2", "School2 " + name, "S2");
        db.AddRange(org, level, root, school1, section, school2);
        await db.SaveChangesAsync();
        return new ConfigurationTestTree(org, root, school1, section, school2);
    }

    /// <summary>A caller acting at the root of this client.</summary>
    public TestCurrentContext AtRoot() => TestCurrentContext.For(Org.Id, Root.Path);
}
