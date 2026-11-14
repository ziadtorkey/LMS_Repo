namespace Lms.Shared;

// M6 wave 1: messaging, announcements, moderation, notification delivery and the communication settings (#613 revision 4, section 9 is the oracle).
// The announcement store lives under /api/comms/announcements because /api/announcements is the M4a course board (CourseAnnouncement); the board migrates onto it in a later task (#613 section 7).
// A conversation is visible only to its participants (a non-participant, an Org Admin included, gets 404); a rule failure is 403 messaging_not_allowed;
// every list is paged and the realtime event carries ids only.
public static partial class Routes
{
    /// <summary>
    /// GET (the inbox) — <c>Messaging.View</c>; POST (<c>CreateConversationRequest</c> → <c>ConversationDto</c>; a direct conversation between the same pair answers 200 with the existing one,
    /// a new one 201) — <c>Messaging.Send</c>; the <c>Messaging</c> feature flag; the rules matrix per participant (owner to member, never pairwise): 403 <c>messaging_not_allowed</c>;
    /// only staff create a group (403 for a student or a guardian).
    /// </summary>
    public const string Conversations = "/api/conversations";

    /// <summary>GET → <c>ConversationDto</c> — <c>Messaging.View</c>; participant only (404 otherwise); muting and leaving are their own routes below.</summary>
    public const string ConversationPattern = Conversations + "/{id:guid}";

    /// <summary>GET <c>?beforeId=&amp;pageSize=</c> → <c>MessagePageDto</c> — <c>Messaging.View</c>; POST (<c>SendMessageRequest</c> → <c>MessageDto</c>) — <c>Messaging.Send</c>; participant who has not left; 403 restricted; 403 <c>group_read_only</c> for a student in any group (D74), for a guardian outside a teacher-run group that includes guardians (D77) and for anyone but staff when the group is read-only; 409 <c>conversation_closed</c>; 400 <c>message_too_long</c>; 429 <c>rate_limited</c>.</summary>
    public const string ConversationMessagesPattern = ConversationPattern + "/messages";

    /// <summary>DELETE — <c>Messaging.Send</c>: the sender deletes their own message for everyone within <c>MessagingLimits.DeleteWindowMinutes</c> (409 <c>message_delete_window_closed</c> after); there is no edit.</summary>
    public const string MessagePattern = ConversationMessagesPattern + "/{mid:guid}";

    /// <summary>PUT (<c>MuteConversationRequest</c> → <c>ConversationDto</c>) and DELETE — <c>Messaging.Send</c>; participant only (404 otherwise): mutes or unmutes the conversation for the caller (no new-message notice; the unread count still counts).</summary>
    public const string ConversationMutePattern = ConversationPattern + "/mute";

    /// <summary>POST (no body) — <c>Messaging.Send</c>; participant only: the caller leaves a group (history stays visible to the others); a direct conversation cannot be left (409 <c>conversation_closed</c>), it can be muted or blocked.</summary>
    public const string ConversationLeavePattern = ConversationPattern + "/leave";

    /// <summary>POST (<c>AddParticipantsRequest</c> → <c>ConversationDto</c>) — <c>Messaging.Send</c>; the group's owner only (404 for another participant); the rules matrix is checked <b>owner to member</b>, never pairwise; 403 <c>messaging_not_allowed</c>; a group never drops below three members (an admin or manager cannot make a private group of two with a student); at most <c>Messaging.MaxGroupSize</c>.</summary>
    public const string ConversationParticipantsPattern = ConversationPattern + "/participants";

    /// <summary>DELETE — <c>Messaging.Send</c>; the group's owner removes a member (the owner leaves with <c>…/leave</c>); 404 for another participant.</summary>
    public const string ConversationParticipantPattern = ConversationParticipantsPattern + "/{userId:guid}";

    /// <summary>PUT (no body → <c>ConversationDto</c>) and DELETE — <c>Messaging.Send</c> for the group's owner (a teacher), or <c>Messaging.Moderate</c> with <c>UnitAccess</c> at the conversation's unit for a moderator: sets or clears <c>IsReadOnly</c>, which is distinct from the moderator's lock (<c>IsLocked</c>, 409 <c>conversation_closed</c>). Only staff can post in a read-only group (D74: students never post in a group; D77: guardians post only in teacher-run groups that include guardians, and not when it is read-only).</summary>
    public const string ConversationReadOnlyPattern = ConversationPattern + "/read-only";

    /// <summary>POST (no body) — <c>Messaging.Send</c>: marks the conversation read up to its last message (<c>LastReadMessageId</c>).</summary>
    public const string ConversationReadPattern = ConversationPattern + "/read";

    /// <summary>POST (multipart, one file → <c>PendingAttachmentDto</c>, 201) — <c>Messaging.Send</c>; at attach time the caller is a participant who may post (404 non-participant; 403 left, restricted or read-only for them); <c>Messaging.MaxAttachmentMb</c> per file (413), the asset allow-list (422 <c>attachment_invalid</c>), at most <c>MessagingLimits.MaxAttachmentsPerMessage</c> pending, <c>Messaging.UploadsPerMinute</c> (429 <c>rate_limited</c>).</summary>
    public const string ConversationAttachmentsPattern = ConversationPattern + "/attachments";

    /// <summary>DELETE — <c>Messaging.Send</c>: the uploader removes their own pending attachment (404 for another user's or an attached one).</summary>
    public const string ConversationAttachmentPattern = ConversationAttachmentsPattern + "/{aid:guid}";

    /// <summary>GET → a signed, short-lived URL — <c>Messaging.View</c>; participants only (404 otherwise).</summary>
    public const string MessageAttachmentUrlPattern = MessagePattern + "/attachments/{fid:guid}/url";

    /// <summary>GET <c>?q=&amp;conversationId=&amp;page=</c> → <c>MessageSearchResultDto[]</c> — <c>Messaging.View</c>; only the caller's conversations; minimum 2 characters, 50 results, a 200-hit window, rate limited.</summary>
    public const string MessageSearch = "/api/messages/search";

    /// <summary>POST (<c>ReportMessageRequest</c>) — <c>Messaging.Send</c>; a participant who is not the sender; 404 others; 409 <c>report_duplicate</c>.</summary>
    public const string MessageReportPattern = "/api/messages/{id:guid}/report";

    /// <summary>GET → <c>BlockDto[]</c> — <c>Messaging.View</c>; the caller's own blocks (no moderation route reaches them).</summary>
    public const string MyBlocks = "/api/me/blocks";

    /// <summary>PUT and DELETE — <c>Messaging.Send</c>: block or unblock a user (direct messages stop both ways; groups are unchanged; the blocked person is never told).</summary>
    public const string MyBlockPattern = MyBlocks + "/{userId:guid}";

    /// <summary>GET <c>?search=</c> → <c>RecipientDto[]</c> — <c>Messaging.View</c>: only users the caller's role may message under the client's rules.</summary>
    public const string MessagingRecipients = "/api/messaging/recipients";

    /// <summary>GET → <c>MessagingRulesSummaryDto</c> — <c>Messaging.View</c>.</summary>
    public const string MessagingRulesSummary = "/api/messaging/rules-summary";

    /// <summary>GET → <c>MessagingLimitsDto</c> — <c>Messaging.View</c>.</summary>
    public const string MessagingLimits = "/api/messaging/limits";

    /// <summary>GET → <c>UnreadTotalDto</c> — <c>Messaging.View</c>: unread conversations (the badge).</summary>
    public const string MyUnreadTotal = "/api/me/messaging/unread-total";

    /// <summary>GET <c>?status=&amp;page=</c> → <c>MessageReportDto</c> page — <c>Messaging.Moderate</c>; <c>UnitAccess</c> at any unit of the report; outside the scope 404; a Teacher or Student 403.</summary>
    public const string ModerationReports = "/api/moderation/reports";

    /// <summary>GET → <c>MessageReportDto</c> — <c>Messaging.Moderate</c>; scope as above.</summary>
    public const string ModerationReportPattern = ModerationReports + "/{id:guid}";

    /// <summary>POST (<c>ResolveReportRequest</c> → <c>MessageReportDto</c>) — <c>Messaging.Moderate</c>; <c>Dismiss/Remove/Warn/Restrict/RemoveAndRestrict/LockConversation</c>; never touches a block; audited.</summary>
    public const string ModerationReportResolvePattern = ModerationReportPattern + "/resolve";

    /// <summary>GET → <c>ModerationStatsDto</c> — <c>Messaging.Moderate</c>; outside the scope the numbers are zero.</summary>
    public const string ModerationStats = "/api/moderation/stats";

    /// <summary>GET <c>?status=</c> → <c>AnnouncementListItemDto</c> page and POST (<c>CreateAnnouncementRequest</c> → <c>AnnouncementDto</c>) — <c>Announcements.Manage</c>; <c>UnitAccess</c> at the authoring unit and the targets inside the author's scope (a Teacher: own ClassCourses and their groups); an unreachable target 404; 400 <c>announcement_target_invalid</c>; 429 <c>rate_limited</c> (per-author daily cap).</summary>
    public const string OrgAnnouncements = "/api/comms/announcements";

    /// <summary>GET → <c>AnnouncementDto</c> (<c>Announcements.View</c> for an author or admin in scope), PUT (<c>UpdateAnnouncementRequest</c>) and DELETE of a draft — <c>Announcements.Manage</c>; 409 <c>announcement_not_editable</c>.</summary>
    public const string OrgAnnouncementPattern = OrgAnnouncements + "/{id:guid}";

    /// <summary>POST (no body) — <c>Announcements.Manage</c>: publishes a draft now, or schedules it; resolves the audience into a recipient snapshot and publishes one <c>Announcements.Published</c> per batch.</summary>
    public const string OrgAnnouncementPublishPattern = OrgAnnouncementPattern + "/publish";

    /// <summary>POST (no body) — <c>Announcements.Manage</c>: withdraws a published or scheduled announcement (recipients no longer see it).</summary>
    public const string OrgAnnouncementWithdrawPattern = OrgAnnouncementPattern + "/withdraw";

    /// <summary>POST (<c>AudiencePreviewRequest</c> → <c>AudiencePreviewDto</c>) — <c>Announcements.Manage</c>: the count before publishing.</summary>
    public const string OrgAnnouncementAudiencePreview = OrgAnnouncements + "/audience-preview";

    /// <summary>POST (multipart, one file → <c>AnnouncementAttachmentUploadDto</c>) — <c>Announcements.Manage</c>: uploads a pending attachment for a draft; the asset file rules, up to <c>Announcements.MaxAttachments</c>; 429 <c>rate_limited</c> (<c>Announcements.UploadsPerMinute</c>); 422 <c>attachment_invalid</c>.</summary>
    public const string OrgAnnouncementAttachments = OrgAnnouncements + "/attachments";

    /// <summary>DELETE — <c>Announcements.Manage</c>: removes a pending attachment the caller uploaded and no saved announcement uses yet (404 otherwise).</summary>
    public const string OrgAnnouncementAttachmentPattern = OrgAnnouncementAttachments + "/{fileId:guid}";

    /// <summary>GET → <c>AnnouncementCapabilitiesDto</c> — <c>Announcements.View</c>: what the caller may author (unit-level targets, class-course targets, urgent); never inferred from another permission.</summary>
    public const string OrgAnnouncementCapabilities = OrgAnnouncements + "/capabilities";

    /// <summary>GET → <c>MyAnnouncementDto</c> page — <c>Announcements.View</c>; only the caller's own recipient rows, active, pinned first.</summary>
    public const string MyAnnouncements = "/api/me/announcements";

    /// <summary>POST (no body) — <c>Announcements.View</c>; own recipient row only (404 others).</summary>
    public const string MyAnnouncementReadPattern = MyAnnouncements + "/{id:guid}/read";

    /// <summary>GET → <c>PushSubscriptionDto[]</c> and POST (<c>SavePushSubscriptionRequest</c>) — authenticated; own rows.</summary>
    public const string MyPushSubscriptions = "/api/me/push-subscriptions";

    /// <summary>DELETE — authenticated; own subscription only (404 others).</summary>
    public const string MyPushSubscriptionPattern = MyPushSubscriptions + "/{id:guid}";

    /// <summary>GET → <c>EffectivePreferenceDto[]</c> — authenticated; own. The existing <c>PUT /api/me/notification-preferences</c> takes <c>SavePreferencesRequest</c>; a mandatory event switched off is 409 <c>notification_mandatory</c>.</summary>
    public const string MyEffectivePreferences = "/api/me/notification-preferences/effective";

    /// <summary>GET → <c>ContactChannelDto[]</c> — authenticated; own, masked.</summary>
    public const string MyChannels = "/api/me/channels";

    /// <summary>POST (no body) → <c>TestEmailResultDto</c> — authenticated; own address; 3 a day (429 <c>rate_limited</c>).</summary>
    public const string MyEmailTest = "/api/me/channels/email/test";

    /// <summary>GET and PUT (<c>SaveMessagingRulesRequest</c> → <c>MessagingRulesDto</c>) — <c>Comms.Manage</c> at the client root (403 below it); 400 <c>rules_looser_than_default</c>.</summary>
    public const string CommsMessagingRules = "/api/comms/messaging-rules";

    /// <summary>GET → <c>ProviderStatusDto[]</c> — <c>Comms.Manage</c> at the client root.</summary>
    public const string CommsProviderStatus = "/api/comms/provider-status";

    /// <summary>GET and PUT (<c>SaveCommsSettingsRequest</c> → <c>CommsSettingsDto</c>) — <c>Comms.Manage</c> at the client root.</summary>
    public const string CommsSettings = "/api/comms/settings";

    /// <summary>POST — anonymous, signature and timestamp verified: the email relay's bounce and complaint events (401 bad signature, 413 oversize, 429 repeats).</summary>
    public const string EmailEventsWebhook = "/api/integrations/email/events";

    /// <summary>Development only (never mapped elsewhere): GET lists and POST adds to the fake provider outbox for end-to-end tests.</summary>
    public const string DevCommsOutbox = "/api/dev/comms/outbox";

    public static string Conversation(Guid id) => $"{Conversations}/{id}";
    public static string ConversationMessages(Guid id) => $"{Conversation(id)}/messages";

    public static string ConversationMessages(Guid id, Guid? beforeId, int pageSize = Messaging.MessagingLimits.MessagePageSize) =>
        $"{ConversationMessages(id)}?{(beforeId is { } b ? $"beforeId={b}&" : string.Empty)}pageSize={pageSize}";

    public static string Message(Guid conversationId, Guid messageId) => $"{ConversationMessages(conversationId)}/{messageId}";
    public static string ConversationRead(Guid id) => $"{Conversation(id)}/read";
    public static string ConversationMute(Guid id) => $"{Conversation(id)}/mute";
    public static string ConversationLeave(Guid id) => $"{Conversation(id)}/leave";
    public static string ConversationAttachments(Guid id) => $"{Conversation(id)}/attachments";
    public static string ConversationAttachment(Guid id, Guid attachmentId) => $"{ConversationAttachments(id)}/{attachmentId}";
    public static string MessageAttachmentUrl(Guid id, Guid messageId, Guid fileId) => $"{Message(id, messageId)}/attachments/{fileId}/url";
    public static string ConversationParticipants(Guid id) => $"{Conversation(id)}/participants";
    public static string ConversationParticipant(Guid id, Guid userId) => $"{ConversationParticipants(id)}/{userId}";
    public static string ConversationReadOnly(Guid id) => $"{Conversation(id)}/read-only";

    public static string MessageSearchUrl(string query, Guid? conversationId = null, int page = 1) =>
        $"{MessageSearch}?q={Uri.EscapeDataString(query)}{(conversationId is { } c ? $"&conversationId={c}" : string.Empty)}&page={page}";

    public static string MessageReport(Guid messageId) => $"/api/messages/{messageId}/report";
    public static string MyBlock(Guid userId) => $"{MyBlocks}/{userId}";
    public static string MessagingRecipientsSearch(string search) => $"{MessagingRecipients}?search={Uri.EscapeDataString(search)}";
    public static string ModerationReport(Guid id) => $"{ModerationReports}/{id}";
    public static string ModerationReportResolve(Guid id) => $"{ModerationReport(id)}/resolve";
    public static string OrgAnnouncement(Guid id) => $"{OrgAnnouncements}/{id}";
    public static string OrgAnnouncementAttachment(Guid fileId) => $"{OrgAnnouncementAttachments}/{fileId}";
    public static string OrgAnnouncementPublish(Guid id) => $"{OrgAnnouncement(id)}/publish";
    public static string OrgAnnouncementWithdraw(Guid id) => $"{OrgAnnouncement(id)}/withdraw";
    public static string MyAnnouncementRead(Guid id) => $"{MyAnnouncements}/{id}/read";
    public static string MyPushSubscription(Guid id) => $"{MyPushSubscriptions}/{id}";
}
