using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Classes;

[Collection(ApiCollection.Name)]
public sealed class BulkClassesTests(LmsApiFactory factory) : IDisposable
{
    private readonly ClassTestHost _h = new(factory);

    public void Dispose() => _h.Dispose();

    private static BulkCreateClassesRequest Bulk(ClassTestHost.World w, int from, int to, string code = "B{N}", Guid? unit = null) =>
        new(unit ?? w.Tree.School1.Id, w.Year.Id, "صف {N}", "Class {N}", code, from, to, 25);

    private async Task<int> CountAsync(ClassTestHost.World w)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Class>().CountAsync(c => c.OrganizationId == w.Tree.Org.Id);
    }

    [Fact]
    public async Task Bulk_creates_Class_1_to_5()
    {
        var w = await _h.SeedAsync("BK1");

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 5));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<List<ClassSummaryDto>>())!;
        Assert.Equal(["Class 1", "Class 2", "Class 3", "Class 4", "Class 5"], created.Select(c => c.NameEn));
        Assert.Equal(["B1", "B2", "B3", "B4", "B5"], created.Select(c => c.Code));
        Assert.All(created, c => Assert.Equal(25, c.Capacity));
        Assert.Equal(5, await CountAsync(w));
    }

    [Fact]
    public async Task Bulk_range_over_50_or_bad_pattern_is_400_and_creates_nothing()
    {
        var w = await _h.SeedAsync("BK2");

        var tooMany = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 51));
        var fifty = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 50));
        var reversed = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 5, 1, "R{N}"));
        var noToken = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 2, "NOTOKEN"));
        var twoTokens = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 2, "{N}-{N}"));

        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(ProblemCodes.too_many_items, await ClassTestHost.CodeAsync(tooMany));
        var wrapped = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, int.MinValue + 1, int.MaxValue - 1));
        var negative = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, -3, 2));
        Assert.Equal(HttpStatusCode.BadRequest, wrapped.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fifty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, twoTokens.StatusCode);
        Assert.Equal(50, await CountAsync(w));
    }

    [Fact]
    public async Task Bulk_with_any_code_conflict_is_409_listing_codes_and_creates_nothing()
    {
        var w = await _h.SeedAsync("BK3");
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School2, w.Year, "B2"));
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School2, w.Year, "B4"));

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ProblemCodes.class_code_taken, body.GetProperty("code").GetString());
        Assert.Equal(["B2", "B4"], body.GetProperty("codes").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.Equal(2, await CountAsync(w));
    }

    [Fact]
    public async Task Copy_2026_to_2027_replaces_year_text_and_skips_existing()
    {
        var w = await _h.SeedAsync("BK4");
        var target = await _h.AddYearAsync(w.Tree.Root);
        var a = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "G1-2026") with { NameEn = "Grade 1 2026", NameAr = "الصف ١ 2026" });
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "G2-2026"));
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School2, w.Year, "G3-2026"));
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, target, "G2-2027")); // already there

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, target.Id, w.Tree.Root.Id, false, "2026", "2027"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CopyClassesResultDto>())!;
        Assert.Equal(2, result.Created);
        Assert.Equal(["G2-2027"], result.SkippedCodes);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var copied = await db.Set<Class>().SingleAsync(c => c.AcademicYearId == target.Id && c.Code == "G1-2027");
        Assert.Equal("Grade 1 2027", copied.NameEn);
        Assert.Equal("الصف ١ 2027", copied.NameAr);
        Assert.Equal(w.Tree.School1.Id, copied.OrgUnitId);
        Assert.Equal(a.Capacity, copied.Capacity);
        Assert.Equal("Active", copied.Status);
        Assert.Equal(1, await db.Set<Class>().CountAsync(c => c.AcademicYearId == target.Id && c.OrgUnitId == w.Tree.School2.Id));
    }

    [Fact]
    public async Task Copy_skips_units_where_the_target_year_is_not_effective_and_refuses_a_closed_target()
    {
        var w = await _h.SeedAsync("BK5");
        var school2Year = await _h.AddYearAsync(w.Tree.School2); // School 2 defines its own list: the target is not effective there
        var target = await _h.AddYearAsync(w.Tree.Root);
        var closed = await _h.AddYearAsync(w.Tree.Root, closed: true);
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "K1"));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new Class(w.Tree.School2, school2Year.Id, "K2", "ا", "N", 5));
            await db.SaveChangesAsync();
        }

        var closedTarget = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, closed.Id, w.Tree.Root.Id, false, null, null));
        var viaSchool2 = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(school2Year.Id, target.Id, w.Tree.Root.Id, false, null, null));

        Assert.Equal(HttpStatusCode.Conflict, closedTarget.StatusCode);
        Assert.Equal(ProblemCodes.year_closed, await ClassTestHost.CodeAsync(closedTarget));
        Assert.Equal(0, (await viaSchool2.Content.ReadFromJsonAsync<CopyClassesResultDto>())!.Created);
    }

    [Fact]
    public async Task Copy_keeps_homeroom_teacher_and_never_copies_students()
    {
        var w = await _h.SeedAsync("BK6");
        var target = await _h.AddYearAsync(w.Tree.Root);
        var teacher = await Lms.IntegrationTests.People.PeopleTestKit.AddUserAsync(factory);
        await Lms.IntegrationTests.Access.AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.Teacher, teacher.Id);
        var source = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "H1", homeroom: teacher.Id));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Class>().SingleAsync(c => c.Id == source.Id);
            var student = await Lms.IntegrationTests.People.PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, Lms.IntegrationTests.People.PeopleTestKit.NewCode());
            db.Add(new ClassStudent(cls, student.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
            await db.SaveChangesAsync();
        }

        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, target.Id, w.Tree.School1.Id, false, null, null));

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var copy = await read.Set<Class>().SingleAsync(c => c.AcademicYearId == target.Id);
        Assert.Equal(teacher.Id, copy.HomeroomTeacherUserId);
        Assert.Equal(0, await read.Set<ClassStudent>().CountAsync(s => s.ClassId == copy.Id));
    }

    [Fact]
    public async Task CreateClasses_policy_blocks_bulk_and_copy()
    {
        var w = await _h.SeedAsync("BK7");
        var target = await _h.AddYearAsync(w.Tree.Root);
        await _h.Host.AddPolicyAsync(w.Tree, w.Tree.Root, PolicyCatalog.CreateClasses);

        var bulk = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 2));
        var copy = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, target.Id, w.Tree.Root.Id, false, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, bulk.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, copy.StatusCode);
        Assert.Equal(0, await CountAsync(w));
    }

    [Fact]
    public async Task Bulk_and_copy_are_denied_to_a_teacher_and_hidden_from_other_clients()
    {
        var w = await _h.SeedAsync("BK8");
        var other = await _h.SeedAsync("BK8B");
        var teacher = await Lms.IntegrationTests.Access.AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher,
            (await Lms.IntegrationTests.People.PeopleTestKit.AddUserAsync(factory)).Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await _h.SendAsync(teacher, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Post, Routes.ClassesBulk, Bulk(w, 1, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, w.Year.Id, w.Tree.School1.Id, false, null, null))).StatusCode);
    }

    [Fact]
    public async Task Copy_skips_units_where_a_CreateClasses_policy_below_the_copy_unit_blocks()
    {
        var w = await _h.SeedAsync("BK9");
        var target = await _h.AddYearAsync(w.Tree.Root);
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "P1-2026"));
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School2, w.Year, "P2-2026"));
        await _h.Host.AddPolicyAsync(w.Tree, w.Tree.School2, PolicyCatalog.CreateClasses);

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, target.Id, w.Tree.Root.Id, false, "2026", "2027"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CopyClassesResultDto>())!;
        Assert.Equal(1, result.Created);
        Assert.Equal(["P2-2027"], result.SkippedCodes);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(0, await db.Set<Class>().CountAsync(c => c.AcademicYearId == target.Id && c.OrgUnitId == w.Tree.School2.Id));
        Assert.Equal(1, await db.Set<Class>().CountAsync(c => c.AcademicYearId == target.Id && c.OrgUnitId == w.Tree.School1.Id));
    }

    [Fact]
    public async Task Copy_over_500_classes_is_400_too_many_items()
    {
        var w = await _h.SeedAsync("BK10");
        var target = await _h.AddYearAsync(w.Tree.Root);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.AddRange(Enumerable.Range(0, 501).Select(i => new Class(w.Tree.School1, w.Year.Id, "BIG" + i, "ا", "N", 5)));
            await db.SaveChangesAsync();
        }

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, target.Id, w.Tree.Root.Id, false, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.too_many_items, await ClassTestHost.CodeAsync(response));
    }

    [Fact]
    public async Task Parallel_creates_of_the_same_code_end_in_one_201_and_409s_never_500()
    {
        var w = await _h.SeedAsync("BK11");
        var body = ClassTestHost.Save(w.Tree.School1, w.Year, "RACE");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, body)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal(ProblemCodes.class_code_taken, await ClassTestHost.CodeAsync(conflict));
        }

        var bulk = Bulk(w, 1, 3, "RB{N}");
        var bulkResponses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesBulk, bulk)));
        Assert.Equal(1, bulkResponses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, bulkResponses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.DoesNotContain(bulkResponses, r => (int)r.StatusCode >= 500);
    }
}
