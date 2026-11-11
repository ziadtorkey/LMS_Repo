using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class StaffBackfillTests(LmsApiFactory factory)
{
    private async Task<int> RunAsync()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await new StaffBackfill(db, NullLogger<StaffBackfill>.Instance).RunAsync(default);
    }

    private async Task<List<StaffProfile>> ProfilesAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<StaffProfile>().AsNoTracking().Where(p => p.UserId == userId).ToListAsync();
    }

    [Fact]
    public async Task Backfill_creates_one_profile_per_bare_user_per_client_and_is_idempotent()
    {
        var a = await AccessTestData.SeedAsync(factory, "BF1A");
        var b = await AccessTestData.SeedAsync(factory, "BF1B");
        var user = await PeopleTestKit.AddUserAsync(factory);
        // Several assignments in client A (the highest one is the root) and one in client B.
        await AccessTestData.AssignAsync(factory, a, a.School2, BuiltInRoles.Teacher, user.Id);
        await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.Manager, user.Id);
        await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user.Id);
        await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user.Id);

        await RunAsync();
        var created = await ProfilesAsync(user.Id);
        var again = await RunAsync();

        Assert.Equal(2, created.Count);
        var inA = Assert.Single(created, p => p.OrganizationId == a.Org.Id);
        var inB = Assert.Single(created, p => p.OrganizationId == b.Org.Id);
        Assert.Equal(a.Root.Id, inA.OrgUnitId); // the unit of the highest (shortest path) assignment
        Assert.Equal(b.School1.Id, inB.OrgUnitId);
        Assert.Equal(user.Email!.ToLowerInvariant(), inA.Email);
        Assert.Equal((PersonStatus.Active, user.DisplayNameEn, user.DisplayNameAr), (inA.Status, inA.NameEn, inA.NameAr));
        Assert.Equal(0, again);
        Assert.Equal(2, (await ProfilesAsync(user.Id)).Count);
    }

    [Theory]
    [InlineData(UserStatus.Active, PersonStatus.Active)]
    [InlineData(UserStatus.Deactivated, PersonStatus.Deactivated)]
    [InlineData(UserStatus.Expired, PersonStatus.Expired)]
    [InlineData(UserStatus.Withdrawn, PersonStatus.Deactivated)]
    public async Task Backfill_carries_over_the_M1_account_status(UserStatus userStatus, PersonStatus expected)
    {
        var tree = await AccessTestData.SeedAsync(factory, "BF4" + userStatus);
        var user = await PeopleTestKit.AddUserAsync(factory);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Users.SingleAsync(u => u.Id == user.Id)).Status = userStatus;
            await db.SaveChangesAsync();
        }

        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user.Id);
        await RunAsync();

        var profile = Assert.Single(await ProfilesAsync(user.Id));
        Assert.Equal(expected, profile.Status);
        // A deactivated or expired person must not regain access through the profile the backfill created.
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var valid = await read.ValidAssignmentsFor(user.Id, DateOnly.FromDateTime(DateTime.UtcNow))
            .AnyAsync(a => a.AssignmentId == assignment.Id);
        Assert.Equal(expected == PersonStatus.Active, valid);
    }

    [Fact]
    public async Task Backfill_leaves_existing_profiles_students_guardians_and_deleted_assignments_alone()
    {
        var tree = await AccessTestData.SeedAsync(factory, "BF2");
        var hasProfile = await PeopleTestKit.AddStaffAsync(factory, tree.School1);
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, hasProfile.UserId);
        var student = await PeopleTestKit.AddUserAsync(factory);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, student.Id);
        var guardian = await PeopleTestKit.AddUserAsync(factory);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, guardian.Id);
        var removed = await PeopleTestKit.AddUserAsync(factory);
        var gone = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, removed.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.RoleAssignments.Remove(await db.RoleAssignments.SingleAsync(x => x.Id == gone.Id));
            await db.SaveChangesAsync();
        }

        await RunAsync();

        var existing = Assert.Single(await ProfilesAsync(hasProfile.UserId));
        Assert.Equal(tree.School1.Id, existing.OrgUnitId); // not moved to the root
        Assert.Empty(await ProfilesAsync(student.Id));
        Assert.Empty(await ProfilesAsync(guardian.Id));
        Assert.Empty(await ProfilesAsync(removed.Id));
    }

    [Fact]
    public async Task Backfill_skips_a_user_without_email_and_a_deleted_home_unit()
    {
        var tree = await AccessTestData.SeedAsync(factory, "BF3");
        var noEmail = await PeopleTestKit.AddUserAsync(factory);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == noEmail.Id);
            tracked.Email = null;
            tracked.NormalizedEmail = null;
            await db.SaveChangesAsync();
        }

        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, noEmail.Id);
        var onDeleted = await PeopleTestKit.AddUserAsync(factory);
        await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Teacher, onDeleted.Id);
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, onDeleted.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.OrgUnits.Remove(await db.OrgUnits.SingleAsync(u => u.Id == tree.Root.Id)); // soft delete the highest unit
            await db.SaveChangesAsync();
        }

        await RunAsync();

        Assert.Empty(await ProfilesAsync(noEmail.Id));
        // The deleted root is skipped: the next highest live unit becomes the home.
        Assert.Equal(tree.School2.Id, Assert.Single(await ProfilesAsync(onDeleted.Id)).OrgUnitId);
    }
}
