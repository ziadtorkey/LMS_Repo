namespace Lms.Shared.Messaging;

/// <summary>A person in a conversation. <c>Left</c> = left the group (history stays visible to the others).</summary>
public sealed record ParticipantDto(Guid UserId, string NameAr, string NameEn, string? RoleKey, bool Left);

/// <summary>
/// A conversation (<c>Messaging.View</c>; only a participant reads it, an Org Admin who is not a participant gets 404). <c>IsLocked</c> = a moderator locked it
/// (409 <c>conversation_closed</c> on send). A direct conversation between the same two people is returned by <c>POST</c>, not duplicated (200 with the existing one).
/// </summary>
public sealed record ConversationDto(
    Guid Id,
    string Kind,
    string? Title,
    IReadOnlyList<ParticipantDto> Participants,
    bool IsLocked,
    bool Muted,
    DateTime CreatedAtUtc,
    bool IsReadOnly = false,
    Guid? OwnerUserId = null,
    Guid? ClassCourseId = null);

/// <summary>A row of the inbox: the unread count and the last message (text is cut, never a tombstone's original).</summary>
public sealed record ConversationListItemDto(
    Guid Id,
    string Kind,
    string? Title,
    IReadOnlyList<ParticipantDto> Participants,
    int UnreadCount,
    string? LastMessagePreview,
    DateTime? LastMessageAtUtc,
    bool Muted,
    bool IsLocked,
    bool IsReadOnly = false);

public sealed record MessageAttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// One message. <c>Body</c> is null for a tombstone (<see cref="MessageStates.Deleted"/>) and for a removed one (<see cref="MessageStates.Removed"/>); only
/// moderators read the original of a removed message, through the report. <c>CanDelete</c> = the caller sent it and is inside the delete window.
/// A user whose account was deleted or expired shows a neutral name ("Former user") and <c>SenderUserId</c> null. <c>MyReportStatus</c> (<see cref="MyReportStatuses"/>) is set only for
/// the caller's own report of this message: pending or reviewed, never the action, and null for the sender and every other participant (L16).
/// </summary>
public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid? SenderUserId,
    string SenderNameAr,
    string SenderNameEn,
    string? Body,
    string State,
    IReadOnlyList<MessageAttachmentDto> Attachments,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc,
    bool CanDelete,
    string? MyReportStatus = null);

/// <summary>POST <c>/api/conversations</c>: a direct one (one other user) or a group (two or more others, at most <c>Messaging.MaxGroupSize</c>); only staff create groups. <c>ClassCourseId</c> makes it a class or course group a teacher of that class course created (own ClassCourses only); <b>students never post in a group</b> (D74): they read it and reply privately to the teacher; <b>guardians may post in a teacher-run group that includes guardians</b> (D77). The rules matrix is checked for every participant (403 <c>messaging_not_allowed</c>).</summary>
public sealed record CreateConversationRequest(string Kind, IReadOnlyList<Guid> ParticipantUserIds, string? Title, string? FirstMessage, Guid? ClassCourseId = null);

/// <summary>POST <c>…/participants</c>: members to add to a group (owner to member check for each).</summary>
public sealed record AddParticipantsRequest(IReadOnlyList<Guid> UserIds);

/// <summary>POST <c>…/messages</c>: text (1-<see cref="MessagingLimits.MaxBodyLength"/>) and up to <see cref="MessagingLimits.MaxAttachmentsPerMessage"/> uploaded files (same file rules as the assets).</summary>
public sealed record SendMessageRequest(string Body, IReadOnlyList<Guid>? AttachmentFileIds);

/// <summary>PUT <c>/api/conversations/{id}/mute</c>: <c>UntilUtc</c> null = until unmuted; a date must lie in the future (400 <c>validation_failed</c>).</summary>
public sealed record MuteConversationRequest(DateTime? UntilUtc);

/// <summary>GET <c>…/messages?beforeId=&amp;pageSize=</c>: keyset paging, newest first; <c>BeforeId</c> is the id of the oldest message already loaded.</summary>
public sealed record MessageQuery(Guid? BeforeId = null, int PageSize = MessagingLimits.MessagePageSize);

public sealed record MessagePageDto(IReadOnlyList<MessageDto> Items, Guid? NextBeforeId);

/// <summary>GET <c>/api/messages/search?q=&amp;conversationId=</c>: only the caller's conversations; at most <see cref="MessagingLimits.SearchWindow"/> hits per query, a conversation filter is required beyond page 1.</summary>
public sealed record MessageSearchResultDto(Guid MessageId, Guid ConversationId, string Snippet, string SenderNameAr, string SenderNameEn, DateTime CreatedAtUtc);

/// <summary>A block owned by the caller; no moderation route ever lists, creates or removes one (a block belongs to its owner).</summary>
public sealed record BlockDto(Guid UserId, string NameAr, string NameEn, DateTime CreatedAtUtc);

/// <summary>GET <c>/api/messaging/recipients?search=</c>: only users the caller may message (a user the rules deny never appears).</summary>
public sealed record RecipientDto(Guid UserId, string NameAr, string NameEn, string RoleKey, string? UnitNameAr, string? UnitNameEn);

/// <summary>GET <c>/api/messaging/rules-summary</c>: the roles the caller's role may reach under the client's rules (the hint line of the new-message dialog).</summary>
public sealed record MessagingRulesSummaryDto(IReadOnlyList<string> ReachableRoleKeys, bool CanCreateGroups);

/// <summary>GET <c>/api/messaging/limits</c>: what the composer needs to validate before sending.</summary>
public sealed record MessagingLimitsDto(
    int MaxBodyLength,
    int MaxAttachments,
    int MaxAttachmentMb,
    int MaxGroupSize,
    int DeleteWindowMinutes,
    int MaxPerMinute,
    bool ReadReceipts);

/// <summary>GET <c>/api/me/messaging/unread-total</c>: the number of unread <b>conversations</b> (the badge); the notification bell is unchanged.</summary>
public sealed record UnreadTotalDto(int Conversations);

/// <summary>The realtime event after a new message: <b>ids only, never text</b> (the client fetches the message through the participant-checked route).</summary>
public sealed record MessageReceivedEvent(Guid ConversationId, Guid MessageId);

/// <summary>One cell of the rules matrix: may a user with <c>FromRole</c> write to a user with <c>ToRole</c> (<see cref="MessagingRuleModes"/>).</summary>
public sealed record MessagingRuleCellDto(string FromRole, string ToRole, string Mode);

/// <summary>GET <c>/api/comms/messaging-rules</c> (<c>Comms.Manage</c>, client root): the client's matrix and the D69 defaults it may only narrow.</summary>
public sealed record MessagingRulesDto(IReadOnlyList<MessagingRuleCellDto> Cells, IReadOnlyList<MessagingRuleCellDto> Defaults);

/// <summary>PUT <c>/api/comms/messaging-rules</c>: the whole matrix. A cell looser than its default is 400 <c>rules_looser_than_default</c> (D69).</summary>
public sealed record SaveMessagingRulesRequest(IReadOnlyList<MessagingRuleCellDto> Cells);

// --- moderation (FR-MSG-04, D66, D70, L16)

/// <summary>POST <c>/api/messages/{id}/report</c> (a participant who is not the sender; 409 <c>report_duplicate</c> for the same reporter and message). <c>Note</c> is at most <see cref="MessagingLimits.ReportNoteMaxLength"/> characters.</summary>
public sealed record ReportMessageRequest(string Reason, string? Note);

/// <summary>One message of the frozen context of a report: text and attachment <b>names</b> only. Only the <b>reported</b> message (<c>IsReported</c>) also carries <see cref="Attachments"/>, the refs a moderator opens through <c>GET /api/moderation/reports/{id}/attachments/{fileId}</c>; any other file id is 404. No user id, no url.</summary>
public sealed record ReportContextMessageDto(
    Guid MessageId,
    string SenderNameAr,
    string SenderNameEn,
    string? Body,
    IReadOnlyList<string> AttachmentNames,
    DateTime CreatedAtUtc,
    bool IsReported,
    IReadOnlyList<ReportAttachmentRefDto>? Attachments = null);

/// <summary>An attachment of the reported message: the id the audited attachment route takes, with what the viewer needs before opening it.</summary>
public sealed record ReportAttachmentRefDto(Guid FileId, string Name, string ContentType, long SizeBytes);

/// <summary>Who sent the reported message, in numbers: the class is the sender's own class when they are a student; prior reports and removals count this client only.</summary>
public sealed record SenderSummaryDto(
    string NameAr,
    string NameEn,
    string RoleKey,
    string? ClassNameAr,
    string? ClassNameEn,
    int PriorReports,
    int PriorRemovals);

/// <summary>A row of the moderation queue (<c>Messaging.Moderate</c>; the Org Admin only, D70). <c>AgeHours</c> since the report; <c>IsSafety</c> = a safety concern, sorted first.</summary>
public sealed record MessageReportListItemDto(
    Guid Id,
    string Reason,
    string Status,
    string ReportedUserNameAr,
    string ReportedUserNameEn,
    string Snippet,
    DateTime CreatedAtUtc,
    int AgeHours,
    bool IsSafety);

/// <summary>
/// A report as a moderator sees it (<c>Messaging.Moderate</c>, scoped by the report's units): the <b>frozen</b> context (the reported message with
/// <see cref="MessagingLimits.ReportContextEachSide"/> messages before and as many after, text and attachment names), the reason and note, a summary of the sender and the status. A moderator never
/// reads the rest of the conversation and the report never names the reporter (D66). <c>ReviewDueUtc</c> = the safety review target (null for other reasons); <c>Action</c> and
/// <c>ResolutionNote</c> are set once resolved (the note is the moderator's own and never reaches the participants).
/// </summary>
public sealed record MessageReportDto(
    Guid Id,
    Guid MessageId,
    Guid ConversationId,
    string Reason,
    string? Note,
    string Status,
    IReadOnlyList<ReportContextMessageDto> Context,
    SenderSummaryDto SenderSummary,
    DateTime? ReviewDueUtc,
    string? Action,
    string? ResolutionNote,
    DateTime? ResolvedAtUtc,
    DateTime ServerNowUtc);

/// <summary>GET <c>/api/moderation/reports?status=&amp;reason=&amp;page=</c>: <c>Status</c> null = the pending ones (<see cref="ReportStatuses.Pending"/>); safety concerns first, then the oldest.</summary>
public sealed record MessageReportQuery(string? Status = null, string? Reason = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

/// <summary>
/// POST <c>/api/moderation/reports/{id}/resolve</c>: <c>DurationDays</c> (one of <see cref="RestrictionDurations"/>) is required for the restricting actions. <c>NoteToPeople</c> is the optional note the
/// sender reads in the warning or restriction notice (at most <see cref="ModerationLimits.NoteToPeopleMaxLength"/> characters); the moderator's private note is not a field: it
/// stays out of every notice.
/// </summary>
public sealed record ResolveReportRequest(string Action, int? DurationDays, string? NoteToPeople);

/// <summary>POST <c>/api/moderation/reports/bulk-dismiss</c>: dismiss up to <see cref="ModerationLimits.BulkDismissMax"/> reports at once (the UI says "Keep").</summary>
public sealed record BulkDismissRequest(IReadOnlyList<Guid> ReportIds, string? NoteToPeople);

/// <summary>A user whose messaging right is taken away until a date (never a block: blocks belong to their owners).</summary>
public sealed record RestrictionDto(Guid UserId, string NameAr, string NameEn, DateTime UntilUtc, string? Reason, DateTime CreatedAtUtc);

/// <summary>GET <c>/api/moderation/stats</c>: outside the caller's scope the numbers are zero, never 404. <c>MedianHoursToDecide</c> over the reports decided in the last 30 days.</summary>
public sealed record ModerationStatsDto(int Open, int NewToday, int Restricted, double MedianHoursToDecide);

/// <summary>GET <c>/api/moderation/reports/{id}/attachments/{fileId}</c>: a signed URL valid a few minutes for an attachment of the <b>reported</b> message only; every issue is audited.</summary>
public sealed record ModerationAttachmentUrlDto(string Url, DateTime ExpiresAtUtc, string FileName, string ContentType, bool InlinePreview);
