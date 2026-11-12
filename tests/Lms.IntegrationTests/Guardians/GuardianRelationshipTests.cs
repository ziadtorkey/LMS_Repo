using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Enrollment;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Lms.IntegrationTests.Guardians.GuardianTestKit;

namespace Lms.IntegrationTests.Guardians;

/// <summary>
/// #310 (D42, PRD 3.3 step 2): a Teacher holds Students.View but reaches only the students of their own class courses (active
/// enrollments) or homeroom class, so the guardian routes show them only the guardians of those students.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GuardianRelationshipTests
{
    private readonly LmsApiFactory _factory;
    private readonly GuardianTestKit _kit;

    public GuardianRelationshipTests(LmsApiFactory factory)
    {
        _factory = factory;
        _kit = new GuardianTestKit(factory, factory.WithWebHostBuilder(_ => { }));
    }

    /// <summary>Class 1 / course CX (Teacher A, student 1, guardian 1) and class 2 / course CY (Teacher B, student 2, guardian 2).</summary>
    private async Task<(RoleAssignment Admin, RoleAssignment TeacherA, Guid Student1, Guid Guardian1, Guid Student2, Guid Guardian2)> WorldAsync(string name)
    {
        var (tree, admin) = await _kit.SetUpAsync(name);
        var year = await AcademicTestKit.AddYearAsync(_factory, tree.School1);
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var students = new List<Guid>();
        var teachers = new List<RoleAssignment>();
        foreach (var _ in new[] { 1, 2 })
        {
            var cls = await AcademicTestKit.AddClassAsync(_factory, tree.School1, year);
            var course = await AcademicTestKit.AddCourseAsync(_factory, tree.School1);
            var classCourse = await AcademicTestKit.AddClassCourseAsync(_factory, cls, course);
            var student = await _kit.StudentAsync(tree.School1);
            var placement = await AcademicTestKit.PlaceAsync(_factory, cls, student);
            var user = await PeopleTestKit.AddUserAsync(_factory);
            await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
            {
                db.AddRange(new CourseEnrollment(classCourse, placement, Lms.Shared.Enrollment.EnrollmentSources.Auto, start),
                    new ClassCourseTeacher(classCourse, user.Id, ClassCourseTeacher.PrimaryRole, start));
                await db.SaveChangesAsync();
            }

            students.Add(student.Id);
            teachers.Add(await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Teacher, user.Id));
        }

        var g1 = await _kit.LinkOkAsync(admin, students[0], NewGuardian(primary: true, name: "Parent One"));
        var g2 = await _kit.LinkOkAsync(admin, students[1], NewGuardian(primary: true, name: "Parent Two"));
        return (admin, teachers[0], students[0], g1.GuardianId, students[1], g2.GuardianId);
    }

    [Fact]
    public async Task Teacher_reads_guardians_only_of_the_students_they_teach()
    {
        var w = await WorldAsync("GR1");

        var own = await _kit.SendAsync(HttpMethod.Get, Routes.StudentGuardians(w.Student1), w.TeacherA);
        var foreignStudent = await _kit.SendAsync(HttpMethod.Get, Routes.StudentGuardians(w.Student2), w.TeacherA); // QA2 probe 1
        var ownGuardian = await _kit.SendAsync(HttpMethod.Get, Routes.Guardian(w.Guardian1), w.TeacherA);
        var foreignGuardian = await _kit.SendAsync(HttpMethod.Get, Routes.Guardian(w.Guardian2), w.TeacherA); // QA2 probe 2
        var list = await _kit.SendAsync(HttpMethod.Get, Routes.Guardians, w.TeacherA); // QA2 probe 3

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignStudent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownGuardian.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignGuardian.StatusCode);
        var page = (await list.Content.ReadFromJsonAsync<PagedResult<GuardianSummaryDto>>(Web))!;
        Assert.Equal([w.Guardian1], page.Items.Select(g => g.Id));
        Assert.Equal(1, page.Items[0].ChildrenInScope);
    }

    [Fact]
    public async Task A_guardian_of_two_students_shows_a_teacher_only_the_child_they_reach()
    {
        var w = await WorldAsync("GR2");
        // the second student also has guardian 1 (siblings): the teacher of student 1 sees the guardian with one child
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var guardian1 = await db.Set<Lms.Domain.People.GuardianProfile>().SingleAsync(g => g.Id == w.Guardian1);
            db.Add(Lms.Domain.People.StudentGuardian.For(await db.Set<Lms.Domain.People.StudentProfile>().SingleAsync(s => s.Id == w.Student2), guardian1, "Father", false));
            await db.SaveChangesAsync();
        }

        var detail = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Guardian(w.Guardian1), w.TeacherA)).Content.ReadFromJsonAsync<GuardianDetailDto>(Web))!;
        var adminDetail = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Guardian(w.Guardian1), w.Admin)).Content.ReadFromJsonAsync<GuardianDetailDto>(Web))!;

        Assert.Equal([w.Student1], detail.Children.Select(c => c.StudentId));
        Assert.Equal(2, adminDetail.Children.Count);
    }

    [Fact]
    public async Task Admin_still_sees_every_guardian_of_the_unit()
    {
        var w = await WorldAsync("GR3");

        var list = (await (await _kit.SendAsync(HttpMethod.Get, Routes.Guardians, w.Admin)).Content.ReadFromJsonAsync<PagedResult<GuardianSummaryDto>>(Web))!;

        Assert.Equal(2, list.Items.Count);
        Assert.Equal(HttpStatusCode.OK, (await _kit.SendAsync(HttpMethod.Get, Routes.StudentGuardians(w.Student2), w.Admin)).StatusCode);
    }
}
