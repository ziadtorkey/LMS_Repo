using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Classes;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Classes;

[Collection(ApiCollection.Name)]
public sealed class MatrixTests(LmsApiFactory factory) : IDisposable
{
    private readonly ClassTestHost _h = new(factory);

    public void Dispose() => _h.Dispose();

    private async Task<DistributionMatrixDto> MatrixAsync(Lms.Domain.Access.RoleAssignment caller, Guid unitId, Guid yearId)
    {
        var response = await _h.SendAsync(caller, HttpMethod.Get, Routes.ClassesMatrixFor(unitId, yearId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DistributionMatrixDto>())!;
    }

    [Fact]
    public async Task Matrix_lists_cells_for_unit_and_year()
    {
        var w = await _h.SeedAsync("MX1");
        var math = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        var art = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        var unused = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        var notAllocated = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        foreach (var course in new[] { math, art, unused })
        {
            await AcademicTestKit.AllocateAsync(factory, course, w.Tree.Root);
        }

        var a = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "MA"));
        var b = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "MB"));
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School2, w.Year, "MC"));
        var otherYear = await _h.AddYearAsync(w.Tree.Root);
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, otherYear, "MD"));
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(a.Id), new AddClassCoursesRequest([math.Id, art.Id], false));
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(b.Id), new AddClassCoursesRequest([math.Id], true));

        var matrix = await MatrixAsync(w.Admin, w.Tree.School1.Id, w.Year.Id);

        Assert.Equal(["MA", "MB"], matrix.Classes.Select(c => c.Code).Order());
        Assert.Equal(new[] { math.Id, art.Id, unused.Id }.Order(), matrix.Courses.Select(c => c.Id).Order());
        Assert.DoesNotContain(matrix.Courses, c => c.Id == notAllocated.Id);
        Assert.Equal(3, matrix.Cells.Count);
        Assert.True(matrix.Cells.Single(c => c.ClassId == b.Id && c.CourseId == math.Id).IsOptional);
        Assert.False(matrix.Cells.Single(c => c.ClassId == a.Id && c.CourseId == math.Id).IsOptional);
        var rootView = await MatrixAsync(w.Admin, w.Tree.Root.Id, w.Year.Id);
        Assert.Equal(3, rootView.Classes.Count);
    }

    [Fact]
    public async Task Matrix_includes_courses_already_on_classes_even_when_no_longer_allocated_and_hides_other_units()
    {
        var w = await _h.SeedAsync("MX2");
        var course = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        var allocation = await AcademicTestKit.AllocateAsync(factory, course, w.Tree.Root);
        var cls = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(cls.Id), new AddClassCoursesRequest([course.Id], false));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<Lms.Domain.Courses.CourseAllocation>().SingleAsync(a => a.Id == allocation.Id));
            await db.SaveChangesAsync();
        }

        var matrix = await MatrixAsync(w.Admin, w.Tree.School1.Id, w.Year.Id);
        var other = await _h.SeedAsync("MX2B");

        Assert.Equal(course.Id, Assert.Single(matrix.Courses).Id);
        Assert.Single(matrix.Cells);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Get, Routes.ClassesMatrixFor(w.Tree.School1.Id, w.Year.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.ClassesMatrixFor(Guid.NewGuid(), w.Year.Id))).StatusCode);
    }

    [Fact]
    public async Task Teacher_matrix_contains_only_own_classes_and_over_200_classes_is_400()
    {
        var w = await _h.SeedAsync("MX3");
        var course = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, course, w.Tree.Root);
        var mine = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(mine.Id), new AddClassCoursesRequest([course.Id], false));
        var user = await PeopleTestKit.AddUserAsync(factory);
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, user.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.ClassId == mine.Id);
            db.Add(new Lms.Domain.Teaching.ClassCourseTeacher(cc, user.Id, "Primary", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var matrix = await MatrixAsync(teacher, w.Tree.School1.Id, w.Year.Id);
        Assert.Equal(mine.Id, Assert.Single(matrix.Classes).Id);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == w.Tree.School2.Id);
            db.AddRange(Enumerable.Range(0, 201).Select(i => new Class(unit, w.Year.Id, "BIG" + i, "ا", "N", 5)));
            await db.SaveChangesAsync();
        }

        var tooMany = await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.ClassesMatrixFor(w.Tree.School2.Id, w.Year.Id));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(ProblemCodes.too_many_items, (await tooMany.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
    }
}
