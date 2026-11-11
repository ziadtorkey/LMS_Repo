using Lms.Domain.Organizations;
using Lms.Infrastructure;
using Lms.Infrastructure.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;

namespace Lms.IntegrationTests.Notifications;

[Collection(ApiCollection.Name)]
public sealed class AncestorRecipientsTests(LmsApiFactory factory)
{
    [Fact]
    public async Task AncestorsAndSelf_event_reaches_school_manager_above_section_but_not_sibling_school()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AR1");
        OrgUnit section;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            section = OrgUnit.CreateChild(tree.School1, tree.School1.LevelTypeId, "قسم", "Section", "SEC");
            db.Add(section);
            await db.SaveChangesAsync();
        }

        var rootAdmin = Guid.NewGuid();
        var schoolManager = Guid.NewGuid();
        var sectionManager = Guid.NewGuid();
        var siblingManager = Guid.NewGuid();
        var teacher = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, rootAdmin);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, schoolManager);
        await AccessTestData.AssignAsync(factory, tree, section, BuiltInRoles.Manager, sectionManager);
        await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Manager, siblingManager);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, teacher);

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resolver = new NotificationRecipientResolver(read, new SystemClock());

        var upward = await resolver.ResolveAsync(NotificationEvents.StaffExpired, tree.Org.Id, section.Path);

        Assert.Equal(new[] { rootAdmin, schoolManager, sectionManager }.Order(), upward.Order());
    }

    [Fact]
    public async Task Subtree_event_still_reaches_only_units_at_or_below()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AR2");
        var rootAdmin = Guid.NewGuid();
        var schoolManager = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, rootAdmin);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, schoolManager);

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resolver = new NotificationRecipientResolver(read, new SystemClock());

        var recipients = await resolver.ResolveAsync(NotificationEvents.OrganizationPolicyChanged, tree.Org.Id, tree.School1.Path);

        Assert.Equal([schoolManager], recipients);
    }
}
