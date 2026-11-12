using System.Net;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Forums;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Forums;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Forums;

[Collection(ApiCollection.Name)]
public sealed class ForumTests(LmsApiFactory factory)
{
    private static CreateForumThreadRequest NewThread(string title = "Question", string body = "<p>Hello</p>", params Guid[] assets) =>
        new(title, body, assets);

    private static async Task<ForumThreadDetailDto> StartAsync(ForumTestWorld w, RoleAssignment who, Guid classCourse, CreateForumThreadRequest? request = null)
    {
        var response = await w.SendAsync(who, HttpMethod.Post, Routes.ClassCourseForumThreads(classCourse), request ?? NewThread());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await w.ReadAsync<ForumThreadDetailDto>(response);
    }

    private static Task<HttpResponseMessage> ReplyAsync(ForumTestWorld w, RoleAssignment who, Guid threadId, string body = "<p>reply</p>") =>
        w.SendAsync(who, HttpMethod.Post, Routes.ForumThreadPosts(threadId), new CreateForumPostRequest(body, []));

    [Fact]
    public async Task Student_starts_a_thread_and_a_classmate_and_the_teacher_reply()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR1");

        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);
        var reply = await ReplyAsync(w, w.StudentA2, thread.Thread.Id, "<p>Me too</p>");
        var teacher = await ReplyAsync(w, w.TeacherA, thread.Thread.Id, "<p>Answer</p>");

        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        Assert.Equal(HttpStatusCode.Created, teacher.StatusCode);
        Assert.True(thread.CanReply);
        Assert.False(thread.CanModerate);
        Assert.True(thread.OpeningPost.IsOpeningPost);
        var detail = await w.ReadAsync<ForumThreadDetailDto>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id)));
        Assert.Equal(2, detail.Thread.ReplyCount);
        var posts = await w.ReadAsync<PagedResult<ForumPostDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ForumThreadPosts(thread.Thread.Id)));
        Assert.Equal(2, posts.Total);
        Assert.All(posts.Items, p => Assert.False(p.IsOpeningPost));
        Assert.All(posts.Items, p => Assert.False(p.CanDelete)); // the student wrote neither reply
    }

    [Fact]
    public async Task Other_class_course_student_and_teacher_get_404_everywhere()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR2");
        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);

        foreach (var outsider in new[] { w.StudentB, w.TeacherB })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(outsider, HttpMethod.Get, Routes.ClassCourseForumThreads(w.ClassCourseA))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(outsider, HttpMethod.Post, Routes.ClassCourseForumThreads(w.ClassCourseA), NewThread())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(outsider, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(outsider, HttpMethod.Get, Routes.ForumThreadPosts(thread.Thread.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ReplyAsync(w, outsider, thread.Thread.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(outsider, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.ClassCourseA))).StatusCode);
        }
    }

    [Fact]
    public async Task Locked_thread_refuses_replies_and_edits()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR3");
        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);
        var own = await w.ReadAsync<ForumPostDto>(await ReplyAsync(w, w.StudentA, thread.Thread.Id, "<p>one</p>"));

        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(w.TeacherA, HttpMethod.Put, Routes.ForumThreadModeration(thread.Thread.Id), new ModerateForumThreadRequest(false, true))).StatusCode);

        var reply = await ReplyAsync(w, w.StudentA2, thread.Thread.Id);
        var edit = await w.SendAsync(w.StudentA, HttpMethod.Put, Routes.ForumPost(own.Id), new UpdateForumPostRequest("<p>changed</p>"));
        Assert.Equal((HttpStatusCode.Conflict, "thread_locked"), (reply.StatusCode, await ForumTestWorld.CodeAsync(reply)));
        Assert.Equal((HttpStatusCode.Conflict, "thread_locked"), (edit.StatusCode, await ForumTestWorld.CodeAsync(edit)));
        var detail = await w.ReadAsync<ForumThreadDetailDto>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id)));
        Assert.False(detail.CanReply);
    }

    [Fact]
    public async Task Pinned_first_then_latest_activity()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR4");
        var first = await StartAsync(w, w.StudentA, w.ClassCourseA, NewThread("first"));
        var second = await StartAsync(w, w.StudentA, w.ClassCourseA, NewThread("second"));
        var third = await StartAsync(w, w.StudentA, w.ClassCourseA, NewThread("third"));
        await w.SendAsync(w.TeacherA, HttpMethod.Put, Routes.ForumThreadModeration(first.Thread.Id), new ModerateForumThreadRequest(true, false));
        await ReplyAsync(w, w.StudentA2, second.Thread.Id, "<p>bump</p>"); // newest activity

        var list = await w.ReadAsync<PagedResult<ForumThreadDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseForumThreads(w.ClassCourseA)));

        Assert.Equal([first.Thread.Id, second.Thread.Id, third.Thread.Id], list.Items.Select(t => t.Id));
        Assert.True(list.Items[0].IsPinned);
    }

    [Fact]
    public async Task Pinned_threads_are_ordered_before_paging_so_an_old_pinned_thread_is_on_page_one()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR4P");
        var oldest = await StartAsync(w, w.StudentA, w.ClassCourseA, NewThread("oldest, pinned"));
        var ids = new List<Guid>();
        foreach (var title in new[] { "t1", "t2", "t3", "t4" })
        {
            ids.Add((await StartAsync(w, w.StudentA, w.ClassCourseA, NewThread(title))).Thread.Id);
        }

        await w.SendAsync(w.TeacherA, HttpMethod.Put, Routes.ForumThreadModeration(oldest.Thread.Id), new ModerateForumThreadRequest(true, false));

        async Task<List<Guid>> PageAsync(int page) =>
            (await w.ReadAsync<PagedResult<ForumThreadDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseForumThreads(w.ClassCourseA) + $"?page={page}&pageSize=2")))
            .Items.Select(t => t.Id).ToList();

        // newest first after the pinned one: t4, t3 | t2, t1
        Assert.Equal([oldest.Thread.Id, ids[3]], await PageAsync(1));
        Assert.Equal([ids[2], ids[1]], await PageAsync(2));
        Assert.Equal([ids[0]], await PageAsync(3));
    }

    [Fact]
    public async Task Student_cannot_moderate_and_cannot_edit_or_delete_others_posts()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR5");
        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);
        var theirs = await w.ReadAsync<ForumPostDto>(await ReplyAsync(w, w.StudentA2, thread.Thread.Id, "<p>mine</p>"));

        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentA, HttpMethod.Put, Routes.ForumThreadModeration(thread.Thread.Id), new ModerateForumThreadRequest(true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentA2, HttpMethod.Delete, Routes.ForumThread(thread.Thread.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentA, HttpMethod.Put, Routes.ForumPost(theirs.Id), new UpdateForumPostRequest("<p>x</p>"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(w.StudentA, HttpMethod.Delete, Routes.ForumPost(theirs.Id))).StatusCode);
    }

    [Fact]
    public async Task Moderator_deletes_a_post_and_a_thread_and_they_disappear()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR6");
        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);
        var reply = await w.ReadAsync<ForumPostDto>(await ReplyAsync(w, w.StudentA2, thread.Thread.Id, "<p>spam</p>"));

        Assert.Equal(HttpStatusCode.NoContent, (await w.SendAsync(w.TeacherA, HttpMethod.Delete, Routes.ForumPost(reply.Id))).StatusCode);
        var posts = await w.ReadAsync<PagedResult<ForumPostDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ForumThreadPosts(thread.Thread.Id)));
        Assert.Empty(posts.Items);

        Assert.Equal(HttpStatusCode.NoContent, (await w.SendAsync(w.TeacherA, HttpMethod.Delete, Routes.ForumThread(thread.Thread.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id))).StatusCode);
        var list = await w.ReadAsync<PagedResult<ForumThreadDto>>(await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ClassCourseForumThreads(w.ClassCourseA)));
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task Author_edits_and_deletes_own_post()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR7");
        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);
        var reply = await w.ReadAsync<ForumPostDto>(await ReplyAsync(w, w.StudentA2, thread.Thread.Id, "<p>v1</p>"));
        Assert.True(reply.CanEdit);
        Assert.True(reply.CanDelete);

        var edited = await w.ReadAsync<ForumPostDto>(await w.SendAsync(w.StudentA2, HttpMethod.Put, Routes.ForumPost(reply.Id), new UpdateForumPostRequest("<p>v2</p>")));

        Assert.Equal("<p>v2</p>", edited.BodyHtml);
        Assert.NotNull(edited.EditedAtUtc);
        Assert.Equal(HttpStatusCode.NoContent, (await w.SendAsync(w.StudentA2, HttpMethod.Delete, Routes.ForumPost(reply.Id))).StatusCode);
    }

    [Fact]
    public async Task Post_html_is_sanitised_on_write()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR8");

        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA,
            NewThread("xss", "<p onclick=\"x()\">hi</p><script>alert(1)</script><a href=\"javascript:alert(1)\">l</a>"));
        var reply = await w.ReadAsync<ForumPostDto>(await ReplyAsync(w, w.StudentA2, thread.Thread.Id, "<img src=x onerror=alert(1)><p>ok</p>"));

        Assert.DoesNotContain("script", thread.OpeningPost.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", thread.OpeningPost.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", thread.OpeningPost.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", reply.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>ok</p>", reply.BodyHtml);
    }

    [Fact]
    public async Task Invalid_input_is_400()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR9");
        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);
        var url = Routes.ClassCourseForumThreads(w.ClassCourseA);
        var tooMany = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

        foreach (var bad in new[]
        {
            NewThread(""), NewThread(new string('t', 201)), NewThread("t", ""), NewThread("t", "<p></p>"), NewThread("t", "<p>x</p>", tooMany),
            NewThread("t", new string('a', 200_001)),
        })
        {
            var response = await w.SendAsync(w.StudentA, HttpMethod.Post, url, bad);
            Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (response.StatusCode, await ForumTestWorld.CodeAsync(response)));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await ReplyAsync(w, w.StudentA, thread.Thread.Id, "")).StatusCode);
    }

    [Fact]
    public async Task Attachments_must_be_assets_of_the_same_class_course()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR10");
        var mine = await w.AddAssetAsync(w.World.CcA);
        var other = await w.AddAssetAsync(w.World.CcB);

        var ok = await StartAsync(w, w.StudentA, w.ClassCourseA, NewThread("with file", "<p>see</p>", mine));
        var bad = await w.SendAsync(w.StudentA, HttpMethod.Post, Routes.ClassCourseForumThreads(w.ClassCourseA), NewThread("t", "<p>x</p>", other));
        var ghost = await w.SendAsync(w.StudentA, HttpMethod.Post, Routes.ClassCourseForumThreads(w.ClassCourseA), NewThread("t", "<p>x</p>", Guid.NewGuid()));

        Assert.Equal([mine], ok.OpeningPost.Attachments.Select(a => a.Id));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ghost.StatusCode);
    }

    [Fact]
    public async Task Attachment_of_a_post_is_readable_by_the_class_and_not_by_another_class_course()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR11");
        var asset = await w.AddAssetAsync(w.World.CcA);
        await StartAsync(w, w.StudentA, w.ClassCourseA, NewThread("with file", "<p>see</p>", asset));

        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(w.StudentA2, HttpMethod.Get, Routes.Asset(asset))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(w.StudentB, HttpMethod.Get, Routes.Asset(asset))).StatusCode);
    }

    [Fact]
    public async Task Closed_class_refuses_forum_writes_but_reads_stay_open()
    {
        using var w = await ForumTestWorld.CreateAsync(factory, "FR12");
        var thread = await StartAsync(w, w.StudentA, w.ClassCourseA);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.World.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var write = await ReplyAsync(w, w.StudentA, thread.Thread.Id);

        Assert.Equal((HttpStatusCode.Conflict, "class_closed"), (write.StatusCode, await ForumTestWorld.CodeAsync(write)));
        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(w.StudentA, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id))).StatusCode);
    }

    [Fact]
    public Task ForumThread_is_isolated_per_client_and_subtree() => AssertIsolatedAsync(0, db => db.Set<ForumThread>().Select(x => x.Id));

    [Fact]
    public Task ForumPost_is_isolated_per_client_and_subtree() => AssertIsolatedAsync(1, db => db.Set<ForumPost>().Select(x => x.Id));

    [Fact]
    public Task ForumPostAttachment_is_isolated_per_client_and_subtree() => AssertIsolatedAsync(2, db => db.Set<ForumPostAttachment>().Select(x => x.Id));

    [Fact]
    public Task CourseAnnouncement_is_isolated_per_client_and_subtree() => AssertIsolatedAsync(3, db => db.Set<CourseAnnouncement>().Select(x => x.Id));

    /// <summary>One row of the table in School 1 and School 2 of client A and in client B: each scope sees exactly its subtree.</summary>
    private async Task AssertIsolatedAsync(int index, Func<LmsDbContext, IQueryable<Guid>> table)
    {
        var a = await AccessTestData.SeedAsync(factory, "FI" + index + "A");
        var b = await AccessTestData.SeedAsync(factory, "FI" + index + "B");
        var inSchool1 = (await AddForumRowsAsync(a.Root, a.School1))[index];
        var inSchool2 = (await AddForumRowsAsync(a.Root, a.School2))[index];
        var otherClient = (await AddForumRowsAsync(b.Root, b.School1))[index];
        Guid[] all = [inSchool1, inSchool2, otherClient];

        async Task<List<Guid>> VisibleAsync(TestCurrentContext who)
        {
            await using var db = factory.CreateDbContext(who);
            return (await table(db).ToListAsync()).Where(all.Contains).Order().ToList();
        }

        Assert.Equal(new[] { inSchool1, inSchool2 }.Order().ToList(), await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path)));
        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path)));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path)));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path)));
    }

    /// <summary>One thread, post, attachment and announcement in the unit; returns their ids in that order.</summary>
    private async Task<Guid[]> AddForumRowsAsync(Lms.Domain.Organizations.OrgUnit root, Lms.Domain.Organizations.OrgUnit unit)
    {
        var year = await AcademicTestKit.AddYearAsync(factory, root);
        var cls = await AcademicTestKit.AddClassAsync(factory, unit, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, unit);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        var author = await PeopleTestKit.AddUserAsync(factory);
        var assetId = await ForumTestWorld.AddAssetAsync(factory, classCourse);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var thread = new ForumThread(classCourse, "t", author.Id, DateTime.UtcNow);
        db.Add(thread);
        await db.SaveChangesAsync();
        var post = new ForumPost(thread, author.Id, "<p>x</p>", true);
        db.Add(post);
        await db.SaveChangesAsync();
        var attachment = new ForumPostAttachment(post, assetId);
        var announcement = new CourseAnnouncement(classCourse, "a", "<p>x</p>", false, author.Id);
        db.AddRange(attachment, announcement);
        await db.SaveChangesAsync();
        return [thread.Id, post.Id, attachment.Id, announcement.Id];
    }
}
