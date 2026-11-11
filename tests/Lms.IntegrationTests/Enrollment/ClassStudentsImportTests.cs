using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Jobs;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Imports;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Imports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.IntegrationTests.Enrollment;

/// <summary>M3 Task 26 (#234, FR-ENR-03, X-06): class placement Excel import: all or nothing, through the enrollment engine.</summary>
[Collection(ApiCollection.Name)]
public sealed class ClassStudentsImportTests : IDisposable
{
    private const string Kind = ImportKinds.ClassStudents;
    private static readonly string[] EnHeaders = ["Student code", "Class code", "Effective date"];
    private static readonly string[] ArHeaders = ["كود الطالب", "رمز الفصل", "تاريخ البدء"];

    private readonly LmsApiFactory _factory;
    private readonly WebApplicationFactory<Program> _host;

    public ClassStudentsImportTests(LmsApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jobs:ServerEnabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBackgroundJobs>();
                services.AddScoped<IBackgroundJobs, RecordingBackgroundJobs>();
            });
        });
    }

    public void Dispose() => _host.Dispose();

    private sealed record World(AccessTree Tree, RoleAssignment Admin, AcademicYear Year, List<Class> Classes, List<Course> Courses);

    /// <summary>An active year at the root, <paramref name="classCount"/> classes at School1, each with every course (mandatory).</summary>
    private async Task<World> SetUpAsync(string name, int classCount = 1, int courseCount = 1, int capacity = 40)
    {
        var tree = await AccessTestData.SeedAsync(_factory, name);
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await AcademicTestKit.AddYearAsync(_factory, tree.Root);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == year.Id)).Activate();
            await db.SaveChangesAsync();
        }

        var courses = new List<Course>();
        for (var i = 0; i < courseCount; i++)
        {
            courses.Add(await AcademicTestKit.AddCourseAsync(_factory, tree.Root));
        }

        var classes = new List<Class>();
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            for (var i = 0; i < classCount; i++)
            {
                var cls = new Class(tree.School1, year.Id, $"K{i}", "فصل", "Class " + i, capacity);
                db.Add(cls);
                db.AddRange(courses.Select(c => new ClassCourse(cls, c, false)));
                classes.Add(cls);
            }

            await db.SaveChangesAsync();
        }

        return new World(tree, admin, year, classes, courses);
    }

    private async Task<List<StudentProfile>> StudentsAsync(OrgUnit home, int count)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var students = Enumerable.Range(0, count).Select(i => new StudentProfile(
            home, PeopleTestKit.NewCode(), "طالب", "Student " + i, new DateOnly(2015, 5, 5), "female", "muslim", "EG")).ToList();
        db.AddRange(students);
        await db.SaveChangesAsync();
        return students;
    }

    private Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        return _host.CreateClient().SendAsync(request);
    }

    private async Task RunQueuedJobsAsync()
    {
        while (RecordingBackgroundJobs.Queue.TryDequeue(out var queued))
        {
            using var scope = _host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HttpCurrentContext>();
            var snapshot = queued.Context;
            context.SetUser(snapshot.UserId ?? Guid.Empty, snapshot.IsPlatformAdmin);
            if (snapshot is { AssignmentId: { } assignmentId, OrganizationId: { } organizationId, ScopePath: { } path })
            {
                context.SetAssignment(assignmentId, organizationId, path);
            }

            var job = ActivatorUtilities.CreateInstance(scope.ServiceProvider, queued.Type);
            await (Task)queued.Job.DynamicInvoke(job)!;
        }
    }

    private async Task<Guid> UploadAsync(RoleAssignment caller, Guid unitId, string[] headers, params string?[][] rows)
    {
        var form = new MultipartFormDataContent { { new StringContent(unitId.ToString()), "orgUnitId" } };
        var part = new ByteArrayContent(XlsxBuilder.Create(headers, rows));
        part.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(part, "file", "placement.xlsx");
        var response = await SendAsync(caller, HttpMethod.Post, Routes.ImportsOfKind(Kind), form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await RunQueuedJobsAsync();
        return id;
    }

    private async Task CommitAsync(RoleAssignment caller, Guid id)
    {
        var commit = await SendAsync(caller, HttpMethod.Post, Routes.ImportCommit(Kind, id));
        Assert.Equal(HttpStatusCode.Accepted, commit.StatusCode);
        await RunQueuedJobsAsync();
    }

    private async Task<string> StatusAsync(Guid id)
    {
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<Lms.Domain.Imports.ImportBatch>().SingleAsync(b => b.Id == id)).Status.ToString();
    }

    private async Task<List<(int Row, string? Column, string Code)>> ErrorsAsync(RoleAssignment caller, Guid id)
    {
        var rows = await SendAsync(caller, HttpMethod.Get, Routes.ImportRows(Kind, id) + "?errorsOnly=true&pageSize=200");
        var items = JsonDocument.Parse(await rows.Content.ReadAsStringAsync()).RootElement.GetProperty("items");
        return items.EnumerateArray().SelectMany(r => r.GetProperty("errors").EnumerateArray().Select(e => (
            r.GetProperty("rowNumber").GetInt32(),
            e.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
            e.GetProperty("code").GetString()!))).ToList();
    }

    private async Task<(int Placements, int Enrollments)> CountsAsync(IEnumerable<Guid> studentIds)
    {
        var ids = studentIds.ToList();
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<ClassStudent>().CountAsync(c => ids.Contains(c.StudentId)),
            await db.Set<CourseEnrollment>().CountAsync(e => ids.Contains(e.StudentId)));
    }

    [Fact]
    public async Task Import_places_300_students_into_10_classes_and_auto_enrolls()
    {
        var w = await SetUpAsync("CI1", classCount: 10, courseCount: 2);
        var students = await StudentsAsync(w.Tree.School1, 300);
        var rows = students.Select((s, i) => new string?[] { s.StudentCode, $"K{i % 10}", null }).ToArray();

        var id = await UploadAsync(w.Admin, w.Tree.Root.Id, EnHeaders, rows);
        Assert.Equal("Valid", await StatusAsync(id));
        Assert.Equal((0, 0), await CountsAsync(students.Select(s => s.Id))); // nothing before the commit

        await CommitAsync(w.Admin, id);

        Assert.Equal("Committed", await StatusAsync(id));
        Assert.Equal((300, 600), await CountsAsync(students.Select(s => s.Id))); // 2 mandatory courses each
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var perClass = await db.Set<ClassStudent>().Where(c => c.AcademicYearId == w.Year.Id).GroupBy(c => c.ClassId).Select(g => g.Count()).ToListAsync();
        Assert.Equal(10, perClass.Count);
        Assert.All(perClass, n => Assert.Equal(30, n));
    }

    [Fact]
    public async Task Arabic_headers_and_a_start_date_are_accepted_and_the_date_is_used()
    {
        var w = await SetUpAsync("CI2");
        var student = (await StudentsAsync(w.Tree.School1, 1)).Single();

        var id = await UploadAsync(w.Admin, w.Tree.Root.Id, ArHeaders, [student.StudentCode, "k0", "2026-09-15"]);
        Assert.Equal("Valid", await StatusAsync(id)); // class codes are matched without regard to case
        await CommitAsync(w.Admin, id);

        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(new DateOnly(2026, 9, 15), (await db.Set<ClassStudent>().SingleAsync(c => c.StudentId == student.Id)).From);
    }

    [Fact]
    public async Task Student_of_sibling_school_is_unknown_student()
    {
        var w = await SetUpAsync("CI3");
        var inSchool1 = (await StudentsAsync(w.Tree.School1, 1)).Single();
        var inSchool2 = (await StudentsAsync(w.Tree.School2, 1)).Single();
        var school1Admin = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var id = await UploadAsync(school1Admin, w.Tree.School1.Id, EnHeaders,
            [inSchool1.StudentCode, "K0", null], [inSchool2.StudentCode, "K0", null], ["NO-SUCH-CODE", "K0", null], [inSchool1.StudentCode.ToLowerInvariant() + "x", "K9", null]);

        Assert.Equal("Invalid", await StatusAsync(id));
        var errors = await ErrorsAsync(school1Admin, id);
        Assert.Contains((3, "StudentCode", "unknown_student"), errors); // exists, but in School 2
        Assert.Contains((4, "StudentCode", "unknown_student"), errors);
        Assert.Contains((5, "ClassCode", "unknown_class"), errors);
        Assert.DoesNotContain(errors, e => e.Row == 2);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(school1Admin, HttpMethod.Post, Routes.ImportCommit(Kind, id))).StatusCode);
        Assert.Equal((0, 0), await CountsAsync([inSchool1.Id, inSchool2.Id]));
    }

    [Fact]
    public async Task Already_placed_student_blocks_file()
    {
        var w = await SetUpAsync("CI4", classCount: 2);
        var students = await StudentsAsync(w.Tree.School1, 3);
        await AcademicTestKit.PlaceAsync(_factory, w.Classes[1], students[1]);
        // The same student twice in one file is a duplicate as well.
        var id = await UploadAsync(w.Admin, w.Tree.Root.Id, EnHeaders,
            [students[0].StudentCode, "K0", null], [students[1].StudentCode, "K0", null], [students[2].StudentCode, "K0", null],
            [students[2].StudentCode, "K1", null]);

        var errors = await ErrorsAsync(w.Admin, id);

        Assert.Equal("Invalid", await StatusAsync(id));
        Assert.Contains((3, "StudentCode", "already_placed"), errors);
        Assert.Contains(errors, e => e.Code == "duplicate_in_file" && e.Column == "StudentCode");
        Assert.Equal(0, (await CountsAsync([students[0].Id])).Placements); // the valid row 2 was not placed either
    }

    [Fact]
    public async Task Full_class_in_block_mode_blocks_file_and_warn_mode_accepts_the_overflow()
    {
        var w = await SetUpAsync("CI5", capacity: 2);
        var students = await StudentsAsync(w.Tree.School1, 3);
        var rows = students.Select(s => new string?[] { s.StudentCode, "K0", null }).ToArray();

        // Warn (the default): three students into two seats are accepted.
        var warn = await UploadAsync(w.Admin, w.Tree.Root.Id, EnHeaders, rows);
        Assert.Equal("Valid", await StatusAsync(warn));

        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitSetting(w.Tree.Org.Id, w.Tree.Root.Id, w.Tree.Root.Path, SettingKeys.ClassesCapacityMode.Name, "\"Block\"", false));
            await db.SaveChangesAsync();
        }

        var block = await UploadAsync(w.Admin, w.Tree.Root.Id, EnHeaders, rows);

        Assert.Equal("Invalid", await StatusAsync(block));
        Assert.Equal([(4, "ClassCode", "class_full")], await ErrorsAsync(w.Admin, block)); // only the row beyond the capacity
        Assert.Equal(0, (await CountsAsync(students.Select(s => s.Id))).Placements);
    }

    [Fact]
    public async Task Failure_on_last_row_leaves_no_placement_or_enrollment()
    {
        var w = await SetUpAsync("CI6", classCount: 2, courseCount: 2);
        var students = await StudentsAsync(w.Tree.School1, 5);
        var rows = students.Select((s, i) => new string?[] { s.StudentCode, i < 3 ? "K0" : "K1", null }).ToArray();
        var id = await UploadAsync(w.Admin, w.Tree.Root.Id, EnHeaders, rows);
        Assert.Equal("Valid", await StatusAsync(id));

        // Between the preview and the commit someone places the LAST student elsewhere: the engine refuses that row.
        await AcademicTestKit.PlaceAsync(_factory, w.Classes[0], students[4]);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // the AcademicTestKit placement counts as the one active placement of that year
            Assert.Equal(1, await db.Set<ClassStudent>().CountAsync(c => c.StudentId == students[4].Id));
        }

        var commit = await SendAsync(w.Admin, HttpMethod.Post, Routes.ImportCommit(Kind, id));
        if (commit.StatusCode == HttpStatusCode.Accepted)
        {
            await RunQueuedJobsAsync();
        }

        Assert.NotEqual("Committed", await StatusAsync(id));
        // Nothing of the file stays: the four other students have neither a placement nor an enrollment.
        Assert.Equal((0, 0), await CountsAsync(students.Take(4).Select(s => s.Id)));
        Assert.Equal(1, (await CountsAsync([students[4].Id])).Placements); // only the one made by hand
        Assert.Equal(0, (await CountsAsync([students[4].Id])).Enrollments);
    }

    [Fact]
    public async Task Manager_without_Enrollment_Import_gets_403()
    {
        var w = await SetUpAsync("CI7");
        var student = (await StudentsAsync(w.Tree.School1, 1)).Single();
        var manager = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var form = new MultipartFormDataContent { { new StringContent(w.Tree.Root.Id.ToString()), "orgUnitId" } };
        var part = new ByteArrayContent(XlsxBuilder.Create(EnHeaders, [student.StudentCode, "K0", null]));
        part.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(part, "file", "placement.xlsx");

        var response = await SendAsync(manager, HttpMethod.Post, Routes.ImportsOfKind(Kind), form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(manager, HttpMethod.Get, Routes.ImportColumns(Kind))).StatusCode);
    }

    [Fact]
    public async Task Columns_endpoint_describes_the_template()
    {
        var w = await SetUpAsync("CI8");

        var response = await SendAsync(w.Admin, HttpMethod.Get, Routes.ImportColumns(Kind));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var keys = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray()
            .Select(c => c.GetProperty("key").GetString()).ToList();
        Assert.Equal(["StudentCode", "ClassCode", "EffectiveOn"], keys);
    }
}
