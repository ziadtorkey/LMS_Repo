using System.Net;
using Lms.Domain.Access;
using Lms.Domain.Announcements;
using Lms.IntegrationTests.Forums;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Announcements;

/// <summary>M6 Task 13 slice 1: authoring drafts, the scope guard, validation, edit rules and isolation.</summary>
[Collection(ApiCollection.Name)]
public sealed class AnnouncementAuthoringTests(LmsApiFactory factory)
{
    private static AnnouncementTargetDto Cc(Guid id) => new(AnnouncementTargetKinds.ClassCourse, id, false);

    private static AnnouncementTargetDto Org() => new(AnnouncementTargetKinds.Organization, null, false);

    private static CreateAnnouncementRequest Request(
        Guid unit, IReadOnlyList<AnnouncementTargetDto> targets, string titleAr = "عنوان", string titleEn = "Title", string? bodyAr = "<p>نص</p>",
        string? bodyEn = "<p>Text</p>", bool urgent = false, DateTime? expires = null) =>
        new(unit, titleAr, titleEn, bodyAr, bodyEn, targets, null, null, expires, false, false, null, false, urgent);

    private static async Task<AnnouncementDto> CreateOkAsync(ForumTestWorld w, RoleAssignment who, CreateAnnouncementRequest request)
    {
        var response = await w.SendAsync(who, HttpMethod.Post, Routes.OrgAnnouncements, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await w.ReadAsync<AnnouncementDto>(response);
    }

    private static Guid SchoolUnit(ForumTestWorld w) => w.World.Tree.School1.Id;

    private static Guid RootUnit(ForumTestWorld w) => w.World.Tree.Root.Id;

    [Fact]
    public async Task Both_titles_are_required()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA1");

        var noAr = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncements, Request(RootUnit(w), [Org()], titleAr: " "));
        var noEn = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncements, Request(RootUnit(w), [Org()], titleEn: ""));

        Assert.Equal(HttpStatusCode.BadRequest, noAr.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noEn.StatusCode);
    }

    [Fact]
    public async Task Body_is_sanitised_and_one_language_is_enough()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA2");

        var made = await CreateOkAsync(w, w.Admin, Request(RootUnit(w), [Org()], bodyAr: null, bodyEn: "<p>Hi</p><script>alert(1)</script>"));

        Assert.Null(made.BodyAr);
        Assert.DoesNotContain("script", made.BodyEn);
        Assert.Contains("Hi", made.BodyEn);
        Assert.Equal(AnnouncementStatuses.Draft, made.Status);
        var none = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncements, Request(RootUnit(w), [Org()], bodyAr: null, bodyEn: " "));
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_target_a_class_course_they_do_not_teach()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA3");

        var own = await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.OrgAnnouncements, Request(SchoolUnit(w), [Cc(w.ClassCourseA)]));
        var other = await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.OrgAnnouncements, Request(SchoolUnit(w), [Cc(w.ClassCourseB)]));

        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode); // never 403: existence is not revealed
    }

    [Fact]
    public async Task Teacher_cannot_use_unit_level_or_organization_targets()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA4");
        var kinds = new[]
        {
            Org(),
            new AnnouncementTargetDto(AnnouncementTargetKinds.Unit, SchoolUnit(w), true),
            new AnnouncementTargetDto(AnnouncementTargetKinds.Class, w.World.Class.Id, false),
        };

        foreach (var target in kinds)
        {
            var response = await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.OrgAnnouncements, Request(SchoolUnit(w), [target]));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Teacher_cannot_mark_urgent_but_a_unit_admin_can()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA5");

        var teacher = await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.OrgAnnouncements, Request(SchoolUnit(w), [Cc(w.ClassCourseA)], urgent: true));
        var admin = await CreateOkAsync(w, w.Admin, Request(RootUnit(w), [Org()], urgent: true));

        Assert.Equal(HttpStatusCode.Forbidden, teacher.StatusCode);
        Assert.True(admin.IsUrgent);
    }

    [Fact]
    public async Task A_student_cannot_author_or_list()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA6");

        var post = await w.SendAsync(w.StudentA, HttpMethod.Post, Routes.OrgAnnouncements, Request(SchoolUnit(w), [Cc(w.ClassCourseA)]));
        var list = await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.OrgAnnouncements);

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Malformed_targets_are_a_400_with_the_target_code()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA7");
        var bad = new[]
        {
            Array.Empty<AnnouncementTargetDto>(),
            [new AnnouncementTargetDto("Planet", Guid.NewGuid(), false)],
            [new AnnouncementTargetDto(AnnouncementTargetKinds.Organization, Guid.NewGuid(), false)],
            [new AnnouncementTargetDto(AnnouncementTargetKinds.ClassCourse, null, false)],
            [new AnnouncementTargetDto(AnnouncementTargetKinds.ClassCourse, w.ClassCourseA, true)],
        };

        foreach (var targets in bad)
        {
            var response = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncements, Request(RootUnit(w), targets));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.announcement_target_invalid, await ForumTestWorld.CodeAsync(response));
        }
    }

    [Fact]
    public async Task A_draft_can_be_edited_with_new_targets_listed_and_deleted_and_publish_is_not_available_yet()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA8");
        var made = await CreateOkAsync(w, w.Admin, Request(RootUnit(w), [Cc(w.ClassCourseA)]));

        var edit = await w.SendAsync(w.Admin, HttpMethod.Put, Routes.OrgAnnouncement(made.Id),
            new UpdateAnnouncementRequest("ع", "New", "<p>x</p>", null, [Cc(w.ClassCourseB)], null, null, null, true, false, null));
        var updated = await w.ReadAsync<AnnouncementDto>(edit);
        var list = await w.ReadAsync<PagedResult<AnnouncementListItemDto>>(
            await w.SendAsync(w.Admin, HttpMethod.Get, Routes.OrgAnnouncements + "?status=Draft"));
        var publish = await w.SendAsync(w.Admin, HttpMethod.Post, Routes.OrgAnnouncements, Request(RootUnit(w), [Org()]) with { Publish = true });
        var deleted = await w.SendAsync(w.Admin, HttpMethod.Delete, Routes.OrgAnnouncement(made.Id));

        Assert.Equal("New", updated.TitleEn);
        Assert.True(updated.IsPinned);
        Assert.Equal([w.ClassCourseB], updated.Targets.Select(t => t.TargetId!.Value));
        Assert.Contains(list.Items, i => i.Id == made.Id);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.Admin, HttpMethod.Get, Routes.OrgAnnouncement(made.Id))).StatusCode);
    }

    [Fact]
    public async Task A_teacher_sees_only_their_own_and_an_admin_sees_all_in_scope()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA9");
        var mine = await CreateOkAsync(w, w.TeacherA, Request(SchoolUnit(w), [Cc(w.ClassCourseA)]));
        var theirs = await CreateOkAsync(w, w.TeacherB, Request(SchoolUnit(w), [Cc(w.ClassCourseB)]));

        var teacherList = await w.ReadAsync<PagedResult<AnnouncementListItemDto>>(await w.SendAsync(w.TeacherA, HttpMethod.Get, Routes.OrgAnnouncements));
        var adminList = await w.ReadAsync<PagedResult<AnnouncementListItemDto>>(await w.SendAsync(w.Admin, HttpMethod.Get, Routes.OrgAnnouncements));
        var peek = await w.SendAsync(w.TeacherA, HttpMethod.Get, Routes.OrgAnnouncement(theirs.Id));

        Assert.Equal([mine.Id], teacherList.Items.Select(i => i.Id));
        Assert.Equal(2, adminList.Total);
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
    }

    [Fact]
    public async Task Capabilities_say_what_the_caller_may_author()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AA10");

        var admin = await w.ReadAsync<AnnouncementCapabilitiesDto>(await w.SendAsync(w.Admin, HttpMethod.Get, Routes.OrgAnnouncementCapabilities));
        var teacher = await w.ReadAsync<AnnouncementCapabilitiesDto>(await w.SendAsync(w.TeacherA, HttpMethod.Get, Routes.OrgAnnouncementCapabilities));
        var student = await w.ReadAsync<AnnouncementCapabilitiesDto>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.OrgAnnouncementCapabilities));

        Assert.Equal((true, true, true), (admin.CanAuthorUnitAnnouncements, admin.CanAuthorClassCourseAnnouncements, admin.CanMarkUrgent));
        Assert.Equal((false, true, false), (teacher.CanAuthorUnitAnnouncements, teacher.CanAuthorClassCourseAnnouncements, teacher.CanMarkUrgent));
        Assert.Equal((false, false, false), (student.CanAuthorUnitAnnouncements, student.CanAuthorClassCourseAnnouncements, student.CanMarkUrgent));
    }

    [Fact]
    public async Task Announcements_of_client_B_are_invisible_in_client_A()
    {
        using var a = await ForumTestWorld.CreateAsync(factory, "AA11A");
        using var b = await ForumTestWorld.CreateAsync(factory, "AA11B");
        var made = await CreateOkAsync(b, b.Admin, Request(RootUnit(b), [Org()]));

        var peek = await a.SendAsync(a.Admin, HttpMethod.Get, Routes.OrgAnnouncement(made.Id));
        var list = await a.ReadAsync<PagedResult<AnnouncementListItemDto>>(await a.SendAsync(a.Admin, HttpMethod.Get, Routes.OrgAnnouncements));
        var foreignTarget = await a.SendAsync(a.Admin, HttpMethod.Post, Routes.OrgAnnouncements, Request(RootUnit(a), [Cc(b.ClassCourseA)]));
        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.World.Tree.Org.Id, a.World.Tree.Root.Path));

        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        Assert.Empty(list.Items);
        Assert.Equal(HttpStatusCode.NotFound, foreignTarget.StatusCode);
        Assert.Empty(await db.Set<Announcement>().ToListAsync());
        Assert.Empty(await db.Set<AnnouncementTarget>().ToListAsync());
    }
}
