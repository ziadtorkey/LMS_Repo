using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.People;

/// <summary>Seeds people rows straight through the context (platform scope), for kernel tests below the API.</summary>
internal static class PeopleTestKit
{
    public static async Task<User> AddUserAsync(LmsApiFactory factory, string? email = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var name = email ?? $"u-{Guid.NewGuid():N}@lms.test";
        var user = new User
        {
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = name,
            NormalizedEmail = name.ToUpperInvariant(),
            DisplayNameAr = "مستخدم",
            DisplayNameEn = "User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<StaffProfile> AddStaffAsync(
        LmsApiFactory factory, OrgUnit home, string? email = null, string? mobile = null, string? nationalId = null,
        string nameEn = "Staff")
    {
        var user = await AddUserAsync(factory);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var staff = new StaffProfile(
            home, user.Id, "موظف", nameEn, email ?? user.Email!, mobile, nationalId, null, null, null, null);
        db.StaffProfiles.Add(staff);
        await db.SaveChangesAsync();
        return staff;
    }

    public static async Task<StudentProfile> AddStudentAsync(
        LmsApiFactory factory, OrgUnit home, string code, string nameAr = "طالب", string nameEn = "Student",
        DateOnly? dob = null, string? nationalId = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var student = new StudentProfile(
            home, code, nameAr, nameEn, dob ?? new DateOnly(2015, 5, 5), "female", "muslim", "EG");
        student.Update(nameAr, nameEn, null, null, dob ?? new DateOnly(2015, 5, 5), "female", "muslim", "EG", nationalId, null, null, null);
        db.StudentProfiles.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    public static async Task<GuardianProfile> AddGuardianAsync(
        LmsApiFactory factory, Guid organizationId, string mobile, string? nationalId = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var guardian = new GuardianProfile(organizationId, "ولي أمر", "Guardian", mobile, null, nationalId);
        db.GuardianProfiles.Add(guardian);
        await db.SaveChangesAsync();
        return guardian;
    }

    public static string NewCode() => "S" + Guid.NewGuid().ToString("N")[..10];

    public static string NewMobile() => "+2010" + Random.Shared.Next(10_000_000, 99_999_999);

    public static string NewNationalId() => "N" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}
