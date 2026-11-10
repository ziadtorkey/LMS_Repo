using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

/// <summary>One client with Root → School1 and the seven built-in roles.</summary>
public sealed record AccessTree(Organization Org, OrgUnit Root, OrgUnit School1, IReadOnlyDictionary<string, Role> Roles);

public static class AccessTestData
{
    public static async Task<AccessTree> SeedAsync(
        LmsApiFactory factory, string name, OrganizationType type = OrganizationType.SchoolGroup)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var org = new Organization(Guid.NewGuid(), "عميل " + name, name, type);
        var level = new LevelType(org.Id, "مستوى", "Level", 1, true);
        var root = OrgUnit.CreateRoot(org.Id, level.Id, "جذر", "Root " + name, "R");
        var school = OrgUnit.CreateChild(root, level.Id, "مدرسة", "School " + name, "S1");
        db.AddRange(org, level, root, school);
        await db.SaveChangesAsync();

        await new DefaultRoleProvisioner(db).ProvisionAsync(org.Id, type, CancellationToken.None);
        var roles = await db.Roles.Where(r => r.OrganizationId == org.Id).ToDictionaryAsync(r => r.BuiltInKey!);
        return new AccessTree(org, root, school, roles);
    }

    public static async Task<RoleAssignment> AssignAsync(
        LmsApiFactory factory, AccessTree tree, OrgUnit unit, string roleKey, Guid userId,
        DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var assignment = new RoleAssignment(unit, userId, tree.Roles[roleKey].Id, validFrom, validTo);
        db.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }
}