using System.Diagnostics;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Enrollment;

[Collection(ApiCollection.Name)]
public sealed class EnrollmentPerformanceTests(LmsApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Placing_500_students_into_class_with_12_courses_under_5_seconds()
    {
        // 12 mandatory courses (2 from the world + 10 more) and a gender restriction on one of them.
        var w = await EnrollmentWorld.CreateAsync(factory, "EP1", mandatoryExtra: 10);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).Update("BIG", "فصل", "Big class", 600, null);
            db.Add(new CourseRestriction(w.B, CourseRestrictionEvaluator.Gender, ["female"]));
            var students = Enumerable.Range(0, 500).Select(i =>
            {
                var s = new StudentProfile(w.Tree.Root, PeopleTestKit.NewCode(), "طالب", "Student " + i, new DateOnly(2015, 5, 5), i % 2 == 0 ? "female" : "male", "muslim", "EG");
                return s;
            }).ToList();
            db.AddRange(students);
            await db.SaveChangesAsync();
        }

        List<StudentProfile> batch;
        using var scope = new EngineScope(factory.Services);
        var ids = await scope.Db.Set<StudentProfile>().Where(s => s.OrgUnitId == w.Tree.Root.Id && s.NameEn.StartsWith("Student ")).Select(s => s.Id).ToListAsync();
        batch = await scope.Db.Set<StudentProfile>().Where(s => ids.Contains(s.Id)).ToListAsync();
        var cls = await scope.Db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id);
        Assert.True(batch.Count >= 500);
        batch = batch.Take(500).ToList();

        var timer = Stopwatch.StartNew();
        var results = await scope.Engine.PlaceManyAsync(batch, cls, DateOnly.FromDateTime(DateTime.UtcNow), false, default);
        timer.Stop();

        output.WriteLine($"placed 500 students x 12 courses in {timer.Elapsed.TotalSeconds:F2} s");
        Assert.Equal(500, results.Count);
        var females = batch.Count(s => s.Gender == "female");
        Assert.Equal(12 * 500 - (500 - females), results.Sum(r => r.EnrollmentsCreated)); // males skip the restricted course
        // X-09: 5 s. Shared CI runners are slower: LMS_PERF_BUDGET_FACTOR (default 1, 3 in ci.yml) scales the budget.
        var factor = double.TryParse(Environment.GetEnvironmentVariable("LMS_PERF_BUDGET_FACTOR"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 ? parsed : 1;
        var budget = TimeSpan.FromSeconds(5 * factor);
        Assert.True(timer.Elapsed < budget, $"placing 500 students took {timer.Elapsed.TotalSeconds:F1} s (budget {budget.TotalSeconds:F0} s)");
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(500, await read.Set<ClassStudent>().CountAsync(c => c.ClassId == w.Class.Id));
    }
}
