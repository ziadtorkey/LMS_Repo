# M6 frontend proposal: messaging, announcements, notification channels, moderation

> **Status: DRAFT proposal for the Team Lead (no code).** Author: FE1. Frontend half of M6 (Communication), written in the format of the M4b frontend proposal (`2026-11-15-m4b-frontend-proposal.md`). Task numbers (`C1`...`C9`) are local; the Lead renumbers them. The backend half is not written yet: section 6 lists what the UI needs from the contract so the Lead can hand it to BE.
>
> **Sources read:** PRD FR-NOT-01 to 03, FR-MSG-01 to 04, FR-FOR-01, X-01, X-05, X-07, X-10, the §3 permission matrix row "Messaging / announcements", DEV-PLAN M6, `docs/DESIGN.md` (Stitch "Serene Academic Emerald" tokens and §3 components), and the merged client code M6 extends: `NotificationBell`, `NotificationsPage`, `NotificationPreferencesPage`, `NotificationConnection` and `NotificationFeed` (SignalR), `INotificationsApi`, `AnnouncementsPage` (course announcements, M4a), `Announcer` and `AnnouncerRegion` (#529), `Plurals` (#515).

## 0. Scope and shape

M6 adds four user-facing things and one cross-cutting one:

1. **Messaging** (FR-MSG-01, 02): an inbox of 1:1 and group conversations, a thread view with attachments, search, unread counters and optional read receipts, and a compose flow that only offers recipients the client's messaging rules allow.
2. **Announcements** (FR-MSG-03): targeted to organization, unit, class, course, group or role; publish now or scheduled; pin; expiry. The course announcements page that exists today (`AnnouncementsPage`, FR-FOR-01) stays and becomes one view of the same data (Q4).
3. **Notification channels** (FR-NOT-03): the existing preferences page (event × channel table with locked in-app column) gains what email, SMS and web push need on the client: a Push subscription flow, a verified mobile number for SMS, and per-channel status. No change to the bell, the notification center or the hub contract except the new events.
4. **Moderation** (FR-MSG-04): report a message, block a user (everyone), and a review queue for the people with `Messaging.Moderate`.
5. **Real time** (FR-NOT-02, X-07): messages, read state and unread counters arrive over the existing SignalR connection; REST stays the source of truth and catches up after a reconnect.

What the frontend deliberately does **not** do: send or render email, SMS or push itself (server channels; the UI only configures them); rich HTML in messages (plain text with links and attachments in v1, Q3); voice or video; message editing after send beyond delete-for-me (Q5); a separate chat app shell (messaging lives in the normal layout); guardian-to-teacher scheduling or forms; any bypass of the rules in the UI (every recipient list, send and block is decided and enforced by the server; the UI only explains).

### Design and conventions (apply to every task)
- `docs/DESIGN.md` is the source of truth (Stitch tokens in `lms-stitch.css`, `PageHeader`, `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, one primary action per page, logical CSS, Rounded icons, mirrored direction icons, ISO dates left to right). New classes go in one new file `lms-messaging.css`, imported after `lms-stitch.css`, with a short "M6 patterns" block added to DESIGN.md §7b in the first PR.
- ar and en resource entries for every key (parity test), bUnit tests first, `Page_renders_rtl_in_arabic` per page, screenshots in `docs/screenshots/pr-<name>/` at 1440 and 360 px in ar and en (light and dark), 0 warnings, files under about 300 lines.
- API clients follow the M3 to M4b pattern: `I<Module>Api` + HTTP implementation using `Routes.*` and `ProblemCodes.*` + an in-memory fake registered only when `Lms:UseFakeApi=true`. The fakes mimic the rules the UI depends on (recipient rules, block, report, unread counts, schedule and expiry with the server clock).
- Counted text through `Plurals.Count` ("3 unread messages", "5 recipients"); resource prefixes `Msg_*`, `Announce_*`, `Notif_*` (extended), `Mod_*`, `Error_<code>` per new problem code.
- Live changes are announced through the `Announcer` service (polite, one message per burst), never by moving focus.

## 1. What M6 builds on (verified on main and open PRs, 2026-11-17)

| Seam | Where | M6 use |
|---|---|---|
| SignalR connection | `NotificationConnection` (one `HubConnection`, `?context=<assignment>`, automatic reconnect, `SwitchContext`), `NotificationHub.Path`, `NotificationFeed` | Messaging events reuse the same connection and its context switching (Q10); a reconnect triggers a REST catch-up. |
| Bell and center | `NotificationBell`, `NotificationsPage`, `NotificationText` | A "New message" event and an "Announcement" event render through the same text and deep-link rules; the parity test enforces ar and en labels. |
| Preferences | `NotificationPreferencesPage`, `NotificationPreferenceDto(EventKey, Channel, Enabled, Locked)`, `NotificationChannels.All = [InApp, Email, Sms, Push]` | Already shows all four channels; C6 adds channel setup and status around it. |
| Course announcements | `AnnouncementsPage`, `IForumsApi`, `ClassCourseTabs` | Kept for course-level posts; the org-level composer (C5) reuses its form pattern (title, rich text, pin, notify). |
| Targeting | `ModuleMetadataForm` (visibility, target class or group), `UnitPicker` | The announcement audience picker reuses `UnitPicker` and the group picker. |
| Uploads | `AssetUploader`, `IAssetsApi`, `ApiHttp.UploadAsync` | Message and announcement attachments. |
| Live regions | `Announcer`, `AnnouncerRegion` (#529) | Incoming messages, "sent", "reported". |
| Tables and filters | `ServerTable`, `lms-filters` toolbar, `lms-stack-table` | Moderation queue, announcement list. |
| Plurals | `Plurals.Count` (#515) | Unread and recipient counts. |

## 2. Permissions and feature flag (finalised by the Lead's M6 contract PR)

The PRD matrix gives: Org Admin **Moderate**, Manager and Director **Announce**, Counselor **Message**, Teacher **Message and Announce (own courses)**, Student and Guardian **Message (rules)**. Proposed constants (names are the Lead's call):

| Group | Constants | Frontend use |
|---|---|---|
| `Messaging` | `View`, `Send`, `Moderate` | `View`: inbox and threads. `Send`: compose, reply, block, report. `Moderate`: the queue and its decisions. |
| `Announcements` | `View`, `Manage` | `View`: read. `Manage`: compose, schedule, pin, expire, withdraw. Course teachers publish only to own courses (server rule). |
| Existing | `Forums.Moderate` | Course announcements keep their current gate until Q4 is decided. |

The `Messaging` feature flag (PRD §2) gates sends on the server (`403 feature_disabled`). As in M4b the UI does not pre-hide by flag; it shows a plain "Messaging is not enabled for your school" state (same open question as M4b Q-G1: a capabilities read for non-admins would let the nav entry hide).

## 3. Pages and components

### Routes and navigation
| Route | Page | Notes |
|---|---|---|
| `/messages` | `MessagesPage` (inbox + thread, master-detail) | Nav entry "Messages" with an unread badge (next to the bell). |
| `/messages/{ConversationId:guid}` | same page, thread open | Deep link from a notification; on phones it is the full-screen thread. |
| `/messages/new` | same page, compose open | Also `?to=<personId>` from "Message" buttons on a person (Q9). |
| `/announcements` | `OrgAnnouncementsPage` (feed + manage) | Everyone reads; `Announcements.Manage` sees "New announcement" and the manage table. |
| `/announcements/{Id:guid}` | detail | Deep link from the notification. |
| `/notifications/preferences` | existing page, extended | Channel setup section on top (C6). |
| `/moderation/messages` | `ModerationQueuePage` | Nav under Administration; needs `Messaging.Moderate`. |

### Components (new, `Components/Messaging/`, `Components/Announcements/`, `Components/Notifications/`)
- `ConversationList` (search box, unread-first ordering from the server, `Plurals` unread chips, avatar or group icon, last-message preview with `dir="auto"`, relative time as ISO tooltip), `ConversationListItem`.
- `ThreadView` (virtualised only after the first measured need; date separators; sender grouping; message bubbles that use `--mud-palette-surface` and `--lms-recessed`, own messages on the logical end; **no colour-only distinction**: own messages also carry a visually hidden "You" label), `MessageBubble`, `AttachmentChip`, `ReadReceiptMarker` (only when the client enables receipts, and only to participants allowed to see them, Q11).
- `Composer` (a plain `<textarea>` growing to 6 lines, Enter sends and Shift+Enter breaks the line, with a visible Send button so the shortcut is never the only way; attach through `AssetUploader`; `dir="auto"`; draft kept per conversation in memory only).
- `NewConversationDialog` and `RecipientPicker` (see C3): an async search that returns **only recipients the rules allow**, grouped by role; a person the rules forbid is simply not found, and a short permanent hint explains the rule in words ("Students can message their teachers and class staff").
- `ConversationMenu` (mute, leave group, block user, report), `ReportDialog` (reason code + optional note), `BlockUserDialog` (confirm, what blocking does).
- `AnnouncementComposer`, `AudiencePicker` (org, unit with subtree, class, course, group, role chips; shows "reaches about N people" from a server preview), `ScheduleFields` (publish now or at a date-time; optional expiry; pin), `AnnouncementCard` (title, author, audience chip, pinned and expired states), `AnnouncementTable`.
- `PushSetupCard`, `SmsSetupCard` (mobile number with verification code), `ChannelStatusChip` (Active, Needs setup, Blocked by the browser, Bounced), `QuietHoursFields` if Q6 says yes.
- `ModerationQueueTable`, `ModerationDecisionDialog` (reviews the reported message **in its context**: the reported message and a few messages around it, loaded by the server for the reviewer only).
- `RealtimeBanner` (a calm "Reconnecting..." line under the page header when the hub is down, with no layout jump).

## 4. Task list

Each task = one issue = one PR. Sizes: S up to 1 day, M about 2 days, L 3 days or more for one FE agent. "BE" names the backend work the UI waits for (or fakes).

### C1: Messaging client, fake, contract plumbing and nav entry (M)
**PRD:** FR-MSG-01, 02, X-07. **Depends:** M6 contract PR (section 6), BE conversations and rules (or the fake).
`IMessagingApi` (list conversations, get thread page, send, mark read, search, create 1:1 / group, add / remove member, mute, leave, allowed-recipient search, report, block / unblock, unread count) with HTTP implementation and `FakeMessagingApi` implementing the **recipient rules** (student to student off by default, teacher to own students and guardians, cross-unit denied), block, unread counts and report. `MessagingState` (a scoped service holding the unread total, updated by REST and by hub events, the way `NotificationFeed` does for the bell). Nav entry with the unread badge (`aria-label` "Messages, 3 unread"), shown with `Messaging.View`. `Notification` labels for the new events (parity test).
**Tests:** `Fake_denies_student_to_student_by_default`, `Fake_allows_teacher_to_own_students_and_guardians_only`, `Fake_hides_forbidden_recipients_from_search`, `Fake_blocked_user_cannot_send`, `Unread_total_updates_on_send_read_and_hub_event`, `Nav_entry_needs_Messaging_View`, `Notification_types_have_both_languages`.

### C2: Inbox and thread (L)
**PRD:** FR-MSG-01. **Depends:** C1.
`MessagesPage` master-detail: list left (logical start), thread right; at 600 px and below the list is the page and a conversation opens full screen with a Back control (mirrored arrow). Search across conversations and inside a thread (server search, highlights as `<mark>` inside escaped text). Paged history upwards ("Load earlier messages", focus kept on the first new message), mark-as-read when the thread is visible and focused (a debounced call, never on background tabs), unread divider ("3 new messages") above the first unread message. Attachments as chips with name, size and a download through the existing authorised download path (no direct storage URL). Empty, loading, error and "you cannot reply" (blocked or removed) states.
**Tests:** `Inbox_lists_conversations_unread_first_with_plural_chips`, `Opening_a_thread_marks_it_read_after_it_is_visible`, `Thread_shows_the_unread_divider_once`, `Load_earlier_keeps_the_reading_position`, `Search_highlights_without_rendering_html`, `Message_text_is_escaped_and_links_are_not_scripts`, `Blocked_thread_hides_the_composer_with_a_reason`, `Mobile_thread_is_full_screen_with_a_back_control`, `Page_renders_rtl_in_arabic`.

### C3: Compose, recipient rules, group conversations (L)
**PRD:** FR-MSG-01, 02. **Depends:** C1, C2.
`NewConversationDialog`: choose 1:1 or group, `RecipientPicker` (debounced search, results grouped by role and unit, keyboard-operable combobox with `aria-activedescendant`, selected people as removable chips with names read by screen readers), group name (ar or en, one required), first message and attachments. A rules hint line is always visible. Sending maps `403 messaging_not_allowed`, `409 conversation_exists` (open the existing 1:1 instead of creating a duplicate), `recipient_blocked` (the sender is **not told who blocked them**: a neutral "This person cannot be messaged" message), `413`/size errors from the uploader. Group management dialog: members, add (same picker), remove, leave; the creator or an admin may remove, members may leave.
**Tests:** `Picker_shows_only_allowed_recipients`, `Picker_explains_the_rule_without_listing_forbidden_people`, `Existing_one_to_one_is_opened_not_duplicated`, `Blocked_recipient_error_is_neutral`, `Group_needs_a_name_and_two_members`, `Chips_are_removable_by_keyboard`, `Enter_sends_and_Shift_Enter_breaks_the_line_and_a_button_also_sends`, `Send_failure_keeps_the_draft_and_offers_retry`, `Guardian_sees_only_teacher_recipients_of_their_children`, `Page_renders_rtl_in_arabic`.

### C4: Real-time messaging over SignalR (M)
**PRD:** FR-NOT-02, FR-MSG-01, X-07. **Depends:** C1, C2, BE hub events.
Extend `INotificationConnection` (or add a sibling registered on the same `HubConnection`, Q10) with `message`, `conversationRead` and `unread` events. Behaviour: a message for the open thread is appended and, if the user is at the bottom, kept in view; otherwise a "New messages" jump control appears and **focus is not moved**; a message for another conversation raises that row and the unread totals and is announced politely once per burst ("New message from Sara"); on **reconnect** the page refetches the conversation list and the open thread's tail (by last known message id) and merges without duplicates (client dedupe by message id); when the hub is down `RealtimeBanner` shows and the page keeps working by REST (no polling loop). The connection follows the active **assignment context** exactly like the bell (`SwitchContext`): on a context switch the inbox clears and reloads, because conversations are tenant and context scoped.
**Tests:** `Incoming_message_for_the_open_thread_is_appended_without_stealing_focus`, `Incoming_message_when_scrolled_up_shows_the_jump_control`, `Incoming_message_for_another_thread_updates_unread_and_announces_once`, `Duplicate_message_ids_are_ignored`, `Reconnect_refetches_and_merges_by_id`, `Context_switch_clears_and_reloads_the_inbox`, `Offline_banner_shows_while_disconnected`, `Burst_of_messages_produces_one_announcement`.

### C5: Targeted announcements (M)
**PRD:** FR-MSG-03. **Depends:** M6 contract, BE announcements, C1 for the shared parts.
- `/announcements`: a feed of what reaches the caller (pinned first, expired hidden unless the author or a manager looks), unread dot per announcement, a detail page; a "Manage" tab for `Announcements.Manage` with an `AnnouncementTable` (status chips: Draft, Scheduled, Published, Expired, Withdrawn; audience; reach; actions edit, withdraw).
- `AnnouncementComposer` (dialog or page): title ar / en, body (the existing `RichTextEditor` and `SanitizedHtml`, since announcements are authored by staff), `AudiencePicker` (any mix of org, unit with subtree toggle, class, course, group and role; teachers see only own courses; the picker shows a server-computed "Reaches about N people" and refuses an empty audience), `ScheduleFields` (now or later, expiry, pin), attachments, "Notify" switch (the channel mix follows each recipient's preferences, FR-NOT-01).
- Course announcements: `AnnouncementsPage` keeps working; until Q4 is decided both pages list what the caller may see.
- Errors: `announcement_audience_empty`, `announcement_expiry_before_publish`, `announcement_audience_not_allowed` on the right fields; a withdrawn announcement shows a tombstone in the feed for one session.
**Tests:** `Composer_requires_an_audience_and_shows_the_reach_preview`, `Teacher_audience_options_are_own_courses_only`, `Schedule_requires_a_future_time_by_server_clock`, `Expiry_must_follow_publish`, `Pinned_announcements_come_first`, `Expired_announcements_are_hidden_from_recipients`, `Body_is_sanitized_on_render`, `Student_sees_no_manage_controls`, `Notification_label_for_announcements_in_both_languages`, `Page_renders_rtl_in_arabic`.

### C6: Notification channels and preferences (M)
**PRD:** FR-NOT-01, 03. **Depends:** M6 contract (channel status, push subscription, SMS verification), BE channel work.
Extends `NotificationPreferencesPage` (the table stays; the locked in-app column stays):
- A **channel status** section above the table: a `SectionCard` per channel with a `ChannelStatusChip`.
- **Email:** the address in use (from the profile, read only here), "Send a test" (rate-limited by the server), bounce state ("Emails to this address are failing; update your address").
- **SMS:** mobile number entry (the existing phone input rules), a six-digit verification step (resend with a visible cooldown), and a clear note that messages may cost the recipient nothing and the school the cost (wording by the owner, Q7). A channel column cell is disabled with a reason until the channel is verified.
- **Web push:** `PushSetupCard` runs the browser permission flow **only on a user click** (never on page load), registers the service worker and the VAPID subscription, sends it to the server, lists "This browser" with a remove action; states: Active, Needs your permission, **Blocked in the browser** (explain how to unblock, no retry loop), Unsupported (iOS Safari outside an installed PWA: explain, Q12).
- Bulk helpers: "Turn all off for this channel" except locked events; the save bar from the Stitch layer (unsaved changes warning); `Notif_LockedHint` is kept.
- Quiet hours and digest: not in the PRD; only if the owner wants them (Q6).
**Tests:** `Channel_cells_are_disabled_until_the_channel_is_verified`, `Sms_verification_flow_accepts_the_code_and_shows_the_cooldown`, `Push_permission_is_requested_only_after_a_click`, `Push_blocked_state_explains_how_to_unblock`, `Push_unsupported_state_is_explained`, `Email_bounce_state_is_shown`, `Locked_events_stay_locked_for_bulk_off`, `Unsaved_changes_are_flagged`, `Preferences_page_renders_rtl_in_arabic`. **E2E (QA):** subscription round trip against a fake push endpoint.

### C7: Report and block (S)
**PRD:** FR-MSG-04. **Depends:** C1, C2, BE report and block endpoints.
`ReportDialog` (reason from a short list: Harassment, Spam, Inappropriate, Other + note up to 500 chars; the dialog tells the reporter what happens next and that the other person is not told), `BlockUserDialog` (explains the effect in both directions and that moderators can still see reported content), a "Blocked people" section in a small settings card on the preferences page or in the conversation menu (unblock). After reporting, the message stays visible to the reporter with a "Reported" marker; the reporter never sees the outcome details, only "Reviewed" or "Closed" (Q8).
**Tests:** `Report_requires_a_reason_and_limits_the_note`, `Report_marks_the_message_for_the_reporter`, `Report_dialog_does_not_promise_anonymity_it_cannot_keep`, `Block_hides_the_composer_and_lists_the_person_under_blocked`, `Unblock_restores_sending`, `Blocked_by_other_is_never_shown_to_the_blocked_person`.

### C8: Moderation queue (M)
**PRD:** FR-MSG-04. **Depends:** C7, BE moderation endpoints, `Messaging.Moderate`.
`/moderation/messages`: `ServerTable` with filters (status Open, Reviewed, Dismissed, Actioned; reason; unit; date) in the standard `lms-filters` toolbar, columns: reported message excerpt, reporter, sender, reason, count of reports on the sender, date, status. Row opens `ModerationDecisionDialog` showing the reported message **with its surrounding context** (a few messages before and after, from the server, reviewers only), the sender's history count and the actions: Dismiss, Delete the message (soft delete, shows as "Removed by a moderator" to participants), Warn the sender (a canned notification), Block the sender from messaging (duration), each with a required note (audited). Everything a moderator opens is **audited server-side**; the UI states that once at the top of the page. Bulk dismiss for duplicates. The queue is tenant-scoped; the Org Admin sees their client only.
**Tests:** `Queue_needs_Messaging_Moderate`, `Decision_requires_a_note`, `Dialog_shows_the_message_in_context_for_reviewers_only`, `Delete_marks_the_message_as_removed_in_the_thread`, `Filters_and_paging_use_the_server`, `Audit_notice_is_visible`, `Actions_are_disabled_while_saving_and_errors_are_mapped`, `Page_renders_rtl_in_arabic`.

### C9: M6 FE hardening: accessibility, 360 px, RTL sweep, E2E hooks (M)
One pass over every M6 page: extend the accessibility tests (named controls, labelled inputs, combobox roles and `aria-activedescendant`, the thread as a `role="log"` region with `aria-live` only where it does not duplicate the announcer, a single live source), focus order in dialogs (existing `CloseOnEscapeKey` and `dialog-focus.js`), keyboard operation of every action (including "jump to latest", report, block), 360 px layout (list or thread, never both; composer pinned to the bottom with the safe area; no horizontal scroll), RTL (bubbles on the logical end, attachment names left to right, arrows mirrored, mixed Arabic and English in a message using `dir="auto"` per message), light and dark, reduced motion for the unread jump. `data-testid`s agreed with QA for the E2E demos (two users exchange messages with a recipient rule denial; a teacher schedules an announcement that a student sees at publish time; a report reaches the queue and is actioned).

## 5. Cross-cutting notes

### Accessibility (WCAG 2.1 AA, X-10)
- One h1 per page; the conversation title is the h2 of the thread region; the list is a `nav`-like landmark with `aria-label`.
- The thread is a `role="log"` region, `aria-live="off"` by itself; arrivals are announced through **one** polite `Announcer` message per burst, so a screen reader is not read every message twice. A new message never moves focus.
- Unread is never colour only: a dot **and** a visually hidden "unread" word, plus the count in the accessible name of the row.
- Combobox pattern for the recipient picker (`role="combobox"`, listbox, `aria-activedescendant`, Escape closes, chips removable with Backspace and a button).
- Composer: a visible label, Send is a real button, Enter-to-send is announced in a hint and can be turned off where it surprises (Q13).
- Time: relative text ("5 min ago") always has the ISO time as its accessible text or `title`; no countdowns in M6.
- Attachment and link text carries the file name and size; external links show a host hint and "Opens in a new tab", as M4b.

### RTL and bilingual
- Bubbles align to the logical start or end with `margin-inline`; own messages use the end side in both languages. Each message body, preview and group name uses `dir="auto"`; file names, numbers, times, emails and phone numbers are left to right in an Arabic page.
- Message text is data, not UI: it is **never translated or resource-looked-up**; only system lines ("Sara left the group", "Removed by a moderator") come from resources.
- Mirrored icons: back, send (the paper-plane flips with `lms-icon-flip`), reply. Plural forms for ar through `Plurals.Count`.

### 360 px
- Inbox is the page; a thread opens as a full-screen route (`/messages/{id}`) with a Back control and the composer fixed above the keyboard (`env(safe-area-inset-bottom)`, `visualViewport` resize handled so the composer is not covered).
- Dialogs become full-height sheets; the audience picker's chips wrap; tables become labelled cards (`DataLabel`s), including the moderation queue and the announcement table.
- No horizontal overflow, targets at least 44 px (existing `.mud-button` rules), the nav badge never clips.

### Real-time behaviour contract (for C4 and BE)
- Delivery is **at least once**; the client dedupes by message id and orders by the server's sequence or timestamp (Q14).
- The hub carries events, not authority: every event is a hint to update the view; an action (send, read, report) is always a REST call whose answer is shown.
- After `Reconnected` the client re-sends the active context (as the bell does) and refetches; if the server cannot resume, the page shows the banner and falls back to refreshing on focus.
- No message body is placed in a **notification deep link or push payload** beyond a short preview the owner approves (Q15): lock-screen privacy.

### Security and privacy notes for QA
- Message and group names are rendered as text only; links in messages are detected client-side, shown with a host hint and `rel="noopener noreferrer nofollow"`; no HTML, no auto-embedding of remote images (tracking and abuse), attachments only through the authorised download path.
- Search results and the recipient picker are tenant and rule scoped by the server; the UI has no client-side filter that could widen them.
- A blocked person is never told they are blocked; a moderator opening a thread is audited; no export of conversations in v1.
- Push payload and email bodies are server-side and contain no message text unless Q15 says so.

## 6. Contract needs (for the Lead)

A list for the M6 contract PR and the BE proposal; names are suggestions.

**Permissions and flags:** `Messaging.{View,Send,Moderate}`, `Announcements.{View,Manage}`; feature flag `Messaging` returning `403 feature_disabled`; optional capabilities read for non-admins (M4b Q-G1).

**Messaging**
- Conversations: list (cursor or page, unread-first, last message preview, unread count, mute flag, type 1:1 or group, name ar and en for groups, member count, blocked state), get with members, create (1:1 or group with first message), add and remove members, leave, mute.
- Messages: page by cursor in both directions (before / after id), send (text up to a limit, up to N attachments), mark read up to a message id, delete for me, search (across conversations and inside one), read receipts (who read up to which message, only if enabled).
- Recipients: `GET .../recipients?q=` returning only allowed people (id, name ar and en, role label, unit, avatar) with paging; a separate **rules summary** read (who may message whom for the caller) for the hint line.
- Unread: `GET .../unread-count` and the same total in a hub event.
- Report and block: `POST .../messages/{id}/report` (reason code, note), `POST/DELETE .../blocks/{personId}`, `GET .../blocks`.
- Problem codes: `messaging_not_allowed`, `conversation_exists` (with the existing id), `recipient_blocked`, `message_too_long`, `attachment_too_large`, `feature_disabled`, `group_too_large`.
- Attachments: the uploaded file owner type for messages and announcements so the authorised download works.

**Announcements**
- CRUD with: title ar and en, body (sanitised HTML), audience as a list of `{kind: Org|Unit|Class|Course|Group|Role, id, includeSubtree}`, `publishAt` (null = now), `expiresAt`, `pinned`, `notify`, attachments, status (Draft, Scheduled, Published, Expired, Withdrawn).
- `POST .../announcements/audience-preview` returning the approximate recipient count and a refusal reason when a teacher picks something outside their courses.
- Reader feed: paged list of announcements that reach the caller, with `read` state and `POST .../read`.
- Problem codes: `announcement_audience_empty`, `announcement_audience_not_allowed`, `announcement_expiry_before_publish`, `announcement_publish_in_past`.
- Relationship to the existing course announcements (FR-FOR-01): one store or two (Q4).

**Notification channels**
- `GET/PUT` of the existing preferences stay; add **channel status** per channel (`Active`, `NeedsSetup`, `Blocked`, `Bounced`, last error code, verified at), a test-send action for email, SMS verify start and confirm (`POST .../sms/verify`, `.../sms/confirm`, resend cooldown in seconds), Web Push: public VAPID key read, `POST .../push/subscriptions` (endpoint, keys, user agent label), `GET` list, `DELETE` one.
- New event keys in `NotificationEvents` with default recipients and locked flags: `Messages.NewMessage`, `Announcements.Published`, `Messaging.Warning` (moderator warning), `Messaging.Blocked` (messaging restriction), reusing `Forums.Announcement` where Q4 merges them.

**Moderation**
- `GET .../moderation/reports` (filter by status, reason, unit, date; paging), `GET .../reports/{id}` with the reported message and **context window** (reviewers only), `POST .../reports/{id}/decision` (Dismiss | DeleteMessage | WarnSender | RestrictSender with duration, note required), bulk dismiss. Everything audited. Problem codes: `report_already_decided`, `decision_note_required`.

**Real time**
- Hub events on the existing connection: `message` (conversation id, message dto), `conversationRead` (conversation id, reader, up to message id), `unread` (total), `announcement` (id); same `SwitchContext` behaviour; at-least-once delivery; the ordering key; a way to resume (last message id) or an explicit statement that clients refetch.

**Dev and test**
- Fake push endpoint and fake SMS provider for E2E (QA's side); seeded demo conversations in the fake client API.

## 7. Task order and parallelism

```
M6 contract (Lead) ── C1 ─┬─ C2 ── C3 ──┬─ C4 (needs BE hub events)
                          │             └─ C7 ── C8 (needs BE moderation)
                          └─ C5 (announcements, independent of C2 after C1)
channel work (BE) ───── C6 (independent of C1)
                                         all ──────── C9
```

| Task | Size | Depends on |
|---|---|---|
| C1 client, fake, nav | M | contract |
| C2 inbox and thread | L | C1 |
| C3 compose and recipient rules | L | C1, C2 |
| C4 real time | M | C1, C2, BE hub events |
| C5 targeted announcements | M | contract, C1 (shared parts only) |
| C6 channels and preferences | M | contract, BE channels |
| C7 report and block | S | C1, C2 |
| C8 moderation queue | M | C7, BE moderation |
| C9 hardening | M | all |

- **Parallelism:** two FE agents. FE-A: C1, C2, C3, C4 (the conversation spine, in that order). FE-B: C6 (needs no messaging), then C5, then C7 and C8. C9 is shared at the end. C6 and C5 can start the day the contract merges because they use fakes.
- **Total** about 3 L-weeks of FE work in parallel with BE; C4 and C8 gate on BE endpoints, everything else runs against fakes first.
- DEV-PLAN M6 order (email, SMS and push; messaging; announcements; moderation) is BE-led; the UI order above follows what can be shown earliest: messaging spine first, channels in parallel.

## 8. Open questions

**For the owner** (the Lead asks via popup):
1. **Q1 Who may message whom (FR-MSG-02 defaults).** Proposed defaults per client: staff to staff in the same client: yes; teacher to own students and their guardians: yes; student to teacher of own class courses: yes; student to student: **off** (PRD); guardian to teachers of own children: yes; guardian to guardian and student to guardian: off. Is this the default rule set, and which of these may a client admin change? (The UI needs the final matrix for the hint text.)
2. **Q2 Read receipts.** Always off, per client setting, or per user setting? (FR-MSG-01 says "optional".)
3. **Q3 Message format.** Plain text with auto-linked URLs and attachments (this proposal) or limited formatting (bold, lists)? Plain keeps the moderation and safety surface small.
4. **Q4 Announcements and course announcements.** One store with a course audience, so the course page is a filtered view (proposed), or two parallel features kept apart?
5. **Q5 Editing and deleting messages.** Delete for me only (proposed), or senders may delete their own message for everyone within a window (and show "Message deleted")?
6. **Q6 Quiet hours and digests.** Not in the PRD. Wanted in v1 (a quiet window that holds email, SMS and push, and an optional daily digest), or later?
7. **Q7 SMS cost and consent.** Who pays and what wording do we show on the SMS card? Is an explicit opt-in sentence required for SMS and push (consent record)?
8. **Q8 What the reporter learns.** Only "Reviewed" or "Closed", or also the action taken? And is the reported person told they were reported (proposed: no)?

**For the Lead and BE:**
9. **Q9 Entry points.** Add "Message" buttons on people cards (teacher on a student's profile, guardian view of the class teacher) in v1, or only the inbox "New message" button?
10. **Q10 One hub or two.** Messaging events on the existing notifications hub and connection (proposed: one connection, one reconnect and context path) or a separate hub?
11. **Q11 Read receipt visibility** in groups: names or only counts; and may a student see a teacher's receipt?
12. **Q12 Web push on iOS and desktop browsers.** Confirm support targets: installed PWA on iOS 16.4+, current Chrome, Edge, Firefox, Safari on desktop; where the user's browser cannot, the card explains.
13. **Q13 Enter to send.** Default on for desktop, off for touch (proposed), with a setting? Product decision for the composer.
14. **Q14 Ordering and delivery.** Which key orders messages (server sequence per conversation)? Confirm at-least-once delivery and that the client dedupes by id.
15. **Q15 Preview text in push and email.** Show no message text (privacy-first, proposed), the sender's name only, or a short preview? Lock-screen exposure of a minor's messages is a safeguarding concern.
16. **Q16 Capabilities read (carried from M4b Q-G1).** Hide the Messages nav entry when the flag is off for the caller's unit, which needs a small read for non-admins.
17. **Q17 Limits.** Message length, attachments per message, file size, group size, conversations per user, announcement attachment count and audience size: the UI shows the numbers from `GET /api/.../limits` rather than hard-coding them. Please confirm a limits read exists.

## 9. Risks

- **Privacy and safeguarding** is the main risk (minors messaging, guardians, moderation access to content): the rules, the audit and the "who learns what" answers (Q1, Q8, Q15) must be settled before C3 and C8 are built; the fake can carry placeholders but the wording cannot.
- **Real time on mobile browsers** drops connections often; the catch-up path (C4) must be tested with an offline and resume E2E, not only bUnit.
- **Context switching** (users with several assignments) makes a "conversation" tenant and context scoped; the inbox must never show a conversation from another context after a switch (tested in C4).
- **Push on iOS** works only for installed PWAs; set the owner's expectation early (Q12).
- **SignalR in bUnit** is stubbed through `INotificationConnection`; the fake connection must be able to raise events for the C4 tests (small test helper in C1).
