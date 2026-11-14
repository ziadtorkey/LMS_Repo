using System.Net;
using Lms.Domain.Announcements;
using Lms.Domain.Forums;
using Lms.Infrastructure.Announcements;
using Lms.IntegrationTests.Forums;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Forums;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Announcements;

/// <summary>M6 Task 17 (L15, site 9): the course board becomes announcements with the same ids, per client, and can be written back.</summary>
[Collection(ApiCollection.Name)]
public sealed class CourseBoardMigrationTests(LmsApiFactory factory)
{
    private async Task<CourseAnnouncement> AddBoardPostAsync(Guid classCourseId, string title, string html, bool pinned, Guid author)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().SingleAsync(c => c.Id == classCourseId);
        var post = new CourseAnnouncement(classCourse, title, html, pinned, author);
        db.Add(post);
        await db.SaveChangesAsync();
        return post;
    }

    private async Task RunAsync(IReadOnlyList<string> statements)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        foreach (var sql in statements)
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }

    private async Task<(Announcement? Announcement, List<AnnouncementTarget> Targets)> LoadAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<Announcement>().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id),
            await db.Set<AnnouncementTarget>().IgnoreQueryFilters().AsNoTracking().Where(t => t.AnnouncementId == id).ToListAsync());
    }

    [Fact]
    public async Task Board_rows_become_class_course_announcements_with_both_titles_and_the_same_id()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "CB1");
        var post = await AddBoardPostAsync(w.ClassCourseA, "واجب الأسبوع", "<p>اقرأوا الفصل الثالث</p>", true, w.UserIdOf(w.TeacherA));

        await RunAsync(CourseBoardMigration.Up);

        var (announcement, targets) = await LoadAsync(post.Id);
        Assert.NotNull(announcement);
        Assert.Equal(("واجب الأسبوع", "واجب الأسبوع"), (announcement.TitleAr, announcement.TitleEn)); // one title, copied to both languages
        Assert.Equal(("<p>اقرأوا الفصل الثالث</p>", "<p>اقرأوا الفصل الثالث</p>"), (announcement.BodyAr, announcement.BodyEn));
        Assert.Equal(AnnouncementStates.Published, announcement.Status);
        Assert.True(announcement.IsPinned);
        Assert.False(announcement.Notify); // a board post never notifies again
        Assert.Equal([AnnouncementRoleFilters.Staff, AnnouncementRoleFilters.Students], announcement.RoleFilters); // Guardians chip off
        Assert.Equal(w.UserIdOf(w.TeacherA), announcement.AuthorUserId);
        Assert.Equal((post.OrganizationId, post.OrgUnitId, post.OrgUnitPath), (announcement.OrganizationId, announcement.OrgUnitId, announcement.OrgUnitPath));
        var target = Assert.Single(targets);
        Assert.Equal((AnnouncementTargetKinds.ClassCourse, w.ClassCourseA, false), (target.Kind, target.TargetId, target.IncludeSubtree));
    }

    [Fact]
    public async Task Migration_is_idempotent_and_keeps_a_deleted_post_deleted()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "CB2");
        var kept = await AddBoardPostAsync(w.ClassCourseA, "باقٍ", "<p>a</p>", false, w.UserIdOf(w.TeacherA));
        var gone = await AddBoardPostAsync(w.ClassCourseA, "محذوف", "<p>b</p>", false, w.UserIdOf(w.TeacherA));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<CourseAnnouncement>().SingleAsync(c => c.Id == gone.Id));
            await db.SaveChangesAsync();
        }

        await RunAsync(CourseBoardMigration.Up);
        await RunAsync(CourseBoardMigration.Up); // twice: nothing doubles

        Assert.Single((await LoadAsync(kept.Id)).Targets);
        var (deleted, deletedTargets) = await LoadAsync(gone.Id);
        Assert.True(deleted!.IsDeleted);
        Assert.Single(deletedTargets);
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await check.Set<Announcement>().IgnoreQueryFilters().CountAsync(a => a.Id == kept.Id));
    }

    [Fact]
    public async Task Board_migration_keeps_each_client_separate()
    {
        using var a = await ForumTestWorld.CreateAsync(factory, "CB3A");
        using var b = await ForumTestWorld.CreateAsync(factory, "CB3B");
        var postA = await AddBoardPostAsync(a.ClassCourseA, "A", "<p>a</p>", false, a.UserIdOf(a.TeacherA));
        var postB = await AddBoardPostAsync(b.ClassCourseA, "B", "<p>b</p>", false, b.UserIdOf(b.TeacherA));

        await RunAsync(CourseBoardMigration.Up);

        var (annA, targetsA) = await LoadAsync(postA.Id);
        var (annB, targetsB) = await LoadAsync(postB.Id);
        Assert.Equal(a.World.Tree.Org.Id, annA!.OrganizationId);
        Assert.Equal(b.World.Tree.Org.Id, annB!.OrganizationId);
        Assert.NotEqual(annA.OrganizationId, annB.OrganizationId);
        Assert.Equal(a.ClassCourseA, Assert.Single(targetsA).TargetId);
        Assert.Equal(b.ClassCourseA, Assert.Single(targetsB).TargetId); // each client post targets its own class course only
    }

    [Fact]
    public async Task Migration_down_restores_the_board_including_posts_written_after_the_move()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "CB4");
        var old = await AddBoardPostAsync(w.ClassCourseA, "قديم", "<p>old</p>", false, w.UserIdOf(w.TeacherA));
        await RunAsync(CourseBoardMigration.Up);

        // After the move: the old post is edited and pinned in the new store, and a new post is written there (one ClassCourse target).
        Guid fresh;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var announcement = await db.Set<Announcement>().IgnoreQueryFilters().SingleAsync(x => x.Id == old.Id);
            announcement.EditPublished("<p>جديد</p>", "<p>new</p>", null, true, DateTime.UtcNow);
            var unit = await db.OrgUnits.IgnoreQueryFilters().SingleAsync(u => u.Id == old.OrgUnitId);
            var created = new Announcement(unit, w.UserIdOf(w.TeacherA));
            created.SetContent("عنوان", "Fresh", "<p>x</p>", "<p>y</p>", [AnnouncementRoleFilters.Students], null, null, false, false, false);
            created.MarkPublished(DateTime.UtcNow, 0);
            db.Add(created);
            db.Add(new AnnouncementTarget(created, AnnouncementTargetKinds.ClassCourse, w.ClassCourseA, false));
            await db.SaveChangesAsync();
            fresh = created.Id;
        }

        await RunAsync(CourseBoardMigration.Down);

        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var restored = await check.Set<CourseAnnouncement>().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == old.Id);
        Assert.Equal(("<p>new</p>", true), (restored.BodyHtml, restored.IsPinned)); // the edit and the pin come back
        var added = await check.Set<CourseAnnouncement>().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == fresh);
        Assert.Equal((w.ClassCourseA, "Fresh"), (added.ClassCourseId, added.Title));
    }

    [Fact]
    public async Task New_board_posts_are_announcements_with_guardians_off_and_a_recipient_snapshot()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "CB5");

        var response = await w.SendAsync(w.TeacherA, HttpMethod.Post, Routes.ClassCourseAnnouncements(w.ClassCourseA), new CreateCourseAnnouncementRequest("Quiz Monday", "<p>Bring a pen</p>", true, false));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await w.ReadAsync<CourseAnnouncementDto>(response);
        Assert.Equal((AnnouncementSources.CourseBoard, "Quiz Monday", true), (dto.Source, dto.Title, dto.IsPinned));
        var (announcement, targets) = await LoadAsync(dto.Id);
        Assert.Equal(AnnouncementStates.Published, announcement!.Status);
        Assert.Equal([AnnouncementRoleFilters.Staff, AnnouncementRoleFilters.Students], announcement.RoleFilters);
        Assert.False(announcement.Notify);
        Assert.Equal(w.ClassCourseA, Assert.Single(targets).TargetId);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var recipients = await db.Set<AnnouncementRecipient>().Where(r => r.AnnouncementId == dto.Id).ToListAsync();
        Assert.Contains(w.UserIdOf(w.StudentA), recipients.Select(r => r.UserId)); // the enrolled student reads it in the feed
        Assert.DoesNotContain(w.UserIdOf(w.StudentB), recipients.Select(r => r.UserId)); // not a student of the other class course
    }

    [Fact]
    public async Task Board_endpoints_answer_unchanged_for_a_migrated_post_by_its_old_id()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "CB6");
        var post = await AddBoardPostAsync(w.ClassCourseA, "Old news", "<p>old</p>", false, w.UserIdOf(w.TeacherA));
        await RunAsync(CourseBoardMigration.Up);

        var list = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));
        Assert.Equal(post.Id, Assert.Single(list.Items).Id);
        Assert.Equal(AnnouncementSources.CourseBoard, list.Items[0].Source);

        var edited = await w.SendAsync(w.TeacherA, HttpMethod.Put, Routes.Announcement(post.Id), new UpdateCourseAnnouncementRequest("New news", "<p>new</p>", true));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var dto = await w.ReadAsync<CourseAnnouncementDto>(edited);
        Assert.Equal(("New news", "<p>new</p>", true), (dto.Title, dto.BodyHtml, dto.IsPinned));
        Assert.Equal(HttpStatusCode.NoContent, (await w.SendAsync(w.TeacherA, HttpMethod.Delete, Routes.Announcement(post.Id))).StatusCode);
        var after = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));
        Assert.Empty(after.Items);
    }

    [Fact]
    public async Task An_announcement_with_several_targets_is_listed_as_school_and_is_not_editable_on_the_board()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "CB7");
        Guid id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().SingleAsync(c => c.Id == w.ClassCourseA);
            var unit = await db.OrgUnits.IgnoreQueryFilters().SingleAsync(u => u.Id == classCourse.OrgUnitId);
            var wide = new Announcement(unit, w.UserIdOf(w.Admin));
            wide.SetContent("عام", "Wide", "<p>x</p>", "<p>y</p>", [AnnouncementRoleFilters.Students], null, null, false, false, false);
            wide.MarkPublished(DateTime.UtcNow, 0);
            db.Add(wide);
            db.Add(new AnnouncementTarget(wide, AnnouncementTargetKinds.ClassCourse, w.ClassCourseA, false));
            db.Add(new AnnouncementTarget(wide, AnnouncementTargetKinds.ClassCourse, w.ClassCourseB, false));
            await db.SaveChangesAsync();
            id = wide.Id;
        }

        var list = await w.ReadAsync<PagedResult<CourseAnnouncementDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA)));
        Assert.Equal(AnnouncementSources.Announcement, Assert.Single(list.Items).Source);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.TeacherA, HttpMethod.Put, Routes.Announcement(id), new UpdateCourseAnnouncementRequest("x", "<p>x</p>", false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.TeacherA, HttpMethod.Delete, Routes.Announcement(id))).StatusCode);
    }
}
