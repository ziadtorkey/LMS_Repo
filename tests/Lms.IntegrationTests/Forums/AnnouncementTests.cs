using System.Net;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Forums;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Forums;

[Collection(ApiCollection.Name)]
public sealed class AnnouncementTests(LmsApiFactory factory)
{
    private static CreateCourseAnnouncementRequest NewAnnouncement(
        string title = "Exam moved", string body = "<p>Monday</p>", bool pinned = false, bool notify = false) => new(title, body, pinned, notify);

    private static async Task<CourseAnnouncementDto> CreateAsync(
        ForumTestWorld w, Lms.Domain.Access.RoleAssignment who, Guid classCourse, CreateCourseAnnouncementRequest? request = null)
    {
        var response = await w.SendAsync(who, HttpMethod.Post, Routes.ClassCourseAnnouncements(classCourse), request ?? NewAnnouncement());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await w.ReadAsync<CourseAnnouncementDto>(response);
    }

    private async Task<int> NotificationCountAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().CountAsync(n => n.UserId == userId && n.EventKey == NotificationEvents.ForumsAnnouncement);
    }

    [Fact]
    public async Task Teacher_posts_and_students_read_pinned_first_then_newest()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AN1");
        var older = await CreateAsync(w, w.TeacherA, w.ClassCourseA, NewAnnouncement("older"));
        var pinned = await CreateAsync(w, w.TeacherA, w.ClassCourseA, NewAnnouncement("pinned", pinned: true));
        var newer = await CreateAsync(w, w.Admin, w.ClassCourseA, NewAnnouncement("newer"));

        var list = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));

        Assert.Equal([pinned.Id, newer.Id, older.Id], list.Items.Select(a => a.Id));
        Assert.Equal(3, list.Total);
    }

    [Fact]
    public async Task Student_cannot_write_and_other_class_course_gets_404()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AN2");
        var announcement = await CreateAsync(w, w.TeacherA, w.ClassCourseA);

        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentA, HttpMethod.Post, Routes.ClassCourseAnnouncements(w.ClassCourseA), NewAnnouncement())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentA, HttpMethod.Put, Routes.Announcement(announcement.Id), new UpdateCourseAnnouncementRequest("x", "<p>x</p>", false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentA, HttpMethod.Delete, Routes.Announcement(announcement.Id))).StatusCode);
        // a teacher of another class course reaches nothing here; a student of another class course lacks the permission first
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.TeacherB, HttpMethod.Post, Routes.ClassCourseAnnouncements(w.ClassCourseA), NewAnnouncement())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.TeacherB, HttpMethod.Put, Routes.Announcement(announcement.Id), new UpdateCourseAnnouncementRequest("x", "<p>x</p>", false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.TeacherB, HttpMethod.Delete, Routes.Announcement(announcement.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentB, HttpMethod.Post, Routes.ClassCourseAnnouncements(w.ClassCourseA), NewAnnouncement())).StatusCode);
    }

    [Fact]
    public async Task Announcement_notifies_enrolled_students_once()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AN3");
        var quiet = await CreateAsync(w, w.TeacherA, w.ClassCourseA, NewAnnouncement("quiet", notify: false));
        Assert.Equal(0, await NotificationCountAsync(w.StudentA.UserId));

        var loud = await CreateAsync(w, w.TeacherA, w.ClassCourseA, NewAnnouncement("loud", notify: true));
        var edit = await w.SendAsync(w.TeacherA, HttpMethod.Put, Routes.Announcement(loud.Id), new UpdateCourseAnnouncementRequest("louder", "<p>x</p>", true));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(1, await NotificationCountAsync(w.StudentA.UserId));
        Assert.Equal(1, await NotificationCountAsync(w.StudentA2.UserId));
        Assert.Equal(0, await NotificationCountAsync(w.StudentB.UserId)); // not enrolled in this class course
        Assert.Equal(0, await NotificationCountAsync(w.TeacherA.UserId));
        Assert.NotEqual(quiet.Id, loud.Id);
    }

    [Fact]
    public async Task Announcement_notification_deep_links_to_the_course_announcements_page()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AN7");
        await CreateAsync(w, w.TeacherA, w.ClassCourseA, NewAnnouncement("link me", notify: true));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var notification = await db.Set<Notification>().SingleAsync(n => n.UserId == w.StudentA.UserId && n.EventKey == NotificationEvents.ForumsAnnouncement);

        // The client page /class-courses/{id}/announcements mirrors the API route without its /api prefix.
        Assert.Equal($"/class-courses/{w.ClassCourseA}/announcements", notification.DeepLink);
    }

    [Fact]
    public async Task Announcement_html_is_sanitised_on_write()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AN4");

        var created = await CreateAsync(w, w.TeacherA, w.ClassCourseA,
            NewAnnouncement(body: "<p>hi</p><script>alert(1)</script><iframe src=\"https://x\"></iframe><a href=\"javascript:x\">l</a>"));
        var updated = await w.ReadAsync<CourseAnnouncementDto>(await w.SendAsync(w.TeacherA, HttpMethod.Put, Routes.Announcement(created.Id),
            new UpdateCourseAnnouncementRequest("t", "<p onclick=\"x()\">ok</p>", false)));

        foreach (var html in new[] { created.BodyHtml, updated.BodyHtml })
        {
            Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("iframe", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        }

        Assert.NotNull(updated.UpdatedAtUtc);
    }

    [Fact]
    public async Task Invalid_input_is_400_and_delete_removes_the_announcement()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "AN5");
        foreach (var bad in new[] { NewAnnouncement(""), NewAnnouncement(new string('t', 201)), NewAnnouncement(body: ""), NewAnnouncement(body: new string('a', 200_001)) })
        {
            var response = await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.ClassCourseAnnouncements(w.ClassCourseA), bad);
            Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (response.StatusCode, await ForumTestWorld.CodeAsync(response)));
        }

        var created = await CreateAsync(w, w.TeacherA, w.ClassCourseA);
        Assert.Equal(HttpStatusCode.NoContent, (await w.SendAsync(w.TeacherA, HttpMethod.Delete, Routes.Announcement(created.Id))).StatusCode);
        var list = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));
        Assert.Empty(list.Items);
    }
}
