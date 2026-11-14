using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>The outcome of an upload or a bind: the attachment, or the problem code and status.</summary>
public sealed record AttachmentResult(MessageAttachment? Attachment, string? ProblemCode, int Status, int? RetryAfterSeconds = null)
{
    public static AttachmentResult Ok(MessageAttachment attachment) => new(attachment, null, 201);

    public static AttachmentResult Refused(string code, int status, int? retryAfter = null) => new(null, code, status, retryAfter);
}

/// <summary>
/// Attachments of messages under D65 and L12: at most <see cref="MessagingLimits.MaxAttachmentsPerMessage"/> files of at most <see cref="MessagingLimits.MaxAttachmentMb"/> MB, an
/// allow-list that leaves out every active type (HTML, SVG, XML, JS, archives and macro-enabled Office formats), checks at <b>attach time</b> (the uploader is a participant who may
/// post right now), pending rows bound to their uploader and one conversation, and an hourly upload cap.
/// </summary>
public sealed class MessageAttachmentGuard(LmsDbContext db, IFileStorage storage, ICurrentContext context, IClock clock, IMessagingPolicy policy)
{
    public const int UploadsPerHour = 30;

    /// <summary>The M4a asset allow-list minus the active types and archives (L12).</summary>
    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".pptx", ".xlsx", ".jpg", ".jpeg", ".png", ".webp", ".gif", ".txt", ".mp3", ".m4a", ".mp4", ".ogg", ".wav", ".webm",
    };

    /// <summary>Only raster images preview inline; everything else downloads (never an in-app viewer).</summary>
    public static readonly IReadOnlySet<string> InlinePreviewExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    public async Task<AttachmentResult> UploadAsync(ConversationAccessResult access, string fileName, long length, Stream content, CancellationToken ct)
    {
        var (conversation, me) = (access.Conversation, access.Me);
        if (me.HasLeft)
        {
            return AttachmentResult.Refused(ProblemCodes.messaging_not_allowed, 403);
        }

        if (conversation.IsLocked)
        {
            return AttachmentResult.Refused(ProblemCodes.conversation_closed, 409);
        }

        if (conversation.IsReadOnly || !(await policy.CanPostAsync(me.UserId, conversation, ct)).Allowed)
        {
            return AttachmentResult.Refused(ProblemCodes.messaging_not_allowed, 403); // a person who may not post here may not upload here (checked now, not at create time)
        }

        var name = Path.GetFileName(fileName);
        if (name.Length == 0 || length <= 0 || !AllowedExtensions.Contains(Path.GetExtension(name)))
        {
            return AttachmentResult.Refused(ProblemCodes.validation_failed, 400);
        }

        if (length > MessagingLimits.MaxAttachmentMb * 1024L * 1024L)
        {
            return AttachmentResult.Refused(ProblemCodes.validation_failed, 413);
        }

        var now = clock.UtcNow;
        var since = now.AddHours(-1);
        if (await db.Set<MessageAttachment>().CountAsync(a => a.UploaderUserId == me.UserId && a.CreatedAtUtc >= since, ct) >= UploadsPerHour)
        {
            return AttachmentResult.Refused(ProblemCodes.rate_limited, 429, 3600);
        }

        var unitId = await db.OrgUnits.Where(u => u.Path == context.ScopePath).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (unitId is null)
        {
            return AttachmentResult.Refused(ProblemCodes.messaging_not_allowed, 403);
        }

        try
        {
            if (name.Length > 255)
            {
                name = name[^255..];
            }

            var stored = await storage.UploadAsync(new FileUploadRequest(name, string.Empty, length, unitId.Value), content, ct);
            var attachment = new MessageAttachment(conversation, me.UserId, stored.Id, stored.FileName, stored.ContentType, stored.SizeBytes, now);
            db.Add(attachment);
            await db.SaveChangesAsync(ct);
            return AttachmentResult.Ok(attachment);
        }
        catch (FileTypeNotAllowedException)
        {
            return AttachmentResult.Refused(ProblemCodes.validation_failed, 400);
        }
        catch (FileTooLargeException)
        {
            return AttachmentResult.Refused(ProblemCodes.validation_failed, 413);
        }
    }

    /// <summary>
    /// The pending attachments of the caller in this conversation for the given file ids. A file that is not the caller own pending upload of this conversation is a 404,
    /// never a hint that it exists elsewhere.
    /// </summary>
    public async Task<(List<MessageAttachment>? Attachments, AttachmentResult? Refused)> ResolvePendingAsync(ConversationAccessResult access, IReadOnlyList<Guid>? fileIds, CancellationToken ct)
    {
        var ids = fileIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return ([], null);
        }

        if (ids.Count > MessagingLimits.MaxAttachmentsPerMessage)
        {
            return (null, AttachmentResult.Refused(ProblemCodes.validation_failed, 400));
        }

        var rows = await db.Set<MessageAttachment>().Where(a => ids.Contains(a.FileId)).ToListAsync(ct);
        var ok = rows.Count == ids.Count && rows.All(a => a.IsPending && a.UploaderUserId == access.Me.UserId && a.ConversationId == access.Conversation.Id);
        return ok ? (rows, null) : (null, AttachmentResult.Refused("not_found", 404));
    }
}
