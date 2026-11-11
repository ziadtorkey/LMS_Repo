using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Enrollment;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Enrollment;

/// <summary>M3 Task 28 (#236, FR-ENR-05, D43): transfer between classes of one academic year.</summary>
[Collection(ApiCollection.Name)]
public sealed class TransferTests(LmsApiFactory factory) : IDisposable
{
    private static readonly DateOnly Start = new(2041, 9, 1);
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly On = new(2041, 11, 1);
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// Year at the root; class From and class To at School1, class Other at School2 (same level type). Courses: A in all three,
    /// B only in From, C (mandatory) and Opt (optional) only in To. The student is placed in From (enrolled in A and B).
    /// </summary>
    private sealed record Setup(
        AccessTree Tree, RoleAssignment Admin, AcademicYear Year, Class From, Class To, Class Other, Course A, Course B, Course C,
        ClassCourse FromA, ClassCourse FromB, ClassCourse ToA, ClassCourse ToC, ClassCourse ToOpt, Guid Student, Guid GuardianUser,
        Guid TeacherFrom, Guid TeacherTo);

    private async Task<Setup> SetUpAsync(string name, string gender = "female")
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == year.Id)).Activate();
            await db.SaveChangesAsync();
        }

        var a = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var b = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var c = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var opt = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var from = await AcademicTestKit.AddClassAsync(factory, tree.School1, year, "FROM");
        var to = await AcademicTestKit.AddClassAsync(factory, tree.School1, year, "TO");
        var other = await AcademicTestKit.AddClassAsync(factory, tree.School2, year, "OTHER");
        var teacherFrom = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var teacherTo = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardianUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());

        ClassCourse fromA, fromB, toA, toC, toOpt;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            fromA = new ClassCourse(from, a, false);
            fromB = new ClassCourse(from, b, false);
            toA = new ClassCourse(to, a, false);
            toC = new ClassCourse(to, c, false);
            toOpt = new ClassCourse(to, opt, true);
            db.AddRange(fromA, fromB, toA, toC, toOpt, new ClassCourse(other, a, false));
            db.AddRange(
                new ClassCourseTeacher(fromA, teacherFrom, ClassCourseTeacher.PrimaryRole, Start),
                new ClassCourseTeacher(toA, teacherTo, ClassCourseTeacher.PrimaryRole, Start));
            var tracked = await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            tracked.Update(tracked.NameAr, tracked.NameEn, null, null, tracked.DateOfBirth, gender, tracked.Religion, tracked.Nationality, null, null, null, null);
            var guardian = new GuardianProfile(tree.Org.Id, "ولي", "Guardian", PeopleTestKit.NewMobile(), null, null);
            guardian.LinkUser(guardianUser);
            db.Add(guardian);
            db.Add(StudentGuardian.For(tracked, guardian, "Father", true));
            await db.SaveChangesAsync();
        }

        using (var scope = new EngineScope(factory.Services))
        {
            var placed = await scope.Db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            await scope.Engine.PlaceAsync(placed, await scope.Db.Set<Class>().SingleAsync(c2 => c2.Id == from.Id), Start, false, default);
        }

        return new Setup(tree, admin, year, from, to, other, a, b, c, fromA, fromB, toA, toC, toOpt, student.Id, guardianUser, teacherFrom, teacherTo);
    }

    private Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(method, url, caller, body);

    private static TransferStudentRequest Request(Setup s, Class to, DateOnly? on = null, bool confirm = false) =>
        new(s.Student, to.Id, on ?? On, "Parents asked", confirm);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(string? Code, string? Reason)> ProblemAsync(HttpResponseMessage r)
    {
        var body = await JsonAsync(r);
        return (body.GetProperty("code").GetString(), body.TryGetProperty("reason", out var reason) ? reason.GetString() : null);
    }

    private async Task<List<CourseEnrollment>> EnrollmentsAsync(Guid studentId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<CourseEnrollment>().AsNoTracking().Where(e => e.StudentId == studentId).ToListAsync();
    }

    [Fact]
    public async Task Transfer_remaps_common_courses_drops_others_and_adds_target_courses()
    {
        var s = await SetUpAsync("TR1");
        var before = await EnrollmentsAsync(s.Student);

        var response = await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<StudentTransferDto>())!;
        Assert.Equal((s.To.NameEn, On), (dto.ToClassNameEn, dto.EffectiveOn));
        var after = await EnrollmentsAsync(s.Student);
        var oldA = before.Single(e => e.ClassCourseId == s.FromA.Id);
        var oldB = before.Single(e => e.ClassCourseId == s.FromB.Id);
        Assert.Equal((CourseEnrollment.DroppedStatus, "transfer", On.AddDays(-1)), (after.Single(e => e.Id == oldA.Id).Status, after.Single(e => e.Id == oldA.Id).DropReason, after.Single(e => e.Id == oldA.Id).To));
        Assert.Equal(CourseEnrollment.DroppedStatus, after.Single(e => e.Id == oldB.Id).Status); // course B is not in the target class
        var active = after.Where(e => e.Status == CourseEnrollment.ActiveStatus).ToList();
        Assert.Equal(new[] { s.ToA.Id, s.ToC.Id }.Order(), active.Select(e => e.ClassCourseId).Order()); // not the optional one
        var carried = active.Single(e => e.ClassCourseId == s.ToA.Id);
        Assert.Equal((EnrollmentSources.Transfer, oldA.Id, On), (carried.Source, carried.CarriedFromEnrollmentId, carried.From));
        Assert.Equal(EnrollmentSources.Auto, active.Single(e => e.ClassCourseId == s.ToC.Id).Source);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var placements = await db.Set<ClassStudent>().Where(p => p.StudentId == s.Student).OrderBy(p => p.From).ToListAsync();
        Assert.Equal([(ClassStudent.TransferredOutStatus, On.AddDays(-1)), (ClassStudent.ActiveStatus, (DateOnly?)null)],
            placements.Select(p => (p.Status, p.To)).ToArray());
        Assert.Equal(s.To.Id, placements[1].ClassId);
    }

    [Fact]
    public async Task Preview_lists_kept_dropped_added_and_restricted_courses_without_changing_anything()
    {
        var s = await SetUpAsync("TR2");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseRestriction(s.C, CourseRestrictionEvaluator.Gender, ["male"])); // the girl cannot take course C
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(s.Admin, HttpMethod.Post, Routes.TransfersPreview, Request(s, s.To));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<TransferPreviewDto>())!;
        Assert.Equal([s.A.Code], preview.KeptCourseCodes);
        Assert.Equal([s.B.Code], preview.DroppedCourseCodes);
        Assert.Empty(preview.AddedCourseCodes);
        Assert.Equal([s.C.Code], preview.RestrictedCourseCodes); // reported, not enrolled
        Assert.False(preview.OverCapacity);
        Assert.Equal(2, (await EnrollmentsAsync(s.Student)).Count(e => e.Status == CourseEnrollment.ActiveStatus)); // nothing changed

        // And the real transfer does exactly that.
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To))).StatusCode);
        var active = (await EnrollmentsAsync(s.Student)).Where(e => e.Status == CourseEnrollment.ActiveStatus).ToList();
        Assert.Equal([s.ToA.Id], active.Select(e => e.ClassCourseId).ToArray());
    }

    [Fact]
    public async Task Old_class_history_and_enrollments_stay_readable()
    {
        var s = await SetUpAsync("TR3");
        await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To));

        var dropped = (await (await SendAsync(s.Admin, HttpMethod.Get, Routes.StudentEnrollments(s.Student) + "?status=Dropped")).Content
            .ReadFromJsonAsync<PagedResult<EnrollmentDto>>())!;
        Assert.Equal(2, dropped.Items.Count); // A and B of the old class, with their drop reason
        Assert.All(dropped.Items, e => Assert.Equal("transfer", e.DropReason));
        var history = (await (await SendAsync(s.Admin, HttpMethod.Get, Routes.StudentTransfers(s.Student))).Content.ReadFromJsonAsync<List<StudentTransferDto>>())!;
        var row = Assert.Single(history);
        Assert.Equal(("Parents asked", On), (row.Reason, row.EffectiveOn));
        Assert.Equal(s.From.NameEn, row.FromClassNameEn);
        Assert.Equal(s.To.NameEn, row.ToClassNameEn);
    }

    [Fact]
    public async Task Invalid_transfers_are_400_with_a_reason()
    {
        var s = await SetUpAsync("TR4");
        var otherYear = await AcademicTestKit.AddYearAsync(factory, s.Tree.School2);
        var inOtherYear = await AcademicTestKit.AddClassAsync(factory, s.Tree.School2, otherYear, "NEXT");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == otherYear.Id)).Activate();
            var deeper = new LevelType(s.Tree.Org.Id, "أعمق", "Deeper", 9, true);
            db.Add(deeper);
            var sub = OrgUnit.CreateChild(s.Tree.School1, deeper.Id, "فرع", "Sub", "SUB");
            db.Add(sub);
            await db.SaveChangesAsync();
        }

        Class differentLevel;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var sub = await db.OrgUnits.SingleAsync(u => u.Code == "SUB" && u.OrganizationId == s.Tree.Org.Id);
            differentLevel = new Class(sub, s.Year.Id, "DEEP", "فصل", "Deep class", 30);
            db.Add(differentLevel);
            await db.SaveChangesAsync();
        }

        var unplaced = await PeopleTestKit.AddStudentAsync(factory, s.Tree.School1, PeopleTestKit.NewCode());

        async Task<(string?, string?)> Try(TransferStudentRequest request)
        {
            var response = await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await ProblemAsync(response);
        }

        Assert.Equal((ProblemCodes.transfer_invalid, "same_class"), await Try(Request(s, s.From)));
        Assert.Equal((ProblemCodes.transfer_invalid, "different_level_type"), await Try(Request(s, differentLevel)));
        Assert.Equal((ProblemCodes.transfer_invalid, "date_outside_year"), await Try(Request(s, s.To, new DateOnly(2030, 1, 1))));
        Assert.Equal((ProblemCodes.transfer_invalid, "not_placed"), await Try(new TransferStudentRequest(unplaced.Id, s.To.Id, On, "x", false)));
        // A class of another academic year: the student is placed, but not in that year.
        Assert.Equal((ProblemCodes.transfer_invalid, "different_year"), await Try(Request(s, inOtherYear)));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, new TransferStudentRequest(s.Student, s.To.Id, On, " ", false))).StatusCode);
    }

    [Fact]
    public async Task Target_full_in_block_mode_is_409()
    {
        var s = await SetUpAsync("TR5");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == s.To.Id)).Update("TO", "فصل", "To", 1, null);
            db.Add(new OrgUnitSetting(s.Tree.Org.Id, s.Tree.Root.Id, s.Tree.Root.Path, SettingKeys.ClassesCapacityMode.Name, "\"Block\"", false));
            await db.SaveChangesAsync();
        }

        var other = await PeopleTestKit.AddStudentAsync(factory, s.Tree.School1, PeopleTestKit.NewCode());
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Engine.PlaceAsync(await scope.Db.Set<StudentProfile>().SingleAsync(x => x.Id == other.Id),
                await scope.Db.Set<Class>().SingleAsync(c => c.Id == s.To.Id), Start, false, default);
        }

        var response = await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To, confirm: true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.class_full, (await ProblemAsync(response)).Code);
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(s.From.Id, (await read.Set<ClassStudent>().SingleAsync(p => p.StudentId == s.Student && p.Status == ClassStudent.ActiveStatus)).ClassId); // nothing moved
    }

    [Fact]
    public async Task Warn_mode_over_capacity_needs_confirmation()
    {
        var s = await SetUpAsync("TR6");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == s.To.Id)).Update("TO", "فصل", "To", 1, null);
            await db.SaveChangesAsync();
        }

        var other = await PeopleTestKit.AddStudentAsync(factory, s.Tree.School1, PeopleTestKit.NewCode());
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Engine.PlaceAsync(await scope.Db.Set<StudentProfile>().SingleAsync(x => x.Id == other.Id),
                await scope.Db.Set<Class>().SingleAsync(c => c.Id == s.To.Id), Start, false, default);
        }

        var refused = await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.over_capacity), (refused.StatusCode, (await ProblemAsync(refused)).Code));
        Assert.True((await (await SendAsync(s.Admin, HttpMethod.Post, Routes.TransfersPreview, Request(s, s.To))).Content.ReadFromJsonAsync<TransferPreviewDto>())!.OverCapacity);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To, confirm: true))).StatusCode);
    }

    [Fact]
    public async Task Group_memberships_are_removed()
    {
        var s = await SetUpAsync("TR7");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var enrollment = await db.Set<CourseEnrollment>().SingleAsync(e => e.StudentId == s.Student && e.ClassCourseId == s.FromA.Id);
            var group = new StudentGroup(s.FromA, "مجموعة", "Group", null, null);
            db.AddRange(group, new GroupMember(group, enrollment));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To))).StatusCode);

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await read.Set<GroupMember>().Where(m => m.StudentId == s.Student).ToListAsync());
    }

    [Fact]
    public async Task Guardian_and_both_teachers_are_notified()
    {
        var s = await SetUpAsync("TR8");

        Assert.Equal(HttpStatusCode.Created, (await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To))).StatusCode);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var recipients = await db.Set<Notification>().Where(n => n.EventKey == NotificationEvents.EnrollmentStudentTransferred
            && new[] { s.GuardianUser, s.TeacherFrom, s.TeacherTo }.Contains(n.UserId)).Select(n => n.UserId).ToListAsync();
        Assert.Equal(new[] { s.GuardianUser, s.TeacherFrom, s.TeacherTo }.Order(), recipients.Order());
    }

    [Fact]
    public async Task Cross_school_transfer_needs_both_units_visible_else_404()
    {
        var s = await SetUpAsync("TR9");
        var school1Admin = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school2Admin = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        // A School 1 admin does not see the School 2 class; a School 2 admin does not see the student's class.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(school1Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.Other))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(school2Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.Other))).StatusCode);

        // The root admin sees both: allowed (D43), and the student's home follows.
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.Other))).StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(s.Tree.School2.Id, (await db.Set<StudentProfile>().SingleAsync(x => x.Id == s.Student)).OrgUnitId);
        var active = (await EnrollmentsAsync(s.Student)).Where(e => e.Status == CourseEnrollment.ActiveStatus).ToList();
        Assert.Single(active); // only course A exists in the School 2 class
    }

    [Fact]
    public async Task Transfer_is_audited_and_needs_the_transfer_permission()
    {
        var s = await SetUpAsync("TR10");
        var teacher = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Teacher, s.TeacherFrom);
        var manager = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(teacher, HttpMethod.Post, Routes.Transfers, Request(s, s.To))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(manager, HttpMethod.Post, Routes.Transfers, Request(s, s.To))).StatusCode); // Manager holds Enrollment.Transfer

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var transferId = (await db.Set<StudentTransfer>().SingleAsync(t => t.StudentId == s.Student)).Id;
        Assert.Equal(manager.UserId, (await db.Set<StudentTransfer>().SingleAsync(t => t.Id == transferId)).CreatedBy); // who did it
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(ClassStudent) && e.EntityId != Guid.Empty && e.Action == AuditAction.Updated)); // the old placement ended
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(CourseEnrollment) && e.Action == AuditAction.Created));
    }

    [Fact]
    public async Task Restricted_course_in_target_is_reported_not_enrolled()
    {
        var s = await SetUpAsync("TR11");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseRestriction(s.A, CourseRestrictionEvaluator.Gender, ["male"]));
            await db.SaveChangesAsync();
        }

        // Course A is restricted to boys: the girl is dropped from it in the target class.
        var response = await SendAsync(s.Admin, HttpMethod.Post, Routes.Transfers, Request(s, s.To));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var active = (await EnrollmentsAsync(s.Student)).Where(e => e.Status == CourseEnrollment.ActiveStatus).ToList();
        Assert.Equal([s.ToC.Id], active.Select(e => e.ClassCourseId).ToArray());
    }
}
