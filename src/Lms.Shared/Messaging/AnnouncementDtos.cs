namespace Lms.Shared.Messaging;

/// <summary>One target of an announcement. <c>TargetId</c> is null for <c>Organization</c>; <c>IncludeSubtree</c> applies to <c>Unit</c> (and <c>Organization</c>).</summary>
public sealed record AnnouncementTargetDto(string Kind, Guid? TargetId, bool IncludeSubtree);

public sealed record AnnouncementAttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);

/// <summary>One cell of an announcement's delivery summary: how many deliveries on a channel (<c>NotificationChannels</c>) are in a status (<c>DeliveryStatuses</c>).</summary>
public sealed record AnnouncementDeliveryCountDto(string Channel, string Status, int Count);

/// <summary>What happened to the notifications of a published announcement, for its authors only (null before publishing or when <c>Notify</c> is off). Counts only, never recipients.</summary>
public sealed record AnnouncementDeliverySummaryDto(IReadOnlyList<AnnouncementDeliveryCountDto> Counts);

/// <summary>
/// POST <c>/api/comms/announcements/attachments</c> (multipart, <c>Announcements.Manage</c>): a file uploaded for a draft that is not saved yet; its <c>FileId</c> goes into <c>AttachmentFileIds</c>.
/// Same file rules as the assets, up to <c>Announcements.MaxAttachments</c> per announcement; 422 <c>attachment_invalid</c> otherwise.
/// A pending upload that no saved announcement uses within 24 hours is orphaned: the retention job of the announcement store (M6 Task 13) deletes the file and its row.
/// </summary>
public sealed record AnnouncementAttachmentUploadDto(Guid FileId, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// An announcement as its authors and admins see it (<c>Announcements.Manage</c> at the authoring unit; a Teacher only for own ClassCourses and their groups).
/// Bilingual: both titles are required, the body is Arabic and/or English. <c>IsUrgent</c> (D76, no SMS in v1): sent immediately, ignoring quiet hours and a schedule, pinned, on push <b>and</b> email. <c>AudienceCount</c> is the size of the recipient snapshot made at publish time
/// (null until published). <c>EditedAtUtc</c> marks a body edited after publishing.
/// </summary>
public sealed record AnnouncementDto(
    Guid Id,
    Guid AuthoringUnitId,
    string TitleAr,
    string TitleEn,
    string? BodyAr,
    string? BodyEn,
    string Status,
    IReadOnlyList<AnnouncementTargetDto> Targets,
    IReadOnlyList<string> RoleFilters,
    DateTime? PublishAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? ExpiresAtUtc,
    bool IsPinned,
    bool Notify,
    IReadOnlyList<AnnouncementAttachmentDto> Attachments,
    Guid AuthorUserId,
    string AuthorNameAr,
    string AuthorNameEn,
    int? AudienceCount,
    DateTime? EditedAtUtc,
    DateTime CreatedAtUtc,
    AnnouncementDeliverySummaryDto? DeliverySummary,
    DateTime ServerNowUtc,
    bool IsUrgent = false);

public sealed record AnnouncementListItemDto(
    Guid Id,
    string TitleAr,
    string TitleEn,
    string Status,
    bool IsPinned,
    DateTime? PublishAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? ExpiresAtUtc,
    int? AudienceCount,
    string AuthorNameAr,
    string AuthorNameEn,
    bool IsUrgent = false);

/// <summary>
/// POST <c>/api/comms/announcements</c>. <c>Publish</c> false saves a <c>Draft</c>; true publishes now (or schedules when <c>PublishAtUtc</c> is in the future).
/// A target the author may not reach is 404 (never 403, existence is not revealed); an invalid combination is 400 <c>announcement_target_invalid</c>.
/// </summary>
public sealed record CreateAnnouncementRequest(
    Guid AuthoringUnitId,
    string TitleAr,
    string TitleEn,
    string? BodyAr,
    string? BodyEn,
    IReadOnlyList<AnnouncementTargetDto> Targets,
    IReadOnlyList<string>? RoleFilters,
    DateTime? PublishAtUtc,
    DateTime? ExpiresAtUtc,
    bool IsPinned,
    bool Notify,
    IReadOnlyList<Guid>? AttachmentFileIds,
    bool Publish,
    bool IsUrgent = false);

/// <summary>
/// PUT <c>/api/comms/announcements/{id}</c>: everything while <c>Draft</c> or <c>Scheduled</c>; once <c>Published</c> only the expiry and the pin change and a body edit
/// leaves the visible "edited" marker (409 <c>announcement_not_editable</c> for the rest).
/// </summary>
public sealed record UpdateAnnouncementRequest(
    string TitleAr,
    string TitleEn,
    string? BodyAr,
    string? BodyEn,
    IReadOnlyList<AnnouncementTargetDto> Targets,
    IReadOnlyList<string>? RoleFilters,
    DateTime? PublishAtUtc,
    DateTime? ExpiresAtUtc,
    bool IsPinned,
    bool Notify,
    IReadOnlyList<Guid>? AttachmentFileIds,
    bool IsUrgent = false);

public sealed record AnnouncementQuery(string? Status = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

/// <summary>
/// What the caller may author (GET <c>/api/comms/announcements/capabilities</c>, <c>Announcements.View</c>): the client never infers it from <c>OrgUnits.View</c>.
/// <c>CanAuthorUnitAnnouncements</c> = <c>Announcements.Manage</c> at some unit and a target of kind Organization, Unit, Level or Class in scope (a Teacher: false, own
/// ClassCourses and their groups only); <c>CanAuthorClassCourseAnnouncements</c> = ClassCourse or Group targets of the caller's own class courses; <c>CanMarkUrgent</c> = may set <c>IsUrgent</c>.
/// </summary>
public sealed record AnnouncementCapabilitiesDto(bool CanAuthorUnitAnnouncements, bool CanAuthorClassCourseAnnouncements, bool CanMarkUrgent);

/// <summary>POST <c>/api/comms/announcements/audience-preview</c>: the count, the count per role chip and a sample of names (capped at <see cref="MessagingLimits.AudiencePreviewSampleMax"/>, inside the author's own scope) before publishing.</summary>
public sealed record AudiencePreviewRequest(
    Guid AuthoringUnitId, IReadOnlyList<AnnouncementTargetDto> Targets, IReadOnlyList<string>? RoleFilters);

public sealed record AudiencePreviewDto(int Count, IReadOnlyDictionary<string, int> CountsByChip, IReadOnlyList<string> SampleNames);

/// <summary>What a recipient sees (<c>Announcements.View</c>, own <c>AnnouncementRecipient</c> row): active ones, pinned first.</summary>
public sealed record MyAnnouncementDto(
    Guid Id,
    string TitleAr,
    string TitleEn,
    string? BodyAr,
    string? BodyEn,
    IReadOnlyList<AnnouncementAttachmentDto> Attachments,
    string AuthorNameAr,
    string AuthorNameEn,
    DateTime PublishedAtUtc,
    bool IsPinned,
    bool IsRead,
    DateTime? EditedAtUtc,
    bool IsUrgent = false,
    string Source = AnnouncementSources.Announcement);
