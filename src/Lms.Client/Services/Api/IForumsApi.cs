using Lms.Shared;
using Lms.Shared.Forums;

namespace Lms.Client.Services.Api;

/// <summary>The forum and announcements of a ClassCourse (FR-FOR-01).</summary>
public interface IForumsApi
{
    Task<PagedResult<ForumThreadDto>> ListThreadsAsync(Guid classCourseId, CancellationToken ct = default);
    Task<ForumThreadDetailDto> CreateThreadAsync(Guid classCourseId, CreateForumThreadRequest request, CancellationToken ct = default);
    Task<ForumThreadDetailDto> GetThreadAsync(Guid threadId, CancellationToken ct = default);
    Task<PagedResult<ForumPostDto>> ListPostsAsync(Guid threadId, CancellationToken ct = default);
    Task<ForumPostDto> CreatePostAsync(Guid threadId, CreateForumPostRequest request, CancellationToken ct = default);
    Task<ForumPostDto> UpdatePostAsync(Guid postId, UpdateForumPostRequest request, CancellationToken ct = default);

    /// <summary>Deleting the opening post deletes the thread.</summary>
    Task DeletePostAsync(Guid postId, CancellationToken ct = default);

    /// <summary>PUT /api/forum-threads/{id}/moderation — pin and lock.</summary>
    Task<ForumThreadDto> ModerateAsync(Guid threadId, ModerateForumThreadRequest request, CancellationToken ct = default);

    Task<PagedResult<CourseAnnouncementDto>> ListAnnouncementsAsync(Guid classCourseId, CancellationToken ct = default);
    Task<CourseAnnouncementDto> CreateAnnouncementAsync(Guid classCourseId, CreateCourseAnnouncementRequest request, CancellationToken ct = default);
    Task<CourseAnnouncementDto> UpdateAnnouncementAsync(Guid id, UpdateCourseAnnouncementRequest request, CancellationToken ct = default);
    Task DeleteAnnouncementAsync(Guid id, CancellationToken ct = default);
}

public sealed class ForumsApi(ApiHttp http) : IForumsApi
{
    private static string Paged(string route) => $"{route}?pageSize={Paging.MaxPageSize}";

    public Task<PagedResult<ForumThreadDto>> ListThreadsAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ForumThreadDto>>(Paged(Routes.ClassCourseForumThreads(classCourseId)), ct);

    public async Task<ForumThreadDetailDto> CreateThreadAsync(Guid classCourseId, CreateForumThreadRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ForumThreadDetailDto>(HttpMethod.Post, Routes.ClassCourseForumThreads(classCourseId), request, ct))!;

    public Task<ForumThreadDetailDto> GetThreadAsync(Guid threadId, CancellationToken ct = default) =>
        http.GetAsync<ForumThreadDetailDto>(Routes.ForumThread(threadId), ct);

    public Task<PagedResult<ForumPostDto>> ListPostsAsync(Guid threadId, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<ForumPostDto>>(Paged(Routes.ForumThreadPosts(threadId)), ct);

    public async Task<ForumPostDto> CreatePostAsync(Guid threadId, CreateForumPostRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ForumPostDto>(HttpMethod.Post, Routes.ForumThreadPosts(threadId), request, ct))!;

    public async Task<ForumPostDto> UpdatePostAsync(Guid postId, UpdateForumPostRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ForumPostDto>(HttpMethod.Put, Routes.ForumPost(postId), request, ct))!;

    public Task DeletePostAsync(Guid postId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.ForumPost(postId), null, ct);

    public async Task<ForumThreadDto> ModerateAsync(Guid threadId, ModerateForumThreadRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ForumThreadDto>(HttpMethod.Put, Routes.ForumThreadModeration(threadId), request, ct))!;

    public Task<PagedResult<CourseAnnouncementDto>> ListAnnouncementsAsync(Guid classCourseId, CancellationToken ct = default) =>
        http.GetAsync<PagedResult<CourseAnnouncementDto>>(Paged(Routes.ClassCourseAnnouncements(classCourseId)), ct);

    public async Task<CourseAnnouncementDto> CreateAnnouncementAsync(Guid classCourseId, CreateCourseAnnouncementRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CourseAnnouncementDto>(HttpMethod.Post, Routes.ClassCourseAnnouncements(classCourseId), request, ct))!;

    public async Task<CourseAnnouncementDto> UpdateAnnouncementAsync(Guid id, UpdateCourseAnnouncementRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CourseAnnouncementDto>(HttpMethod.Put, Routes.Announcement(id), request, ct))!;

    public Task DeleteAnnouncementAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Announcement(id), null, ct);
}

/// <summary>In-memory forum for the dev server and tests. <see cref="Moderator"/> and <see cref="CanPost"/> drive the capability flags.</summary>
public sealed class FakeForumsApi : IForumsApi
{
    private readonly Guid _me = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    public bool Moderator { get; set; }
    public bool CanPost { get; set; } = true;
    public List<ForumThreadDto> Threads { get; } = [];
    public Dictionary<Guid, List<ForumPostDto>> Posts { get; } = [];
    public List<CourseAnnouncementDto> Announcements { get; } = [];
    public List<CreateCourseAnnouncementRequest> AnnouncementRequests { get; } = [];
    public List<CreateForumPostRequest> PostRequests { get; } = [];
    public Exception? Failure { get; set; }

    public ForumThreadDto AddThread(Guid classCourseId, string title, bool pinned = false, bool locked = false, DateTime? activity = null)
    {
        var at = activity ?? DateTime.UtcNow;
        var t = new ForumThreadDto(Guid.NewGuid(), classCourseId, title, _me, "كاتب", "Author", pinned, locked, 0, at, at);
        Threads.Add(t);
        Posts[t.Id] = [new ForumPostDto(Guid.NewGuid(), t.Id, _me, "كاتب", "Author", "<p>" + title + "</p>", [], true, true, true, at, null)];
        return t;
    }

    private static PagedResult<T> Page<T>(IReadOnlyList<T> items) => new(items, items.Count, 1, Paging.MaxPageSize);

    private Exception? Fail => Failure;

    public Task<PagedResult<ForumThreadDto>> ListThreadsAsync(Guid classCourseId, CancellationToken ct = default) =>
        Fail is { } f ? Task.FromException<PagedResult<ForumThreadDto>>(f) : Task.FromResult(Page<ForumThreadDto>(Threads.Where(t => t.ClassCourseId == classCourseId).ToList()));

    public Task<ForumThreadDetailDto> CreateThreadAsync(Guid classCourseId, CreateForumThreadRequest request, CancellationToken ct = default)
    {
        var t = AddThread(classCourseId, request.Title);
        Posts[t.Id][0] = Posts[t.Id][0] with { BodyHtml = request.BodyHtml };
        return GetThreadAsync(t.Id, ct);
    }

    public Task<ForumThreadDetailDto> GetThreadAsync(Guid threadId, CancellationToken ct = default)
    {
        if (Fail is { } f) return Task.FromException<ForumThreadDetailDto>(f);
        var t = Threads.FirstOrDefault(x => x.Id == threadId) ?? throw new ApiProblemException(404, ProblemCodes.not_found, null, null);
        return Task.FromResult(new ForumThreadDetailDto(t, Posts[threadId][0], CanPost && !t.IsLocked, Moderator));
    }

    public Task<PagedResult<ForumPostDto>> ListPostsAsync(Guid threadId, CancellationToken ct = default) =>
        Task.FromResult(Page<ForumPostDto>(Posts[threadId].Where(p => !p.IsOpeningPost).ToList()));

    public Task<ForumPostDto> CreatePostAsync(Guid threadId, CreateForumPostRequest request, CancellationToken ct = default)
    {
        if (Fail is { } f) return Task.FromException<ForumPostDto>(f);
        PostRequests.Add(request);
        var post = new ForumPostDto(Guid.NewGuid(), threadId, _me, "كاتب", "Author", request.BodyHtml, [], false, true, true, DateTime.UtcNow, null);
        Posts[threadId].Add(post);
        var i = Threads.FindIndex(t => t.Id == threadId);
        Threads[i] = Threads[i] with { ReplyCount = Threads[i].ReplyCount + 1 };
        return Task.FromResult(post);
    }

    public Task<ForumPostDto> UpdatePostAsync(Guid postId, UpdateForumPostRequest request, CancellationToken ct = default)
    {
        foreach (var list in Posts.Values)
        {
            var i = list.FindIndex(p => p.Id == postId);
            if (i >= 0) { list[i] = list[i] with { BodyHtml = request.BodyHtml, EditedAtUtc = DateTime.UtcNow }; return Task.FromResult(list[i]); }
        }
        throw new ApiProblemException(404, ProblemCodes.not_found, null, null);
    }

    public Task DeletePostAsync(Guid postId, CancellationToken ct = default)
    {
        foreach (var (threadId, list) in Posts)
        {
            var post = list.FirstOrDefault(p => p.Id == postId);
            if (post is null) continue;
            if (post.IsOpeningPost) { Threads.RemoveAll(t => t.Id == threadId); Posts.Remove(threadId); }
            else
            {
                list.Remove(post);
                var i = Threads.FindIndex(t => t.Id == threadId);
                Threads[i] = Threads[i] with { ReplyCount = Math.Max(0, Threads[i].ReplyCount - 1) };
            }
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    public Task<ForumThreadDto> ModerateAsync(Guid threadId, ModerateForumThreadRequest request, CancellationToken ct = default)
    {
        var i = Threads.FindIndex(t => t.Id == threadId);
        Threads[i] = Threads[i] with { IsPinned = request.IsPinned, IsLocked = request.IsLocked };
        return Task.FromResult(Threads[i]);
    }

    public Task<PagedResult<CourseAnnouncementDto>> ListAnnouncementsAsync(Guid classCourseId, CancellationToken ct = default) =>
        Fail is { } f ? Task.FromException<PagedResult<CourseAnnouncementDto>>(f) : Task.FromResult(Page<CourseAnnouncementDto>(Announcements.Where(a => a.ClassCourseId == classCourseId).ToList()));

    public Task<CourseAnnouncementDto> CreateAnnouncementAsync(Guid classCourseId, CreateCourseAnnouncementRequest request, CancellationToken ct = default)
    {
        AnnouncementRequests.Add(request);
        var a = new CourseAnnouncementDto(Guid.NewGuid(), classCourseId, request.Title, request.BodyHtml, request.Pinned, _me, "كاتب", "Author", DateTime.UtcNow, null);
        Announcements.Add(a);
        return Task.FromResult(a);
    }

    public Task<CourseAnnouncementDto> UpdateAnnouncementAsync(Guid id, UpdateCourseAnnouncementRequest request, CancellationToken ct = default)
    {
        var i = Announcements.FindIndex(a => a.Id == id);
        Announcements[i] = Announcements[i] with { Title = request.Title, BodyHtml = request.BodyHtml, IsPinned = request.Pinned, UpdatedAtUtc = DateTime.UtcNow };
        return Task.FromResult(Announcements[i]);
    }

    public Task DeleteAnnouncementAsync(Guid id, CancellationToken ct = default)
    {
        Announcements.RemoveAll(a => a.Id == id);
        return Task.CompletedTask;
    }
}
