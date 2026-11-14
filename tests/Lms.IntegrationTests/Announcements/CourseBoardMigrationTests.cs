using Lms.Domain.Announcements;
using Lms.Domain.Forums;
using Lms.Infrastructure.Announcements;
using Lms.IntegrationTests.Forums;
using Lms.IntegrationTests.Infrastructure;
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
}
