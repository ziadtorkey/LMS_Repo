using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Seeding;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Lms.IntegrationTests.Seeding;

/// <summary>M6 Task 18: the people of the E2E <c>MessagingWorld</c> and their relationships exist after the Development seed, once, and only in the school client.</summary>
[Collection(ApiCollection.Name)]
public sealed class M6DemoSeedTests(LmsApiFactory factory)
{
    private sealed class DevEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Lms.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static string Email(string name) => M6DemoSeeder.Email(name);

    private async Task<string> SeededAsync()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await new DevSeeder(cs, new DevEnvironment(), new SeedOptions("admin@lms.test", "DevPass123")).SeedAsync();
        return cs;
    }

    private static LmsDbContext Open(string cs) => LmsDbContextFactory.Create(cs, TestCurrentContext.PlatformAdmin());

    private static async Task<Guid> UserAsync(LmsDbContext db, string name) =>
        (await DevSeeder.UserIdAsync(db, Email(name), CancellationToken.None))!.Value;

    [Fact]
    public async Task The_messaging_world_people_exist_with_their_relationships()
    {
        var cs = await SeededAsync();
        await using var db = Open(cs);
        var (s1, s2, g1, g2, teacher) = (await UserAsync(db, "student1"), await UserAsync(db, "student2"), await UserAsync(db, "guardian1"), await UserAsync(db, "guardian2"), await UserAsync(db, "teacher"));

        var classId = (await db.Set<Class>().SingleAsync(c => c.CodeKey == M6DemoSeeder.ClassCode)).Id;
        var classCourse = await db.Set<ClassCourse>().SingleAsync(cc => cc.ClassId == classId);
        Assert.Contains(await db.Set<ClassCourseTeacher>().Where(t => t.ClassCourseId == classCourse.Id).Select(t => t.UserId).ToListAsync(), id => id == teacher);

        var students = await db.Set<StudentProfile>().Where(p => p.UserId == s1 || p.UserId == s2).ToListAsync();
        Assert.Equal(2, students.Count);
        var profile1 = students.Single(p => p.UserId == s1);
        var profile2 = students.Single(p => p.UserId == s2);
        // each student has a guardian of their own and no other
        var links = await db.Set<StudentGuardian>().Select(l => new { l.StudentId, l.GuardianId }).ToListAsync();
        var guardian1 = await db.Set<GuardianProfile>().SingleAsync(g => g.UserId == g1);
        var guardian2 = await db.Set<GuardianProfile>().SingleAsync(g => g.UserId == g2);
        Assert.Equal([guardian1.Id], links.Where(l => l.StudentId == profile1.Id).Select(l => l.GuardianId));
        Assert.Equal([guardian2.Id], links.Where(l => l.StudentId == profile2.Id).Select(l => l.GuardianId));

        // both in the class course, in different groups
        var members = await db.Set<GroupMember>().Where(m => m.ClassCourseId == classCourse.Id).Select(m => new { m.StudentId, m.GroupId }).ToListAsync();
        Assert.Equal(2, members.Count);
        Assert.NotEqual(members.Single(m => m.StudentId == profile1.Id).GroupId, members.Single(m => m.StudentId == profile2.Id).GroupId);
    }

    [Fact]
    public async Task Each_person_has_the_role_the_scenarios_assume()
    {
        var cs = await SeededAsync();
        await using var db = Open(cs);

        async Task<string> RoleOfAsync(string name)
        {
            var id = await UserAsync(db, name);
            return await db.RoleAssignments.Where(a => a.UserId == id).Join(db.Roles, a => a.RoleId, r => r.Id, (_, r) => r.BuiltInKey!).SingleAsync();
        }

        Assert.Equal("Student", await RoleOfAsync("student1"));
        Assert.Equal("Student", await RoleOfAsync("student2"));
        Assert.Equal("Guardian", await RoleOfAsync("guardian1"));
        Assert.Equal("Guardian", await RoleOfAsync("guardian2"));
        Assert.Equal("Teacher", await RoleOfAsync("teacher"));
        Assert.Equal("Manager", await RoleOfAsync("manager"));
        Assert.Equal("OrgAdmin", await RoleOfAsync("orgadmin"));
    }

    [Fact]
    public async Task Seeding_twice_adds_nothing_and_the_university_gets_no_messaging_people()
    {
        var cs = await SeededAsync();
        await using (var first = Open(cs))
        {
            var counts = (await first.Users.CountAsync(), await first.Set<StudentProfile>().CountAsync(), await first.Set<GuardianProfile>().CountAsync(), await first.Set<ClassCourse>().CountAsync());
            await new DevSeeder(cs, new DevEnvironment(), new SeedOptions("admin@lms.test", "DevPass123")).SeedAsync();
            await using var again = Open(cs);
            Assert.Equal(counts, (await again.Users.CountAsync(), await again.Set<StudentProfile>().CountAsync(), await again.Set<GuardianProfile>().CountAsync(), await again.Set<ClassCourse>().CountAsync()));
            var universityId = DevSeedData.UniversityId;
            Assert.False(await again.Set<StudentProfile>().AnyAsync(p => p.OrganizationId == universityId));
        }
    }
}
