namespace Lms.Shared.Messaging;

/// <summary>A person in a conversation. <c>Left</c> = left the group (history stays visible to the others).</summary>
public sealed record ParticipantDto(Guid UserId, string NameAr, string NameEn, string? RoleKey, bool Left);

/// <summary>
/// A conversation (<c>Messaging.Send</c>; only a participant reads it, an Org Admin who is not a participant gets 404). <c>IsLocked</c> = a moderator locked it
/// (409 <c>conversation_closed</c> on send). A direct conversation between the same two people is returned by <c>POST</c>, not duplicated (200 with the existing one).
/// </summary>
public sealed record ConversationDto(
    Guid Id,
    string Kind,
    string? Title,
    IReadOnlyList<ParticipantDto> Participants,
    bool IsLocked,
    bool Muted,
    DateTime CreatedAtUtc);

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
    bool IsLocked);

public sealed record MessageAttachmentDto(Guid FileId, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// One message. <c>Body</c> is null for a tombstone (<see cref="MessageStates.Deleted"/>) and for a removed one (<see cref="MessageStates.Removed"/>); only
/// moderators read the original of a removed message, through the report. <c>CanDelete</c> = the caller sent it and is inside the delete window.
/// A user whose account was deleted or expired shows a neutral name ("Former user") and <c>SenderUserId</c> null.
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
    bool CanDelete);

/// <summary>POST <c>/api/conversations</c>: a direct one (one other user) or a group (two or more others, at most <c>Messaging.MaxGroupSize</c>); staff create groups. The rules matrix is checked for every participant (403 <c>messaging_not_allowed</c>).</summary>
public sealed record CreateConversationRequest(string Kind, IReadOnlyList<Guid> ParticipantUserIds, string? Title, string? FirstMessage);

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

// --- moderation (FR-MSG-04)

/// <summary>POST <c>/api/messages/{id}/report</c> (a participant who is not the sender; 409 <c>report_duplicate</c> for the same reporter and message).</summary>
public sealed record ReportMessageRequest(string Reason, string? Note);

/// <summary>
/// A report as a moderator sees it (<c>Messaging.Moderate</c>, scoped by the report's units): the reported message with its original text and a bounded
/// context window (<see cref="MessagingLimits.ReportContextMessages"/>); a moderator never reads the rest of the conversation.
/// </summary>
public sealed record MessageReportDto(
    Guid Id,
    Guid MessageId,
    Guid ConversationId,
    string Reason,
    string? Note,
    string Status,
    Guid ReporterUserId,
    Guid ReportedUserId,
    string ReportedUserNameAr,
    string ReportedUserNameEn,
    string? ReportedMessageOriginalBody,
    IReadOnlyList<MessageDto> Context,
    DateTime CreatedAtUtc,
    string? Action,
    string? ResolutionNote,
    DateTime? ResolvedAtUtc);

public sealed record ReportQuery(string? Status = null, int Page = 1, int PageSize = Paging.DefaultPageSize);

/// <summary>POST <c>/api/moderation/reports/{id}/resolve</c>: <c>RestrictUntilUtc</c> is required for the restricting actions and must lie in the future.</summary>
public sealed record ResolveReportRequest(string Action, string? Note, DateTime? RestrictUntilUtc);

/// <summary>A user's messaging right taken away until a date (never a block: blocks belong to their owners).</summary>
public sealed record RestrictionDto(Guid UserId, string NameAr, string NameEn, DateTime UntilUtc, string? Reason, DateTime CreatedAtUtc);

/// <summary>GET <c>/api/moderation/stats</c>: outside the caller's scope the numbers are zero, never 404.</summary>
public sealed record ModerationStatsDto(int OpenReports, int ResolvedLast30Days, int ActiveRestrictions);
