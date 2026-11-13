using System.Net;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Domain.Imports;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Imports;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Imports;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Courses;

/// <summary>Excel import of courses (M3 Task 6, X-06): all or nothing with an error report.</summary>
[Collection(ApiCollection.Name)]
public sealed class CourseImportTests(LmsApiFactory factory) : IDisposable
{
    private static readonly string[] Headers =
        ["Code", "Name (Arabic)", "Name (English)", "Description (Arabic)", "Description (English)", "Language", "Weekly periods", "Owner unit code"];

    private readonly ImportTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.OrgUnits.SingleAsync(u => u.Id == tree.School1.Id)).SetAcademicFlags(false, null, true);
        await db.SaveChangesAsync();
        return (tree, admin);
    }

    private static string?[] Row(string code, string owner, string periods = "4", string language = "en") =>
        [code, "مادة " + code, "Course " + code, null, null, language, periods, owner];

    private async Task<Guid> UploadAsync(RoleAssignment admin, AccessTree tree, params string?[][] rows)
    {
        var response = await _host.UploadAsync(
            admin, tree.Root.Id, XlsxBuilder.Create(Headers, rows), "courses.xlsx", "en", ImportKinds.Courses);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await _host.RunQueuedJobsAsync();
        return id;
    }

    private async Task<int> CountAsync(string prefix)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Course>().CountAsync(c => c.Code.StartsWith(prefix));
    }

    [Fact]
    public async Task Import_of_40_courses_all_or_nothing_with_error_report()
    {
        var (tree, admin) = await SetUpAsync("CI1");
        var prefix = "IMP" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var good = Enumerable.Range(1, 40).Select(i => Row($"{prefix}-{i}", tree.School1.Code)).ToArray();

        var id = await UploadAsync(admin, tree, good);
        Assert.Equal("Valid", (await _host.BatchAsync(factory, id)).Status);
        Assert.Equal(0, await CountAsync(prefix)); // nothing before the commit
        await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(ImportKinds.Courses, id), admin);
        await _host.RunQueuedJobsAsync();

        var state = await _host.BatchAsync(factory, id);
        Assert.Equal("Committed", state.Status);
        Assert.Equal(40, state.CreatedCount);
        Assert.Equal(40, await CountAsync(prefix));

        // a file with one bad row (code taken by the first import) creates nothing and has an error report
        var prefix2 = "IMQ" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var mixed = Enumerable.Range(1, 39).Select(i => Row($"{prefix2}-{i}", tree.School1.Code))
            .Append(Row($"{prefix}-1", tree.School1.Code)).ToArray();
        var bad = await UploadAsync(admin, tree, mixed);
        var badState = await _host.BatchAsync(factory, bad);
        Assert.Equal("Invalid", badState.Status);
        Assert.Equal(1, badState.ErrorCount);
        Assert.NotNull(badState.ErrorReportFileId);
        var commit = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(ImportKinds.Courses, bad), admin);
        Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);
        Assert.Equal(0, await CountAsync(prefix2));
    }

    [Fact]
    public async Task Row_errors_cover_owner_rule_unknown_unit_duplicates_and_values()
    {
        var (tree, admin) = await SetUpAsync("CI2");
        var a = "ER" + Guid.NewGuid().ToString("N")[..6];
        var id = await UploadAsync(admin, tree,
            Row(a, tree.School2.Code), // School2 is not a course-management level
            Row(a + "x", "NOPE"),
            Row(a, tree.School1.Code), // same code twice in the file
            Row(a + "y", tree.School1.Code, "41", "english"),
            Row("bad code!", tree.School1.Code));

        Assert.Equal("Invalid", (await _host.BatchAsync(factory, id)).Status);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var codes = (await db.Set<ImportRow>().Where(r => r.BatchId == id).Select(r => r.ErrorsJson).ToListAsync())
            .Where(j => j is not null)
            .SelectMany(j => JsonDocument.Parse(j!).RootElement.EnumerateArray().Select(e => e.GetProperty("Code").GetString()))
            .ToHashSet();
        Assert.Superset(
            new HashSet<string?> { "invalid_owner_unit", "unknown_unit", "duplicate_in_file", "invalid_weekly_periods", "invalid_language", "invalid_code" },
            codes);
    }

    [Fact]
    public async Task Code_taken_in_sibling_school_is_a_row_error()
    {
        var (tree, admin) = await SetUpAsync("CI3");
        var code = "TK" + Guid.NewGuid().ToString("N")[..6];
        await AcademicTestKit.AddCourseAsync(factory, tree.School2, code);

        var id = await UploadAsync(admin, tree, Row(code.ToLowerInvariant(), tree.School1.Code));

        Assert.Equal("Invalid", (await _host.BatchAsync(factory, id)).Status);
    }

    [Fact]
    public async Task CreateCourses_policy_blocks_import()
    {
        var (tree, admin) = await SetUpAsync("CI4");
        using var units = new OrgUnitTestHost(factory);
        await units.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateCourses);

        var response = await _host.UploadAsync(
            admin, tree.Root.Id, XlsxBuilder.Create(Headers, Row("PB1", tree.School1.Code)), "c.xlsx", "en", ImportKinds.Courses);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("BlockedByPolicy", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Teacher_cannot_import_courses()
    {
        var (tree, _) = await SetUpAsync("CI5");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        var response = await _host.UploadAsync(
            teacher, tree.School1.Id, XlsxBuilder.Create(Headers, Row("X1", tree.School1.Code)), "c.xlsx", "en", ImportKinds.Courses);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Import_can_allocate_courses_to_listed_units_and_rejects_units_outside_the_owner_subtree()
    {
        var (tree, admin) = await SetUpAsync("CI6");
        string[] headers = [.. Headers, "Allocate to unit codes"];
        var ok = "AO" + Guid.NewGuid().ToString("N")[..6];
        var bad = "AB" + Guid.NewGuid().ToString("N")[..6];

        async Task<Guid> UploadWithAllocationAsync(params string?[][] rows)
        {
            var response = await _host.UploadAsync(
                admin, tree.Root.Id, XlsxBuilder.Create(headers, rows), "courses.xlsx", "en", ImportKinds.Courses);
            var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
            await _host.RunQueuedJobsAsync();
            return id;
        }

        // owned at the root, allocated at School 1 and School 2
        var good = await UploadWithAllocationAsync([.. Row(ok, tree.Root.Code), tree.School1.Code + ";" + tree.School2.Code]);
        Assert.Equal("Valid", (await _host.BatchAsync(factory, good)).Status);
        await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(ImportKinds.Courses, good), admin);
        await _host.RunQueuedJobsAsync();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var course = await db.Set<Course>().SingleAsync(c => c.Code == ok);
            Assert.Equal(
                new[] { tree.School1.Id, tree.School2.Id }.Order(),
                (await db.Set<CourseAllocation>().Where(a => a.CourseId == course.Id).Select(a => a.OrgUnitId).ToListAsync()).Order());
        }

        // owned at School 1 it cannot be allocated at School 2; an unknown code is an error too
        var invalid = await UploadWithAllocationAsync(
            [.. Row(bad, tree.School1.Code), tree.School2.Code],
            [.. Row(bad + "2", tree.School1.Code), "NOPE"]);
        Assert.Equal("Invalid", (await _host.BatchAsync(factory, invalid)).Status);
        Assert.Equal(0, await CountAsync(bad));
    }
}
