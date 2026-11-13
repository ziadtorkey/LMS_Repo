using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Forums;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Relationship probes of the forums and announcements (M4a Task 13, D42), on the shared M3 callers: Student1 is enrolled in the
/// class course X only, TeacherA teaches X, TeacherB teaches Y. The main probe matrix has no student column, so the student side
/// lives here: a student reads and posts in the forum of an own class course, never moderates, and gets 404 for any other class
/// course; a guardian has no Forums.* permission; the supervisor only Forums.View.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4aForumRelationshipProbeTests(LmsApiFactory factory)
{
    private static CreateForumThreadRequest NewThread => new("Probe", "<p>probe</p>", []);

    private static async Task<ForumThreadDetailDto> ThreadOfAsync(M3Callers w, M3Caller author, Guid classCourse)
    {
        var response = await w.SendAsync(author, HttpMethod.Post, Routes.ClassCourseForumThreads(classCourse), NewThread);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ForumThreadDetailDto>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
    }

    [Fact]
    public async Task Student_reads_and_posts_in_an_own_class_course_but_cannot_moderate()
    {
        var w = await M3Callers.GetAsync(factory);
        var thread = await ThreadOfAsync(w, M3Caller.TeacherA, w.Data.ClassCourseX.Id);

        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(M3Caller.Student1, HttpMethod.Get, Routes.ClassCourseForumThreads(w.Data.ClassCourseX.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(M3Caller.Student1, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(M3Caller.Student1, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.Data.ClassCourseX.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await w.SendAsync(M3Caller.Student1, HttpMethod.Post, Routes.ClassCourseForumThreads(w.Data.ClassCourseX.Id), NewThread)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await w.SendAsync(M3Caller.Student1, HttpMethod.Post, Routes.ForumThreadPosts(thread.Thread.Id), new CreateForumPostRequest("<p>hi</p>", []))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.Student1, HttpMethod.Put, Routes.ForumThreadModeration(thread.Thread.Id), new ModerateForumThreadRequest(true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.Student1, HttpMethod.Delete, Routes.ForumThread(thread.Thread.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.Student1, HttpMethod.Post, Routes.ClassCourseAnnouncements(w.Data.ClassCourseX.Id),
            new CreateCourseAnnouncementRequest("t", "<p>x</p>", false, false))).StatusCode);
    }

    [Fact]
    public async Task Student_gets_404_for_the_forum_of_a_class_course_they_are_not_enrolled_in()
    {
        var w = await M3Callers.GetAsync(factory);
        var thread = await ThreadOfAsync(w, M3Caller.TeacherB, w.Data.ClassCourseY.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.Student1, HttpMethod.Get, Routes.ClassCourseForumThreads(w.Data.ClassCourseY.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.Student1, HttpMethod.Post, Routes.ClassCourseForumThreads(w.Data.ClassCourseY.Id), NewThread)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.Student1, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.Student1, HttpMethod.Get, Routes.ForumThreadPosts(thread.Thread.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.Student1, HttpMethod.Post, Routes.ForumThreadPosts(thread.Thread.Id), new CreateForumPostRequest("<p>x</p>", []))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.Student1, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.Data.ClassCourseY.Id))).StatusCode);
    }

    [Fact]
    public async Task Teacher_of_another_class_course_gets_404_guardian_is_refused_and_supervisor_only_reads()
    {
        var w = await M3Callers.GetAsync(factory);
        var thread = await ThreadOfAsync(w, M3Caller.TeacherA, w.Data.ClassCourseX.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.TeacherB, HttpMethod.Get, Routes.ClassCourseForumThreads(w.Data.ClassCourseX.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.TeacherB, HttpMethod.Put, Routes.ForumThreadModeration(thread.Thread.Id), new ModerateForumThreadRequest(true, true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.SendAsync(M3Caller.TeacherB, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.Data.ClassCourseX.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.Guardian1, HttpMethod.Get, Routes.ClassCourseForumThreads(w.Data.ClassCourseX.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.Guardian1, HttpMethod.Get, Routes.ForumThread(thread.Thread.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.Guardian1, HttpMethod.Post, Routes.ClassCourseForumThreads(w.Data.ClassCourseX.Id), NewThread)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.Guardian1, HttpMethod.Get, Routes.ClassCourseAnnouncements(w.Data.ClassCourseX.Id))).StatusCode);

        // The built-in Supervisor holds Forums.View only (contract, not relationship-limited): it reads, never posts or moderates.
        Assert.Equal(HttpStatusCode.OK, (await w.SendAsync(M3Caller.SupervisorOfCx, HttpMethod.Get, Routes.ClassCourseForumThreads(w.Data.ClassCourseX.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.SupervisorOfCx, HttpMethod.Post, Routes.ClassCourseForumThreads(w.Data.ClassCourseX.Id), NewThread)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(M3Caller.SupervisorOfCx, HttpMethod.Put, Routes.ForumThreadModeration(thread.Thread.Id), new ModerateForumThreadRequest(true, true))).StatusCode);
    }
}
