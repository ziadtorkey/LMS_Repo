using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Branches;
using Lms.Infrastructure.Courses;
using Lms.Infrastructure.Organizations;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Branches;

/// <summary>Copying a branch (and applying a template) with its courses and allocations (M3 Task 10, FR-ORG-06).</summary>
[Collection(ApiCollection.Name)]
public sealed class CopyBranchCoursesTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Root → School1 → X (owns courses) → Y; School2 is the copy target.</summary>
    private sealed record Fixture(AccessTree Tree, OrgUnit X, OrgUnit Y);

    private async Task<Fixture> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var level2 = new LevelType(tree.Org.Id, "ثانٍ", "Second", 2, true);
        var level3 = new LevelType(tree.Org.Id, "ثالث", "Third", 3, true);
        var x = OrgUnit.CreateChild(tree.School1, level2.Id, "س", "X", "X");
        var y = OrgUnit.CreateChild(x, level3.Id, "ص", "Y", "Y");
        x.SetAcademicFlags(false, null, true);
        db.AddRange(level2, level3, x, y);
        await db.SaveChangesAsync();
        return new Fixture(tree, x, y);
    }

    private async Task<(Course Owned, Course Root)> AddCoursesAsync(Fixture f)
    {
        var owned = await AcademicTestKit.AddCourseAsync(factory, f.X, "OWN" + Guid.NewGuid().ToString("N")[..6]);
        var root = await AcademicTestKit.AddCourseAsync(factory, f.Tree.Root, "ROOT" + Guid.NewGuid().ToString("N")[..6]);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new CourseRestriction(owned, CourseRestrictionTypes.Gender, ["Female"]));
        await db.SaveChangesAsync();
        await AcademicTestKit.AllocateAsync(factory, owned, f.Y);
        await AcademicTestKit.AllocateAsync(factory, root, f.Y);
        return (owned, root);
    }

    private async Task<(BranchSnapshot Snapshot, MaterializeResult? Result)> CopyAsync(
        Fixture f, string suffix, bool materialize = true, Func<BranchSnapshot, BranchSnapshot>? edit = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var context = TestCurrentContext.PlatformAdmin();
        var snapshot = await new BranchSnapshotter(db).CaptureAsync(f.X, false, false, default, includeCourses: true);
        snapshot = edit?.Invoke(snapshot) ?? snapshot;
        if (!materialize)
        {
            return (snapshot, null);
        }

        var materializer = new BranchMaterializer(db, new OrgTree(db), new CourseCatalog(db, context));
        var result = await materializer.MaterializeAsync(snapshot, f.Tree.School2, n => n, c => c + suffix, default);
        return (snapshot, result);
    }

    [Fact]
    public async Task Copy_with_courses_creates_owned_courses_and_allocations()
    {
        var f = await SetUpAsync("CB1");
        var (owned, root) = await AddCoursesAsync(f);

        var (_, result) = await CopyAsync(f, "-C");

        Assert.Equal(1, result!.CoursesCreated);
        Assert.Empty(result.SkippedAllocations!);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var copy = await db.Set<Course>().SingleAsync(c => c.Code == owned.Code + "-C");
        Assert.Equal(result.NewRoot.Id, copy.OrgUnitId);
        var restriction = await db.Set<CourseRestriction>().SingleAsync(r => r.CourseId == copy.Id);
        Assert.Equal(["Female"], restriction.Values);
        var newY = await db.OrgUnits.SingleAsync(u => u.ParentId == result.NewRoot.Id);
        var allocated = await db.Set<CourseAllocation>().Where(a => a.OrgUnitId == newY.Id).Select(a => a.CourseId).ToListAsync();
        Assert.Equivalent(new[] { copy.Id, root.Id }, allocated);
        Assert.NotEqual(owned.Id, copy.Id);
    }

    [Fact]
    public async Task Allocation_of_outside_course_not_available_at_target_is_skipped_and_reported()
    {
        var f = await SetUpAsync("CB2");
        await AddCoursesAsync(f);
        // owned by School1 (a course-management level): not available below School2
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.OrgUnits.SingleAsync(u => u.Id == f.Tree.School1.Id)).SetAcademicFlags(false, null, true);
            await db.SaveChangesAsync();
        }

        var school = await AcademicTestKit.AddCourseAsync(factory, f.Tree.School1, "SCH" + Guid.NewGuid().ToString("N")[..6]);
        await AcademicTestKit.AllocateAsync(factory, school, f.Y);

        var (_, result) = await CopyAsync(f, "-S");

        Assert.Equal([school.Code], result!.SkippedAllocations);
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await check.Set<CourseAllocation>().Where(a => a.CourseId == school.Id && a.OrgUnitPath.StartsWith(result.NewRoot.Path)).ToListAsync());
    }

    [Fact]
    public async Task Course_code_conflict_creates_nothing()
    {
        var f = await SetUpAsync("CB3");
        var (owned, _) = await AddCoursesAsync(f);
        await AcademicTestKit.AddCourseAsync(factory, f.Tree.School2, owned.Code + "-C"); // outside the branch, same client

        var thrown = await Assert.ThrowsAsync<BranchCourseCodeConflictException>(() => CopyAsync(f, "-C"));

        Assert.Equal([(owned.Code + "-C").ToUpperInvariant()], thrown.Codes);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await db.OrgUnits.Where(u => u.OrganizationId == f.Tree.Org.Id && u.Code.EndsWith("-C")).ToListAsync());
        Assert.Equal(1, await db.Set<Course>().CountAsync(c => c.Code == owned.Code + "-C"));
    }

    [Fact]
    public async Task Template_with_courses_applies_with_placeholders()
    {
        var f = await SetUpAsync("CB4");
        var (owned, _) = await AddCoursesAsync(f);

        // Stored as JSON like a template, placeholders substituted before materializing (as the template endpoint does).
        var (snapshot, _) = await CopyAsync(f, "", materialize: false, edit: s => new BranchSnapshot(
            [.. s.Nodes.Select(n => n with
            {
                OwnedCourses = n.OwnedCourses?.Select(c => c with { Code = c.Code.Replace(owned.Code, "{Year}-MATH") }).ToList(),
            })]));
        var stored = JsonSerializer.Deserialize<BranchSnapshot>(JsonSerializer.Serialize(snapshot, Json), Json)!;
        var applied = new BranchSnapshot([.. stored.Nodes.Select(n => n with
        {
            OwnedCourses = n.OwnedCourses?.Select(c => c with { Code = TemplatePlaceholders.Apply(c.Code, new Dictionary<string, string> { ["Year"] = owned.Code }) }).ToList(),
        })]);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var materializer = new BranchMaterializer(db, new OrgTree(db), new CourseCatalog(db, TestCurrentContext.PlatformAdmin()));

        var result = await materializer.MaterializeAsync(applied, f.Tree.School2, n => n, c => c + "-T", default);

        Assert.Equal(1, result.CoursesCreated);
        Assert.True(await db.Set<Course>().AnyAsync(c => c.Code == owned.Code + "-MATH-T"));
    }

    [Fact]
    public async Task Snapshot_without_course_fields_still_materializes()
    {
        var f = await SetUpAsync("CB5");
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var plain = await new BranchSnapshotter(db).CaptureAsync(f.X, false, false, default);
        var old = JsonSerializer.Deserialize<BranchSnapshot>(
            "{\"nodes\":" + JsonSerializer.Serialize(plain.Nodes.Select(n => new
            {
                n.Key, n.ParentKey, n.LevelTypeId, n.NameAr, n.NameEn, n.Code, n.IsLevelTransfer, n.NextTransferKey,
                n.IsCourseManagementLevel, n.PolicyKeys, n.Settings,
            }), Json) + "}", Json)!;

        var result = await new BranchMaterializer(db, new OrgTree(db), new CourseCatalog(db, TestCurrentContext.PlatformAdmin()))
            .MaterializeAsync(old, f.Tree.School2, n => n, c => c + "-O", default);

        Assert.Equal(2, result.UnitsCreated);
        Assert.Equal(0, result.CoursesCreated);
    }

    private async Task<RoleAssignment> AdminAsync(Fixture f) =>
        await AccessTestData.AssignAsync(factory, f.Tree, f.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

    [Fact]
    public async Task Endpoint_copies_courses_only_when_asked_and_conflict_is_409()
    {
        var f = await SetUpAsync("CB6");
        var (owned, _) = await AddCoursesAsync(f);
        var admin = await AdminAsync(f);

        var without = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnitsCopy, admin,
            new CopyBranchRequest(f.X.Id, f.Tree.School2.Id, "-A", false, false, null, null));
        var with = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnitsCopy, admin,
            new CopyBranchRequest(f.X.Id, f.Tree.School2.Id, "-B", false, false, null, null, IncludeCourses: true));
        var again = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnitsCopy, admin,
            new CopyBranchRequest(f.X.Id, f.Tree.School2.Id, "-B", false, false, null, null, IncludeCourses: true));

        Assert.Equal(HttpStatusCode.Created, without.StatusCode);
        Assert.Equal(HttpStatusCode.Created, with.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<Course>().AnyAsync(c => c.Code == owned.Code + "-A"));
        Assert.True(await db.Set<Course>().AnyAsync(c => c.Code == owned.Code + "-B"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Template_endpoints_carry_courses_with_placeholders()
    {
        var f = await SetUpAsync("CB7");
        var (owned, _) = await AddCoursesAsync(f);
        var admin = await AdminAsync(f);
        var literal = owned.Code[..3];
        var tail = owned.Code[3..];

        var saved = await _host.SendAsync(HttpMethod.Post, Routes.Templates, admin,
            new SaveTemplateRequest(
                f.X.Id, "قالب", "Template", "Shared", false, false, new Dictionary<string, string> { [literal] = "{Prefix}", ["X"] = "{CodeX}", ["Y"] = "{CodeY}" },
                IncludeCourses: true));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var id = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("summary").GetProperty("id").GetGuid();

        var newPrefix = "TP" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var applied = await _host.SendAsync(HttpMethod.Post, Routes.TemplateApply(id), admin,
            new ApplyTemplateRequest(f.Tree.School2.Id, new Dictionary<string, string> { ["Prefix"] = newPrefix, ["CodeX"] = newPrefix + "X", ["CodeY"] = newPrefix + "Y" }));

        Assert.Equal(HttpStatusCode.Created, applied.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var expected = newPrefix + tail;
        Assert.True(await db.Set<Course>().AnyAsync(c => c.Code == expected));
    }
}
