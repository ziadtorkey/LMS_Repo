using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Forums;

/// <summary>
/// The opening post of a thread or a reply. <see cref="BodyHtml"/> is already sanitised (D48). Posts are not audited row by
/// row (volume); moderation is audited on the thread.
/// </summary>
public sealed class ForumPost : UnitOwnedEntity
{
    private ForumPost()
    {
        BodyHtml = string.Empty;
    }

    public ForumPost(ForumThread thread, Guid authorUserId, string bodyHtml, bool isOpeningPost)
    {
        PlaceLike(thread);
        ThreadId = thread.Id;
        ClassCourseId = thread.ClassCourseId;
        AuthorUserId = authorUserId;
        BodyHtml = bodyHtml;
        IsOpeningPost = isOpeningPost;
    }

    public Guid ThreadId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string BodyHtml { get; private set; }
    public bool IsOpeningPost { get; private set; }
    public DateTime? EditedAtUtc { get; private set; }

    public void Edit(string bodyHtml, DateTime nowUtc)
    {
        BodyHtml = bodyHtml;
        EditedAtUtc = nowUtc;
    }
}

/// <summary>An asset attached to a post (max 5 per post).</summary>
public sealed class ForumPostAttachment : UnitOwnedEntity
{
    private ForumPostAttachment()
    {
    }

    public ForumPostAttachment(ForumPost post, Guid assetId)
    {
        PlaceLike(post);
        PostId = post.Id;
        ClassCourseId = post.ClassCourseId;
        AssetId = assetId;
    }

    public Guid PostId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid AssetId { get; private set; }
}
