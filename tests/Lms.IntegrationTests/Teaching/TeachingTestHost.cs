using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Domain.Identity;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Teaching;

namespace Lms.IntegrationTests.Teaching;

/// <summary>A client with a current year, a class at School1, a course available there and teachers with Teacher roles.</summary>
internal sealed class TeachingTestHost(LmsApiFactory factory) : IDisposable
{
    public ClassTestHost Classes { get; } = new(factory);
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public void Dispose() => Classes.Dispose();

    public sealed record Setup(
        ClassTestHost.World W, ClassSummaryDto Class, Course Course, ClassCourseDto ClassCourse);

    public async Task<Setup> SetUpAsync(string name)
    {
        var w = await Classes.SeedAsync(name, currentYear: true);
        var course = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, course, w.Tree.Root);
        var cls = await Classes.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        var added = await Classes.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(cls.Id), new AddClassCoursesRequest([course.Id], false));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        return new Setup(w, cls, course, (await added.Content.ReadFromJsonAsync<List<ClassCourseDto>>())![0]);
    }

    /// <summary>A user holding the Teacher role at the unit.</summary>
    public async Task<(User User, RoleAssignment Assignment)> TeacherAsync(ClassTestHost.World w, Lms.Domain.Organizations.OrgUnit? unit = null)
    {
        var user = await PeopleTestKit.AddUserAsync(factory);
        return (user, await AccessTestData.AssignAsync(factory, w.Tree, unit ?? w.Tree.School1, BuiltInRoles.Teacher, user.Id));
    }

    public Task<HttpResponseMessage> GrantAsync(ClassTestHost.World w, Guid unitId, IReadOnlyList<Guid> users, IReadOnlyList<Guid> courses) =>
        Classes.SendAsync(w.Admin, HttpMethod.Post, Routes.TeacherEligibility, new GrantEligibilityRequest(unitId, users, courses));

    public async Task<(User User, RoleAssignment Assignment)> EligibleTeacherAsync(Setup s)
    {
        var teacher = await TeacherAsync(s.W);
        var grant = await GrantAsync(s.W, s.W.Tree.School1.Id, [teacher.User.Id], [s.Course.Id]);
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);
        return teacher;
    }

    public Task<HttpResponseMessage> AssignAsync(Setup s, Guid userId, string role = TeacherRoles.Primary, DateOnly? from = null) =>
        Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCourseTeachers(s.ClassCourse.Id), new AssignTeacherRequest(userId, role, from));

    public async Task<List<ClassCourseTeacherDto>> TeachersAsync(Setup s, bool history = false)
    {
        var response = await Classes.SendAsync(s.W.Admin, HttpMethod.Get,
            Routes.ClassCourseTeachers(s.ClassCourse.Id) + (history ? "?includeHistory=true" : string.Empty));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ClassCourseTeacherDto>>())!;
    }
}
