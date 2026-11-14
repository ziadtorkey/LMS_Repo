using System.Diagnostics;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Seeding;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Seeding;

/// <summary>M7 X-09: the performance seed builds a consistent client at a small size, is idempotent, stays inside its own client and is Development only.</summary>
[Collection(ApiCollection.Name)]
public sealed class PerfSeedTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Lms.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static DevSeeder Seeder(string cs, int students, string environment = "Development") =>
        new(cs, new Env(environment), new SeedOptions("admin@lms.test", "DevPass123", students));

    private static LmsDbContext Open(string cs) => LmsDbContextFactory.Create(cs, TestCurrentContext.PlatformAdmin());

    [Fact]
    public async Task A_small_perf_client_is_consistent_and_idempotent()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(cs, 600).SeedAsync();

        await using var db = Open(cs);
        var org = PerfSeeder.OrganizationId;
        Assert.Equal(600, await db.Set<StudentProfile>().CountAsync(s => s.OrganizationId == org));
        Assert.Equal(600, await db.Set<GuardianProfile>().CountAsync(g => g.OrganizationId == org));
        Assert.Equal(600, await db.Set<StudentGuardian>().CountAsync(l => l.OrganizationId == org));
        var classes = await db.Set<Class>().CountAsync(c => c.OrganizationId == org);
        Assert.Equal(3, classes); // 600 students of 280 per class
        Assert.Equal(600 * PerfSeeder.CoursesPerSchool, await db.Set<CourseEnrollment>().CountAsync(e => e.OrganizationId == org));
        Assert.Equal(600 * PerfSeeder.CoursesPerSchool * PerfSeeder.ItemsPerGradebook, await db.Set<GradeEntry>().CountAsync(e => e.OrganizationId == org));
        Assert.Equal(4, await db.Users.CountAsync(u => u.Email!.EndsWith("@" + PerfSeeder.Domain)));

        // a second run adds nothing
        var before = (await db.Set<StudentProfile>().CountAsync(), await db.Set<GradeEntry>().CountAsync(), await db.Users.CountAsync());
        await Seeder(cs, 600).SeedAsync();
        await using var again = Open(cs);
        Assert.Equal(before, (await again.Set<StudentProfile>().CountAsync(), await again.Set<GradeEntry>().CountAsync(), await again.Users.CountAsync()));
    }

    [Fact]
    public async Task Perf_rows_are_invisible_to_another_client()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(cs, 300).SeedAsync();

        await using var other = LmsDbContextFactory.Create(cs, TestCurrentContext.For(DevSeedData.SchoolGroupId, "S"));
        await using var admin = Open(cs);

        Assert.Equal(0, await other.Set<StudentProfile>().CountAsync(s => s.OrganizationId == PerfSeeder.OrganizationId));
        Assert.Equal(300, await admin.Set<StudentProfile>().CountAsync(s => s.OrganizationId == PerfSeeder.OrganizationId));
    }

    [Fact]
    public async Task Nothing_is_seeded_without_the_option_or_outside_development()
    {
        var off = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(off, 0).SeedAsync();
        var production = await factory.CreateIsolatedDatabaseAsync();
        var ran = await Seeder(production, 300, "Production").SeedAsync();

        await using var a = Open(off);
        await using var b = Open(production);
        Assert.False(await a.Organizations.AnyAsync(o => o.Id == PerfSeeder.OrganizationId));
        Assert.False(ran);
        Assert.False(await b.Organizations.AnyAsync(o => o.Id == PerfSeeder.OrganizationId));
    }

    /// <summary>The full-size run, only on request: <c>LMS_PERF_SEED_STUDENTS=50000 dotnet test --filter Full_size</c>. It reports how long the seed takes.</summary>
    [Fact]
    public async Task Full_size_seed_reports_its_time()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("LMS_PERF_SEED_STUDENTS"), out var students))
        {
            return;
        }

        var cs = await factory.CreateIsolatedDatabaseAsync();
        var watch = Stopwatch.StartNew();
        await Seeder(cs, students).SeedAsync();
        output.WriteLine($"Seeded {students} students in {watch.Elapsed.TotalSeconds:F1} s");
        await using var db = Open(cs);
        Assert.Equal(students, await db.Set<StudentProfile>().CountAsync(s => s.OrganizationId == PerfSeeder.OrganizationId));
        watch.Restart();
        await Seeder(cs, students).SeedAsync();
        output.WriteLine($"Second run (idempotent) took {watch.Elapsed.TotalSeconds:F1} s");
    }
}
