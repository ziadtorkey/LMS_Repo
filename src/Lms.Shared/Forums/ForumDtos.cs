using Lms.Shared.Content;

namespace Lms.Shared.Forums;

/// <summary>
/// A thread of a ClassCourse forum (FR-FOR-01; one implicit forum per ClassCourse). Lists put pinned threads first,
/// then the latest activity. Titles are user text in the author's language (1–200), not bilingual business names.
/// </summary>
public sealed record ForumThreadDto(
    Guid Id,
    Guid ClassCourseId,
    string Title,
    Guid AuthorUserId,
    string AuthorNameAr,
    string AuthorNameEn,
    bool IsPinned,
    bool IsLocked,
    int ReplyCount,
    DateTime CreatedAtUtc,
    DateTime LastActivityAtUtc);

/// <summary>
/// A post (the opening post of a thread or a reply). <c>BodyHtml</c> is sanitised on write (D48, max 200 000).
/// Attachments are assets of the ClassCourse. <c>CanEdit</c> = own post in an unlocked thread; <c>CanDelete</c> = own
/// post, or Forums.Moderate. A deleted post is not returned.
/// </summary>
public sealed record ForumPostDto(
    Guid Id,
    Guid ThreadId,
    Guid AuthorUserId,
    string AuthorNameAr,
    string AuthorNameEn,
    string BodyHtml,
    IReadOnlyList<AssetDto> Attachments,
    bool IsOpeningPost,
    bool CanEdit,
    bool CanDelete,
    DateTime CreatedAtUtc,
    DateTime? EditedAtUtc);

/// <summary>GET /api/forum-threads/{id} — the thread, its opening post and what the caller may do; replies are paged separately.</summary>
public sealed record ForumThreadDetailDto(
    ForumThreadDto Thread,
    ForumPostDto OpeningPost,
    bool CanReply,
    bool CanModerate);

/// <summary>POST /api/class-courses/{id}/forum/threads (Forums.Post, own ClassCourse). Attachments max 5.</summary>
public sealed record CreateForumThreadRequest(
    string Title,
    string BodyHtml,
    IReadOnlyList<Guid> AttachmentAssetIds);

/// <summary>POST /api/forum-threads/{id}/posts (Forums.Post; 409 <c>thread_locked</c> when locked). Attachments max 5.</summary>
public sealed record CreateForumPostRequest(
    string BodyHtml,
    IReadOnlyList<Guid> AttachmentAssetIds);

/// <summary>PUT /api/forum-posts/{id} — the author edits the body of an own post (409 <c>thread_locked</c> when locked).</summary>
public sealed record UpdateForumPostRequest(string BodyHtml);

/// <summary>PUT /api/forum-threads/{id}/moderation (Forums.Moderate) — pin and lock; deleting is DELETE on the thread or post.</summary>
public sealed record ModerateForumThreadRequest(bool IsPinned, bool IsLocked);

/// <summary>
/// A course announcement (FR-FOR-01): written by Forums.Moderate holders of the ClassCourse, read by everyone with
/// Forums.View on it; pinned first, then newest. <c>BodyHtml</c> sanitised on write. <c>Source</c> (M6): <c>CourseBoard</c> (default) for an
/// entry written on the board, <c>School</c> for a school-level announcement merged into the same list by GET <c>/api/class-courses/{id}/announcements</c>
/// (Task 14); a School entry is read-only on the course page.
/// </summary>
public sealed record CourseAnnouncementDto(
    Guid Id,
    Guid ClassCourseId,
    string Title,
    string BodyHtml,
    bool IsPinned,
    Guid AuthorUserId,
    string AuthorNameAr,
    string AuthorNameEn,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string Source = Lms.Shared.Messaging.AnnouncementSources.CourseBoard);

/// <summary>
/// POST /api/class-courses/{id}/announcements. Title 1–200, body max 200 000. <c>Notify</c> sends
/// <c>Forums.Announcement</c> to the enrolled students once, after commit.
/// </summary>
public sealed record CreateCourseAnnouncementRequest(
    string Title,
    string BodyHtml,
    bool Pinned,
    bool Notify);

/// <summary>PUT /api/announcements/{id} (no new notification).</summary>
public sealed record UpdateCourseAnnouncementRequest(
    string Title,
    string BodyHtml,
    bool Pinned);
