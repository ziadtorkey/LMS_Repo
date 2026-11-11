using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Enrollment;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Groups of a class course (M3 Task 27, #235): everything is checked at the class unit (School1). School 2 and the other
/// client get 404. The Teacher holds Groups.* only for class courses they teach, and the probe's class course has no teacher
/// assignment, so every row answers 404 for the Teacher (the relationship rule, D32).
/// </summary>
public sealed class GroupsProbeRows : IProbeRowModule
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly SaveGroupRequest Body = new("مجموعة", "Probe group", null, null);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("groups create", HttpMethod.Post, Routes.ClassCourseGroupsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseGroups((await NewAsync(w)).ClassCourse.Id), Body),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("groups list", HttpMethod.Get, Routes.ClassCourseGroupsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseGroups((await NewAsync(w)).ClassCourse.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("groups update", HttpMethod.Put, Routes.GroupPattern,
            async (w, _) => new ProbeRequest(Routes.Group((await NewAsync(w)).Group.Id), Body),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("groups delete", HttpMethod.Delete, Routes.GroupPattern,
            async (w, _) => new ProbeRequest(Routes.Group((await NewAsync(w)).Group.Id)),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
        new("group members add", HttpMethod.Post, Routes.GroupMembersPattern,
            async (w, _) =>
            {
                var s = await NewAsync(w);
                return new ProbeRequest(Routes.GroupMembers(s.Group.Id), new GroupMembersRequest([s.StudentId]));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("group members remove", HttpMethod.Post, Routes.GroupMembersRemovePattern,
            async (w, _) =>
            {
                var s = await NewAsync(w);
                return new ProbeRequest(Routes.GroupMembersRemove(s.Group.Id), new GroupMembersRequest([s.StudentId]));
            },
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
        new("group members list", HttpMethod.Get, Routes.GroupMembersPattern,
            async (w, _) => new ProbeRequest(Routes.GroupMembers((await NewAsync(w)).Group.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("group suggestions", HttpMethod.Get, Routes.GroupSuggestionsPattern,
            async (w, _) => new ProbeRequest(Routes.Groups + $"/{(await NewAsync(w)).Group.Id}/suggestions"),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
    ];

    private sealed record Seeded(ClassCourse ClassCourse, StudentGroup Group, Guid StudentId);

    /// <summary>A class at School1 with one course, an enrolled student and a group (the student is not a member yet).</summary>
    private static async Task<Seeded> NewAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        var student = await PeopleTestKit.AddStudentAsync(factory, school1, PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var placement = new ClassStudent(cls, student.Id, Today);
        var enrollment = new CourseEnrollment(classCourse, placement, EnrollmentSources.Auto, Today);
        var group = new StudentGroup(classCourse, "مجموعة", "Seed group", null, null);
        var member = new GroupMember(group, enrollment);
        db.AddRange(placement, enrollment, group);
        await db.SaveChangesAsync();
        // The "remove" row needs a member to remove; the "add" row works because adding an existing member is a no-op.
        db.Add(member);
        await db.SaveChangesAsync();
        return new Seeded(classCourse, group, student.Id);
    }
}
