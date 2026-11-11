using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class PeopleIsolationTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Staff_student_link_and_history_rows_are_isolated_by_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "PI1A");
        var b = await AccessTestData.SeedAsync(factory, "PI1B");
        var staff = await PeopleTestKit.AddStaffAsync(factory, a.School2);
        var student = await PeopleTestKit.AddStudentAsync(factory, a.School2, PeopleTestKit.NewCode());
        var guardian = await PeopleTestKit.AddGuardianAsync(factory, a.Org.Id, PeopleTestKit.NewMobile());
        Guid linkId, changeId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var link = StudentGuardian.For(student, guardian, "father", true);
            var change = PersonStatusChange.For(staff, PersonStatus.Active, PersonStatus.Deactivated, "left", Guid.NewGuid(), DateTime.UtcNow);
            db.AddRange(link, change);
            await db.SaveChangesAsync();
            (linkId, changeId) = (link.Id, change.Id);
        }

        // The parent (root) sees School2's rows; School1 and the other client do not.
        await AssertVisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), true, staff.Id, student.Id, linkId, changeId);
        await AssertVisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), true, staff.Id, student.Id, linkId, changeId);
        await AssertVisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), false, staff.Id, student.Id, linkId, changeId);
        await AssertVisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), false, staff.Id, student.Id, linkId, changeId);
        await AssertVisibleAsync(TestCurrentContext.PlatformAdmin(), true, staff.Id, student.Id, linkId, changeId);
    }

    private async Task AssertVisibleAsync(TestCurrentContext context, bool expected, Guid staffId, Guid studentId, Guid linkId, Guid changeId)
    {
        await using var db = factory.CreateDbContext(context);
        Assert.Equal(expected, await db.Set<StaffProfile>().AnyAsync(x => x.Id == staffId));
        Assert.Equal(expected, await db.Set<StudentProfile>().AnyAsync(x => x.Id == studentId));
        Assert.Equal(expected, await db.Set<StudentGuardian>().AnyAsync(x => x.Id == linkId));
        Assert.Equal(expected, await db.Set<PersonStatusChange>().AnyAsync(x => x.Id == changeId));
    }

    [Fact]
    public async Task Guardian_rows_are_isolated_by_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "PI2A");
        var b = await AccessTestData.SeedAsync(factory, "PI2B");
        var guardian = await PeopleTestKit.AddGuardianAsync(factory, a.Org.Id, PeopleTestKit.NewMobile());

        await using (var db = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path)))
        {
            // Client-wide: any unit scope of the client sees the guardian.
            Assert.True(await db.Set<GuardianProfile>().AnyAsync(g => g.Id == guardian.Id));
        }

        await using var other = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        Assert.False(await other.Set<GuardianProfile>().AnyAsync(g => g.Id == guardian.Id));
    }

    [Fact]
    public async Task Student_code_is_unique_per_client_even_after_soft_delete_but_reusable_in_other_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "PI3A");
        var b = await AccessTestData.SeedAsync(factory, "PI3B");
        var code = PeopleTestKit.NewCode();
        var first = await PeopleTestKit.AddStudentAsync(factory, a.School1, code);

        await Assert.ThrowsAsync<DbUpdateException>(() => PeopleTestKit.AddStudentAsync(factory, a.School2, code));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Set<StudentProfile>().Remove(await db.Set<StudentProfile>().SingleAsync(s => s.Id == first.Id)); // soft delete
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => PeopleTestKit.AddStudentAsync(factory, a.School1, code));
        await PeopleTestKit.AddStudentAsync(factory, b.School1, code); // another client may use the same code
    }

    [Fact]
    public async Task Repather_covers_people_tables()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var covered = Lms.Infrastructure.Organizations.OrgUnitRepather.DiscoverTargets(db.Model).Select(t => t.ClrType).ToHashSet();

        Assert.Contains(typeof(StaffProfile), covered);
        Assert.Contains(typeof(StudentProfile), covered);
        Assert.Contains(typeof(StudentGuardian), covered);
        Assert.Contains(typeof(PersonStatusChange), covered);
    }
}
