using Lms.Domain.Announcements;
using Lms.Domain.Classes;
using Lms.Domain.Forums;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Forums;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Forums and announcements (M4a Task 13, #363): everything is reached through a class course at School1, so School 2 and the
/// other client get 404. The Teacher holds Forums.* only for class courses they teach and the probe's class course has no teacher
/// assignment, so every row answers 404 for the Teacher (the relationship rule, D42). Editing a post is the author's alone: the
/// seeded post has another author, so admins get 403 on it (they may still delete it as moderators).
/// </summary>
public sealed class ForumsProbeRows : IProbeRowModule
{
    private static readonly CreateForumThreadRequest NewThread = new("Probe thread", "<p>probe</p>", []);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("forum threads create", HttpMethod.Post, Routes.ClassCourseForumThreadsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseForumThreads((await NewAsync(w)).ClassCourse.Id), NewThread),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)) { Milestone = ProbeMilestones.M4a },
        new("forum threads list", HttpMethod.Get, Routes.ClassCourseForumThreadsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseForumThreads((await NewAsync(w)).ClassCourse.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("forum thread get", HttpMethod.Get, Routes.ForumThreadPattern,
            async (w, _) => new ProbeRequest(Routes.ForumThread((await NewAsync(w)).Thread.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("forum thread delete", HttpMethod.Delete, Routes.ForumThreadPattern,
            async (w, _) => new ProbeRequest(Routes.ForumThread((await NewAsync(w)).Thread.Id)),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)) { Milestone = ProbeMilestones.M4a },
        new("forum thread moderation", HttpMethod.Put, Routes.ForumThreadModerationPattern,
            async (w, _) => new ProbeRequest(Routes.ForumThreadModeration((await NewAsync(w)).Thread.Id), new ModerateForumThreadRequest(true, false)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("forum thread posts list", HttpMethod.Get, Routes.ForumThreadPostsPattern,
            async (w, _) => new ProbeRequest(Routes.ForumThreadPosts((await NewAsync(w)).Thread.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("forum thread reply", HttpMethod.Post, Routes.ForumThreadPostsPattern,
            async (w, _) => new ProbeRequest(Routes.ForumThreadPosts((await NewAsync(w)).Thread.Id), new CreateForumPostRequest("<p>reply</p>", [])),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)) { Milestone = ProbeMilestones.M4a },
        new("forum post edit (not the author)", HttpMethod.Put, Routes.ForumPostPattern,
            async (w, _) => new ProbeRequest(Routes.ForumPost((await NewAsync(w)).Reply.Id), new UpdateForumPostRequest("<p>edit</p>")),
            Expect(Forbidden, Forbidden, NotFound, NotFound, NotFound, Forbidden)) { Milestone = ProbeMilestones.M4a },
        new("forum post delete (moderator)", HttpMethod.Delete, Routes.ForumPostPattern,
            async (w, _) => new ProbeRequest(Routes.ForumPost((await NewAsync(w)).Reply.Id)),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)) { Milestone = ProbeMilestones.M4a },
        new("announcements list", HttpMethod.Get, Routes.ClassCourseAnnouncementsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseAnnouncements((await NewAsync(w)).ClassCourse.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("announcements create", HttpMethod.Post, Routes.ClassCourseAnnouncementsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseAnnouncements((await NewAsync(w)).ClassCourse.Id),
                new CreateCourseAnnouncementRequest("Probe", "<p>probe</p>", false, false)),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)) { Milestone = ProbeMilestones.M4a },
        new("announcements update", HttpMethod.Put, Routes.AnnouncementPattern,
            async (w, _) => new ProbeRequest(Routes.Announcement((await NewAsync(w)).Announcement.Id),
                new UpdateCourseAnnouncementRequest("Probe", "<p>probe</p>", false)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)) { Milestone = ProbeMilestones.M4a },
        new("announcements delete", HttpMethod.Delete, Routes.AnnouncementPattern,
            async (w, _) => new ProbeRequest(Routes.Announcement((await NewAsync(w)).Announcement.Id)),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)) { Milestone = ProbeMilestones.M4a },
    ];

    private sealed record Seeded(ClassCourse ClassCourse, ForumThread Thread, ForumPost Reply, Announcement Announcement);

    /// <summary>A class course at School1 with a thread (opening post and one reply by another author) and an announcement.</summary>
    private static async Task<Seeded> NewAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        var author = await PeopleTestKit.AddUserAsync(factory);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var thread = new ForumThread(classCourse, "Seed thread", author.Id, DateTime.UtcNow);
        db.Add(thread);
        await db.SaveChangesAsync();
        var opening = new ForumPost(thread, author.Id, "<p>seed</p>", true);
        var reply = new ForumPost(thread, author.Id, "<p>reply</p>", false);
        // The course board answers from the announcements store (M6 Task 17): a published announcement with one ClassCourse target.
        var unit = await db.OrgUnits.IgnoreQueryFilters().SingleAsync(u => u.Id == classCourse.OrgUnitId);
        var announcement = new Announcement(unit, author.Id);
        announcement.SetContent("Seed", "Seed", "<p>seed</p>", "<p>seed</p>", [AnnouncementRoleFilters.Staff, AnnouncementRoleFilters.Students], null, null, false, false, false);
        announcement.MarkPublished(DateTime.UtcNow, 0);
        db.AddRange(opening, reply, announcement, new AnnouncementTarget(announcement, AnnouncementTargetKinds.ClassCourse, classCourse.Id, false));
        await db.SaveChangesAsync();
        return new Seeded(classCourse, thread, reply, announcement);
    }
}
