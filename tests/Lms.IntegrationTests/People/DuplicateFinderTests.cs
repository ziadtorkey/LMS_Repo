using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class DuplicateFinderTests(LmsApiFactory factory)
{
    // The finder is built with a School1-scoped context: the duplicates below sit in School2 or in another client.
    private PersonDuplicateFinderScope Finder(Guid organizationId, string scopePath) => new(factory, organizationId, scopePath);

    private sealed class PersonDuplicateFinderScope : IDisposable
    {
        private readonly LmsApiFactory _factory;
        private readonly Lms.Infrastructure.Persistence.LmsDbContext _db;

        public PersonDuplicateFinderScope(LmsApiFactory factory, Guid organizationId, string scopePath)
        {
            _factory = factory;
            _db = factory.CreateDbContext(TestCurrentContext.For(organizationId, scopePath));
            Finder = new Lms.Infrastructure.People.PersonDuplicateFinder(_db);
        }

        public IPersonDuplicateFinder Finder { get; }

        public void Dispose() => _db.Dispose();
    }

    [Fact]
    public async Task Duplicate_finder_sees_sibling_subtree_but_not_other_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "DF1A");
        var b = await AccessTestData.SeedAsync(factory, "DF1B");
        var mobile = PeopleTestKit.NewMobile();
        var nationalId = PeopleTestKit.NewNationalId();
        var email = $"dup-{Guid.NewGuid():N}@lms.test";
        await PeopleTestKit.AddStaffAsync(factory, a.School2, email, mobile, nationalId);

        using var inA = Finder(a.Org.Id, a.School1.Path);
        Assert.Equal("email", await inA.Finder.FindStaffConflictAsync(a.Org.Id, email, null, null, null, default));
        Assert.Equal("mobile", await inA.Finder.FindStaffConflictAsync(a.Org.Id, "other@lms.test", mobile, null, null, default));
        Assert.Equal("nationalId", await inA.Finder.FindStaffConflictAsync(a.Org.Id, "other@lms.test", null, nationalId, null, default));
        Assert.Null(await inA.Finder.FindStaffConflictAsync(a.Org.Id, "other@lms.test", PeopleTestKit.NewMobile(), PeopleTestKit.NewNationalId(), null, default));

        // The same values in another client are no conflict.
        using var inB = Finder(b.Org.Id, b.Root.Path);
        Assert.Null(await inB.Finder.FindStaffConflictAsync(b.Org.Id, email, mobile, nationalId, null, default));
    }

    [Fact]
    public async Task Staff_conflict_ignores_the_excluded_profile()
    {
        var a = await AccessTestData.SeedAsync(factory, "DF2");
        var email = $"self-{Guid.NewGuid():N}@lms.test";
        var staff = await PeopleTestKit.AddStaffAsync(factory, a.School1, email);

        using var scope = Finder(a.Org.Id, a.Root.Path);

        Assert.Equal("email", await scope.Finder.FindStaffConflictAsync(a.Org.Id, email, null, null, null, default));
        Assert.Null(await scope.Finder.FindStaffConflictAsync(a.Org.Id, email, null, null, staff.Id, default));
    }

    [Fact]
    public async Task Deleted_profiles_are_not_duplicates()
    {
        var a = await AccessTestData.SeedAsync(factory, "DF3");
        var email = $"gone-{Guid.NewGuid():N}@lms.test";
        var mobile = PeopleTestKit.NewMobile();
        var nationalId = PeopleTestKit.NewNationalId();
        var staff = await PeopleTestKit.AddStaffAsync(factory, a.School2, email, mobile, nationalId);
        var student = await PeopleTestKit.AddStudentAsync(
            factory, a.School2, PeopleTestKit.NewCode(), nameAr: "مريم", nameEn: "Maryam", nationalId: nationalId);
        var guardian = await PeopleTestKit.AddGuardianAsync(factory, a.Org.Id, mobile, nationalId);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.StaffProfiles.Remove(await db.StaffProfiles.SingleAsync(x => x.Id == staff.Id));
            db.StudentProfiles.Remove(await db.StudentProfiles.SingleAsync(x => x.Id == student.Id));
            db.GuardianProfiles.Remove(await db.GuardianProfiles.SingleAsync(x => x.Id == guardian.Id));
            await db.SaveChangesAsync();
        }

        using var scope = Finder(a.Org.Id, a.School1.Path);

        Assert.Null(await scope.Finder.FindStaffConflictAsync(a.Org.Id, email, mobile, nationalId, null, default));
        Assert.False(await scope.Finder.StudentNationalIdExistsAsync(a.Org.Id, nationalId, null, default));
        Assert.Equal(0, await scope.Finder.CountStudentsByNameAndDobAsync(
            a.Org.Id, PersonNormalizer.NameKey("مريم"), PersonNormalizer.NameKey("Maryam"), new DateOnly(2015, 5, 5), null, default));
        Assert.Null(await scope.Finder.FindGuardianAsync(a.Org.Id, nationalId, mobile, default));
    }

    [Fact]
    public async Task Student_checks_are_client_wide_and_match_either_name_key()
    {
        var a = await AccessTestData.SeedAsync(factory, "DF4A");
        var b = await AccessTestData.SeedAsync(factory, "DF4B");
        var nationalId = PeopleTestKit.NewNationalId();
        var dob = new DateOnly(2014, 3, 3);
        var student = await PeopleTestKit.AddStudentAsync(
            factory, a.School2, PeopleTestKit.NewCode(), nameAr: "فاطمة علي", nameEn: "Fatma Ali", dob: dob, nationalId: nationalId);

        using var inA = Finder(a.Org.Id, a.School1.Path);
        Assert.True(await inA.Finder.StudentNationalIdExistsAsync(a.Org.Id, nationalId, null, default));
        Assert.False(await inA.Finder.StudentNationalIdExistsAsync(a.Org.Id, nationalId, student.Id, default));
        // Arabic spelling variant matches through the name key; the English name differs.
        Assert.Equal(1, await inA.Finder.CountStudentsByNameAndDobAsync(
            a.Org.Id, PersonNormalizer.NameKey("فاطمه علي"), PersonNormalizer.NameKey("Other"), dob, null, default));
        Assert.Equal(1, await inA.Finder.CountStudentsByNameAndDobAsync(
            a.Org.Id, PersonNormalizer.NameKey("غير"), PersonNormalizer.NameKey("FATMA ali"), dob, null, default));
        Assert.Equal(0, await inA.Finder.CountStudentsByNameAndDobAsync(
            a.Org.Id, PersonNormalizer.NameKey("فاطمة علي"), PersonNormalizer.NameKey("Fatma Ali"), dob.AddDays(1), null, default));

        using var inB = Finder(b.Org.Id, b.Root.Path);
        Assert.False(await inB.Finder.StudentNationalIdExistsAsync(b.Org.Id, nationalId, null, default));
    }

    [Fact]
    public async Task Guardian_lookup_prefers_national_id_then_mobile_and_stays_in_the_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "DF5A");
        var b = await AccessTestData.SeedAsync(factory, "DF5B");
        var mobile = PeopleTestKit.NewMobile();
        var nationalId = PeopleTestKit.NewNationalId();
        var byMobile = await PeopleTestKit.AddGuardianAsync(factory, a.Org.Id, mobile);
        var byId = await PeopleTestKit.AddGuardianAsync(factory, a.Org.Id, PeopleTestKit.NewMobile(), nationalId);
        await PeopleTestKit.AddGuardianAsync(factory, b.Org.Id, PeopleTestKit.NewMobile(), nationalId);

        using var inA = Finder(a.Org.Id, a.School1.Path);

        Assert.Equal(byId.Id, (await inA.Finder.FindGuardianAsync(a.Org.Id, nationalId, mobile, default))!.Id);
        Assert.Equal(byMobile.Id, (await inA.Finder.FindGuardianAsync(a.Org.Id, PeopleTestKit.NewNationalId(), mobile, default))!.Id);
        Assert.Null(await inA.Finder.FindGuardianAsync(a.Org.Id, PeopleTestKit.NewNationalId(), PeopleTestKit.NewMobile(), default));
        Assert.Null(await inA.Finder.FindGuardianAsync(a.Org.Id, null, null, default));

        using var inB = Finder(b.Org.Id, b.Root.Path);
        Assert.NotEqual(byId.Id, (await inB.Finder.FindGuardianAsync(b.Org.Id, nationalId, null, default))!.Id);
    }
}
