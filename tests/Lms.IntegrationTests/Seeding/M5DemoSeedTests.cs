using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Excuses;
using Lms.Domain.Grading;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Seeding;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Lms.IntegrationTests.Seeding;

/// <summary>The M5 demo data of the dev seeder: present once, idempotent, Development only, and shaped for QA's demos.</summary>
[Collection(ApiCollection.Name)]
public sealed class M5DemoSeedTests(LmsApiFactory factory)
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Lms.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static LmsDbContext Open(string cs) => LmsDbContextFactory.Create(cs, TestCurrentContext.PlatformAdmin());

    private static DevSeeder Seeder(string cs, bool withUsers = true, string environment = "Development") =>
        new(cs, new FakeEnvironment(environment), withUsers ? new SeedOptions("admin@lms.test", "DevPass123") : new SeedOptions(null, null));

    [Fact]
    public async Task Seeding_twice_creates_the_demo_once()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(cs).SeedAsync();

        async Task<int[]> CountsAsync()
        {
            await using var db = Open(cs);
            return
            [
                await db.Set<Gradebook>().CountAsync(), await db.Set<GradeItem>().CountAsync(), await db.Set<GradeEntry>().CountAsync(),
                await db.Set<TimetableSlot>().CountAsync(), await db.Set<AttendanceSession>().CountAsync(), await db.Set<AttendanceRecord>().CountAsync(),
                await db.Set<AbsenceExcuse>().CountAsync(), await db.Set<AttendanceAlert>().CountAsync(), await db.Set<GradingScale>().CountAsync(),
            ];
        }

        var first = await CountsAsync();
        await Seeder(cs).SeedAsync();

        Assert.Equal(first, await CountsAsync());
        Assert.Equal(2, first[0]); // a school gradebook and a university gradebook
        Assert.All(first, n => Assert.True(n > 0));
    }

    [Fact]
    public async Task The_school_gradebook_has_two_terms_the_five_band_scale_and_every_publish_state()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(cs).SeedAsync();

        await using var db = Open(cs);
        var cc = await db.Set<ClassCourse>().SingleAsync(c => db.Set<Class>().Any(k => k.Id == c.ClassId && k.CodeKey == M5DemoSeeder.SchoolClassCode));
        var year = await db.Set<Lms.Domain.AcademicYears.Term>().Where(t => db.Set<Class>().Any(k => k.Id == cc.ClassId && k.AcademicYearId == t.AcademicYearId)).ToListAsync();
        var book = await db.Set<Gradebook>().SingleAsync(g => g.ClassCourseId == cc.Id);
        var scale = await db.Set<GradingScale>().SingleAsync(s => s.Id == book.GradingScaleId);
        var bands = await db.Set<GradingScaleBand>().CountAsync(b => b.ScaleId == scale.Id);
        var states = await db.Set<GradeItem>().Where(i => i.GradebookId == book.Id).Select(i => i.PublishState).Distinct().ToListAsync();

        Assert.Equal(2, year.Count);
        Assert.Equal(SaudiPresetSeed.SchoolKey, scale.SeedKey);
        Assert.False(scale.Provisional);
        Assert.Equal(5, bands);
        Assert.Contains(GradeItem.PublishedState, states);
        Assert.Contains(GradeItem.HiddenState, states);
        Assert.Contains(GradeItem.ScheduledState, states);
    }

    [Fact]
    public async Task The_university_gradebook_uses_the_five_point_gpa_scale()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(cs).SeedAsync();

        await using var db = Open(cs);
        var cc = await db.Set<ClassCourse>().SingleAsync(c => db.Set<Class>().Any(k => k.Id == c.ClassId && k.CodeKey == M5DemoSeeder.UniversityClassCode));
        var book = await db.Set<Gradebook>().SingleAsync(g => g.ClassCourseId == cc.Id);
        var scale = await db.Set<GradingScale>().SingleAsync(s => s.Id == book.GradingScaleId);

        Assert.Equal(SaudiPresetSeed.University5Key, scale.SeedKey);
        Assert.Equal("Gpa", scale.Kind);
        Assert.Equal(5m, await db.Set<GradingScaleBand>().Where(b => b.ScaleId == scale.Id).MaxAsync(b => b.Points) ?? 0m);
    }

    [Fact]
    public async Task The_timetable_is_published_and_conflict_free_and_attendance_has_an_excuse_and_an_alert()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(cs).SeedAsync();

        await using var db = Open(cs);
        var slots = await db.Set<TimetableSlot>().ToListAsync();
        Assert.All(slots, s => Assert.True(s.IsPublished));
        Assert.Equal(slots.Count, slots.Select(s => (s.Day, s.PeriodId)).Distinct().Count()); // no day and period used twice
        var excuse = await db.Set<AbsenceExcuse>().SingleAsync();
        Assert.Equal(ExcuseStatus.Submitted, excuse.Status);
        var alert = await db.Set<AttendanceAlert>().SingleAsync();
        Assert.True(alert.Score >= alert.Threshold);
        var absent = await db.Set<AttendanceRecord>().CountAsync(r => r.StudentProfileId == alert.StudentProfileId && r.Status == AttendanceStatuses.Absent);
        Assert.Equal(alert.Absences, absent);
        Assert.Contains(await db.Set<AttendanceSession>().Select(s => s.State).ToListAsync(), s => s == AttendanceSession.OpenState);
    }

    [Fact]
    public async Task Without_accounts_the_gradebooks_are_seeded_and_the_timetable_is_not()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await Seeder(cs, withUsers: false).SeedAsync();

        await using var db = Open(cs);
        Assert.Equal(2, await db.Set<Gradebook>().CountAsync());
        Assert.Equal(0, await db.Set<TimetableSlot>().CountAsync());
    }

    [Fact]
    public async Task Outside_development_nothing_is_seeded()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();

        Assert.False(await Seeder(cs, environment: "Production").SeedAsync());

        await using var db = Open(cs);
        Assert.Equal(0, await db.Set<Gradebook>().CountAsync());
    }
}
