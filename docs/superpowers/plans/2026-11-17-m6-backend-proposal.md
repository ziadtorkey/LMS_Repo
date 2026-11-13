# M6 Backend Proposal: Communication (email, SMS, push, messaging, announcements, moderation)

> **Status: DRAFT proposal for the Team Lead (no code), revision 1 (2026-11-17).** Written by Backend 1 from `docs/DEV-PLAN.md` M6, `docs/PRD.md` FR-NOT-01..03, FR-MSG-01..04, X-07, §3.4 and D1-D57, and the code on `main` at `3fb28bda` + the M4a/M4b notification, forum and job seams. Modelled on the M4b backend proposal (#427). Open owner decisions are the numbered **Q** items in section 13: sections 2 to 12 assume the defaults named there.

**Goal:** Every domain event reaches people on the channels they chose (in-app always; email, SMS and Web Push per event and user preference) through one queued, retried, rate-limited delivery pipeline; users hold 1:1 and group conversations under client-level messaging rules; staff publish targeted announcements (organization, unit, class, course, group or role; now or scheduled; pin; expiry); users report messages and block others, and admins moderate.

**Spec:** FR-NOT-01/02/03, FR-MSG-01..04, X-07, §3.4 (Messaging/announcements row), §4.4 feature flags (`Messaging`, `GuardianPortal`), D16/D28 (role escalation), D30 (guardians), D42 (relationship rules), D51 (kernels), D52/D56 style decisions. Master plan: `docs/DEV-PLAN.md` M6 items 1-5 (email + preferences; SMS + push; messaging; announcements; moderation).

---

## 1. What M6 builds on (verified on `main`, 2026-11-17)

| Seam | Where | M6 use |
|---|---|---|
| Notification kernel | `INotificationPublisher.PublishAsync(NotificationRequest)` stores one `Notification` row per recipient (in-app) and pushes it over SignalR through `INotificationPusher`; `NotificationEvents` catalog (`Key`, default recipient roles, parameters); `NotificationRecipientResolver` (role holders at/below or at/above the event unit; the acting user is never notified) | The publisher gains an **outbox write**: in the same transaction as the `Notification` rows it writes one `NotificationDelivery` per (recipient, enabled channel). The in-app path is unchanged; its tests stay. |
| Preferences | `NotificationPreference(UserId, EventKey, Channel, Enabled)`, `NotificationChannels {InApp, Email, Sms, Push}`, `INotificationPreferences.IsEnabledAsync`, my-preferences endpoints and page | Stored today, honoured for in-app only. M6 makes them effective for the other three channels and adds **event defaults per channel** (a catalog column) and **mandatory events** that cannot be switched off. |
| Email | `IEmailSender` (`EmailMessage(To, Subject, HtmlBody)`) with `SmtpEmailSender` (MailKit, `EmailOptions`), `IAccountEmails` (activation, reset, credentials: sent **directly**, not queued) | The queued email channel wraps the same sender; account emails move onto the outbox (retries, bounce logging) but stay *transactional* (no preference check, no quiet hours). |
| Jobs | `IBackgroundJobs.Enqueue/EnqueueAsSystem/AddOrUpdateRecurring/Schedule(key, runAt, …)/ScheduleAsSystem/Cancel` (#434: idempotent keys, MySQL named lock `IJobKeyLock`); `JobSettings.TimeZone`; system context via `JobContextSnapshot.System` | Delivery dispatcher (recurring + kicked on write), announcement publish/expiry (keyed schedule per announcement), retention. Same system-context rules and the same `// ISOLATION-BYPASS` + allow-list discipline. |
| Rate limiting pattern | `ZoomOutboundLimiter` (token bucket per class, penalty window on 429, bounded queue) (#499) | Reused as the shape of the per-provider limiter (email relay, SMS gateway, push service). |
| Announcements today | `CourseAnnouncement` (class course board, pin, notify enrolled students, M4a #376) + `Forums.Announcement` event | Stays as the **course board**; M6 adds cross-scope `Announcement` (FR-MSG-03). Whether the two merge is Q19. |
| Realtime | `NotificationsHub` (SignalR, group per user, token startup filter) | New events `MessageReceived`, `ConversationRead` over the same hub (ids only; the client fetches). |
| Authorization | `Permissions`, `BuiltInRoles` matrix, `RelationshipRules` (Teacher own ClassCourses, Supervisor supervised, Student own enrollments), `UnitAccess.CheckUnitAsync`, `RelationshipAccess.CheckClassCourseAsync`, ProblemDetails `code`s | New permission groups (section 9); relationship rules extended; 404 on a relationship miss, 403 on a missing permission, everywhere. |
| People and guardians | `StudentProfile`, `GuardianProfile` + links (D30, portal #294), `StaffProfile` (email, mobile), `User` (email, status), `ClassStudent`, `CourseEnrollment`, `GroupMember` | Contact points and the audience resolver read these (section 5, 8). |
| Files | `IFileStorage`, M4a asset service, `Files.MaxUploadMb` | Message attachments (section 6). |
| Feature flags | `FeatureFlags.Messaging`, `GuardianPortal` (catalog exists, default false) | `Messaging` gates all conversation writes and announcement targeting of guardians where relevant (403 `feature_disabled`, the M4b pattern). |
| Cross-cutting | One migration per PR; probe rows per route; isolation test per table; `[Audited]` on business entities; allow-listed `IgnoreQueryFilters`; warnings are errors | Same. |

## 2. Decisions this proposal assumes (confirm in review; see section 13 for the owner questions)

1. **One pipeline.** In-app stays synchronous (as today); email, SMS and push are **asynchronous deliveries from a transactional outbox** (the pattern of ADR 0004): the `NotificationDelivery` rows commit with the event, a dispatcher sends them, a failure never undoes the event and a crash never loses a delivery.
2. **At-least-once delivery with idempotent sends.** A delivery is claimed with a lease, marked `Sent` with the provider message id, and a retry after a crash may duplicate at most one message; the provider message id is stored so duplicates are visible. No exactly-once claim.
3. **Providers are abstractions with fakes** (`IEmailChannel` over the existing MailKit sender, `ISmsProvider`, `IWebPushSender`); the real SMS gateway is chosen by the owner (Q2); the fakes are what every test and the E2E stack use.
4. **No per-client provider credentials in v1** (like Zoom, D49): one platform email relay, one platform SMS account, one VAPID key pair, from server configuration/secret store only. Per-client sender name/branding is a setting (Q12).
5. **Preferences are per user, per event, per channel** (existing table) with **catalog defaults** and **mandatory events**; a client admin sets *which channels may be used at all* for the client (setting), a user can only narrow.
6. **Quiet hours apply to SMS and push only**, never to email or in-app, and never to mandatory/urgent events (Q5). They are evaluated in the client's time zone (`Jobs:TimeZone` today; a unit setting is Q5b).
7. **Messaging is relationship-based, not unit-subtree-based.** A conversation is tenant-owned (`OrganizationId`) and visible only to its participants; org admins do **not** read arbitrary conversations (moderation sees reported messages only, section 8).
8. **Rules are default-deny with an editable matrix per client** (FR-MSG-02), evaluated on conversation creation, on adding a participant and on every send to a direct conversation; student-to-student is off by default.
9. **Announcement audiences are resolved server-side at publish time** into a recipient snapshot (so history is stable), reusing the M3 relationship rules for *who may target what* and the existing role/enrollment/group queries for *who is reached*.
10. **Moderation never exposes whole conversations**: a report carries the reported message plus a bounded context window; moderators act on messages and on a user's messaging right, all audited.

## 3. Domain model (new entities)

Tenant-owned = `OrganizationId` with the client query filter; unit-scoped = also `OrgUnitId`/`OrgUnitPath` (`IOrgUnitScoped`, repather-covered). Every entity gets an isolation test; `[Audited]` where it is business configuration.

| Entity | Scope | Notes |
|---|---|---|
| `NotificationDelivery` | tenant | `NotificationId`, `UserId`, `Channel` (`Email`/`Sms`/`Push`), `Status` (`Pending`, `Sending`, `Sent`, `Failed`, `Suppressed`, `Held`, `Skipped`), `Attempts`, `NextAttemptAtUtc`, `LeaseUntilUtc`, `HeldUntilUtc` (quiet hours), `Provider`, `ProviderMessageId`, `LastErrorCode` (code only, never a body), `SentAtUtc`, `CreatedAt`. Unique `(NotificationId, Channel)`. Rows for transactional emails (account, reset) use `NotificationId = null` + `TemplateKey`. Retention job (section 4.6). |
| `ContactSuppression` | tenant | `Channel`, `AddressHash` (SHA-256 of the normalised address, never the address), `Reason` (`HardBounce`, `Complaint`, `UserOptOut`, `InvalidNumber`), `CreatedAt`. A suppressed address is `Suppressed`, not retried. |
| `PushSubscription` | tenant | `UserId`, `Endpoint` (unique, length-capped), `P256dh`, `Auth` (protected with Data Protection), `UserAgent` (trimmed), `CreatedAt`, `LastSuccessAtUtc`, `ExpiredAtUtc`. Removed on 404/410 from the push service. |
| `NotificationPreference` (exists) | user | Add `Channel` values already present; defaults/mandatory live in the catalog (code), not rows. |
| `ContactPoint` (view, not a table) | - | The resolved delivery address of a user per channel (email from `User`, mobile from the staff/guardian/student profile, push from `PushSubscription`), plus verified flags; produced by `IContactResolver` (section 5). |
| `Conversation` | tenant | `Kind` (`Direct`, `Group`), `Title?` (groups), `CreatedByUserId`, `ClassCourseId?` (context link, optional), `DirectKey` (for `Direct`: sorted pair of user ids, **unique per client** so a pair has one conversation), `LastMessageAtUtc`, `IsLocked`. `[Audited]` for group create/lock. |
| `ConversationParticipant` | tenant | `ConversationId`, `UserId`, `Role` (`Member`, `Owner`), `JoinedAtUtc`, `LeftAtUtc?`, `LastReadMessageId?`, `Muted`. Unique `(ConversationId, UserId)`. This table is the **access list**. |
| `Message` | tenant | `ConversationId`, `SenderUserId`, `BodyText` (plain text, length cap 4000, normalised; no HTML in v1), `CreatedAtUtc`, `EditedAtUtc?`, `DeletedAtUtc?` (sender or moderator), `State` (`Visible`, `HiddenByModerator`). FULLTEXT index (section 6.4). Not audited row by row (volume); moderation actions are. |
| `MessageAttachment` | tenant | `MessageId`, `StoredFileId` (the M4a asset path: type/size checks, signed GET), `FileName`. |
| `MessagingRule` | tenant | `FromRole`, `ToRole` (built-in role keys), `Allowed`, `Via` (`Relationship`, `SameUnit`, `Anywhere`): the editable matrix (FR-MSG-02). Seeded per client with defaults; `[Audited]`. |
| `UserBlock` | tenant | `BlockerUserId`, `BlockedUserId`, `CreatedAt`; unique pair. Blocks are one-directional in storage, symmetric in effect for **direct** conversations (neither can send). |
| `Announcement` | unit | `Title`, `BodyHtml` (sanitised by `IRichTextSanitizer`), `AuthorUserId`, `Status` (`Draft`, `Scheduled`, `Published`, `Expired`, `Withdrawn`), `PublishAtUtc?`, `ExpiresAtUtc?`, `IsPinned`, `IncludeGuardians`, `AudienceCount`. Unit fields = the **authoring unit** (the caller's context unit), so admins above see it. `[Audited]`. |
| `AnnouncementTarget` | unit | `AnnouncementId`, `Kind` (`Organization`, `Unit`, `Class`, `ClassCourse`, `Group`, `Role`), `TargetId` (unit/class/class-course/group id) or `RoleKey`. 1..n per announcement. |
| `AnnouncementRecipient` | tenant | `AnnouncementId`, `UserId`, `ReadAtUtc?`: the resolved audience snapshot at publish (also drives the read state and the fan-out). Created in batches by the publish job. |
| `MessageReport` | unit | `MessageId`, `ReporterUserId`, `Reason` (code) + `Note?` (≤500), `Status` (`Open`, `Dismissed`, `ActionTaken`), `ModerationUnitPath` (the **reported sender's home unit path** at report time, so admins at/above see it), `ContextJson` (the bounded message window, frozen), `ReviewedByUserId?`, `ReviewedAtUtc?`, `Resolution` (code). `[Audited]`. |
| `MessagingRestriction` | tenant | `UserId`, `UntilUtc?`, `Reason` code, `ByUserId`: a moderator's mute of a user's right to send. Checked on every send. `[Audited]`. |

Contracts are wave-listed in section 10. Nothing here changes an M1-M5 table except two additive columns: `Notification` gets no change; the notification **event catalog** gets `DefaultChannels` and `Mandatory` (code, not DB).

## 4. Delivery pipeline (FR-NOT-03, X-07)

### 4.1 Write path
`INotificationPublisher.PublishAsync` (unchanged signature) keeps validating the event and resolving recipients, then in **one transaction**: inserts the `Notification` rows (in-app) and, for each recipient and each channel in `{Email, Sms, Push}` that passes the checks below, one `NotificationDelivery(Pending)`. Checks, in order: client channel setting on (`Comms.EmailEnabled`/`SmsEnabled`/`PushEnabled`); the user's preference for (event, channel), falling back to the catalog default; the event is not `InApp`-only; a contact point exists (otherwise **no row**, counted in a metric); the address is not suppressed (`Suppressed` row kept for the log). The in-app SignalR push stays after commit as today.

### 4.2 Dispatcher
A recurring system job (`comms-dispatch`, every 15 s, also kicked after a publish) claims a bounded batch **per channel** with a conditional update (`Status=Pending AND NextAttemptAtUtc<=now` → `Sending`, `LeaseUntilUtc=now+2 min`, ordered by `NextAttemptAtUtc`, `LIMIT n`), so two nodes never take the same row; expired leases return to `Pending`. For each row it loads the notification, renders the channel text (4.4), calls the channel sender through the limiter, and writes the outcome. It runs under the system context and reads across clients: **bypass site 1** (allow-listed, with the isolation statement: it reads delivery rows and the minimum of user contact data, never content of other modules).

### 4.3 Retries and failure classes
Sender exceptions are classified, never string-matched in the dispatcher: `Transient` (timeout, 5xx, 429): retry with backoff **1 min, 5 min, 30 min, 2 h, 12 h**, max 5 attempts, honouring `Retry-After`; `Permanent` (invalid address, 4xx, rejected sender): `Failed`, no retry, and for a **hard bounce** or complaint a `ContactSuppression` is written; `NotConfigured`: the delivery stays `Pending` with a long backoff and the provider status shows it (the Zoom `provider_not_configured` pattern). `LastErrorCode` stores a code, never a response body, an address or a token. A delivery older than its **time to live** per channel (SMS 6 h, push 24 h, email 3 days) becomes `Skipped` instead of sending stale news.

### 4.4 Templates
Each (event, channel) has a localised template in **resource files** (ar and en; the existing `NotificationText` parity rule) rendered with the stored parameters: email = subject + HTML + text alternative, SMS = one short text (length-aware: Arabic uses UCS-2, 70 characters per segment, the renderer reports segments and truncates with an ellipsis at a configurable cap), push = title + body + a deep link. The recipient's language is the user's preference if set, else the unit's `Ui.DefaultCulture` (Q10). Templates never carry data the recipient could not see in the app (no grades in SMS/push bodies by default; a "new grade" push says so without the score, Q3).

### 4.5 Rate limits and caps
Per provider token bucket (email relay messages/second, SMS gateway messages/second, push), a penalty window on a provider 429, a bounded in-memory queue, and **per-client daily caps** as settings (`Comms.MaxEmailPerDay` default 5,000, `Comms.MaxSmsPerDay` default 500, `Comms.MaxPushPerDay` 20,000): over the cap a delivery becomes `Held` until the next UTC day and the unit admins get one `Comms.CapReached` notice per day. Caps count **sent** deliveries, mirroring `Live.MaxCreatesPerDay`. A broadcast announcement larger than the remaining cap does not silently drop recipients: the overflow is `Held` and delivered next day (Q9 asks whether to refuse instead).

### 4.6 Retention
`NotificationDelivery` rows older than **90 days** (`Comms.DeliveryRetentionDays`) are deleted by a daily job; `ContactSuppression` is kept until the user changes the address or an admin lifts it. Delivery rows hold no message body, so retention is about volume, not privacy (Q11 confirms the period).

### 4.7 Bounces and complaints
v1 logs what the SMTP exchange itself reports (synchronous rejections) and accepts **asynchronous bounces** through one anonymous, signature-verified webhook for the chosen relay (`POST /api/integrations/email/events`, the Zoom webhook pattern: body cap, signature, receipt table for idempotency); the exact payload depends on the relay (Q1). Without a webhook relay, bounces arrive by a polled mailbox (VERP return path) in a later task.

## 5. Provider abstractions with fakes

```csharp
public interface IEmailChannel { Task<SendResult> SendAsync(EmailDelivery d, CancellationToken ct); }          // wraps IEmailSender (MailKit)
public interface ISmsProvider  { Task<SendResult> SendAsync(SmsDelivery d, CancellationToken ct); string Name { get; } }
public interface IWebPushSender{ Task<SendResult> SendAsync(PushSubscription s, PushPayload p, CancellationToken ct); }
public sealed record SendResult(SendOutcome Outcome, string? ProviderMessageId, string? ErrorCode, TimeSpan? RetryAfter);
public enum SendOutcome { Sent, Transient, Permanent, HardBounce, NotConfigured }
```

- **Fakes** (`FakeEmailChannel`, `FakeSmsProvider`, `FakePushSender`) in Infrastructure, registered in the test host (the `FakeMeetingProvider` pattern: recorded calls, scripted outcomes `ScriptNext(Transient, Sent, …)`, `FailAfter`). The E2E stack uses them too, plus a Development-only trigger like `POST /api/dev/zoom/events` (`/api/dev/comms/outbox` to list what was "sent").
- **Real providers:** email = the existing `SmtpEmailSender` (MailKit) behind `IEmailChannel`; SMS = one adapter chosen in Q2; push = a VAPID implementation (`WebPush` library or hand-rolled RFC 8291/8292 with `System.Security.Cryptography`; decision in the task, library license checked).
- **`IContactResolver`:** `user → (email, mobile, push subscriptions, verified flags, language)`. Email from `User.Email`; mobile from `StaffProfile.Mobile`, `GuardianProfile.Mobile`, or the student record's mobile if present (D26: students may have none); for a **student under the minor rule** (Q6) SMS/email resolve to the primary guardian's contact, flagged `OnBehalfOf`. Missing contact = no delivery row.
- **Provider status** (`GET /api/comms/provider-status`, `Comms.Manage`, platform health only: configured/healthy per channel, never configuration), like the Zoom status.
- **Secrets** (SMTP password, SMS credentials, VAPID private key) only in configuration/secret store; a test asserts none appears in logs, DTOs, audit rows or delivery rows.

## 6. Messaging (FR-MSG-01/02)

1. **Conversations:** `POST /api/conversations` (direct with one user, or group with ≥2 others); a direct conversation between the same pair is **returned, not duplicated** (`DirectKey`). Group creation, max size (default 50) and who may create are rules (Q15). Participants may leave a group; the owner may add/remove (rules re-checked on add).
2. **Rules (`IMessagingPolicy`):** `CanMessageAsync(senderUserId, recipientUserId)` combines (a) the client's `MessagingRule` matrix by the two users' **active built-in roles** (a user with several roles: any allowed pair), (b) the **relationship** when the rule says `Relationship`: sender and recipient share a ClassCourse (teacher of / enrolled in), or the recipient is a guardian of a student the sender teaches or counsels (M3 links), (c) blocks, (d) `MessagingRestriction`, (e) the `Messaging` flag. Defaults (Q13): Org Admin/Manager/Supervisor/Counselor → anyone in their scope; Teacher ↔ own students, their guardians, staff of the unit; Student → own teachers and counselor, **not** other students; Guardian → teachers/counselor of own children. A failing check is 403 `messaging_not_allowed` with a reason code, never a user list.
3. **Messages:** `POST /api/conversations/{id}/messages` (text + up to 5 attachments via the asset path, same file rules and sanitising as M4a), paged `GET` (keyset by id, newest first), edit/delete own within a window (Q18); soft delete keeps a tombstone for the reporter context. Server time on every timed DTO.
4. **Unread counters and receipts:** `LastReadMessageId` per participant (no per-message receipt rows); `POST /api/conversations/{id}/read`; the conversation list returns `UnreadCount`, `LastMessage`, `Muted`. **Read receipts are optional** (FR-MSG-01): a client setting `Messaging.ReadReceipts` (default off; Q16) controls whether other participants see "seen by"; counters work either way.
5. **Search:** MySQL FULLTEXT (`WITH PARSER ngram` for Arabic and English) on `Message.BodyText`, **always joined to the caller's participant rows** (a user searches only conversations they are in); minimum query length 2, result cap 50, rate limited. The ngram parser's quality for Arabic stems is a risk (section 12); fallback is `LIKE` on a short window.
6. **Realtime:** a new message publishes `MessageReceived{conversationId, messageId}` over `NotificationsHub` to each participant's user group (ids only; the client fetches); an in-app notification `Messaging.NewMessage` is created **only for offline/muted-not users and is throttled per conversation** (one unread notification per conversation until read, so a chatty thread does not flood the bell or the outbox); email/push for messages follow the same throttle and preferences, SMS never for chat by default.
7. **Blocks:** `PUT/DELETE /api/me/blocks/{userId}`; a block hides nothing already sent, stops new direct messages both ways, and does not remove either from shared groups (Q14).

## 7. Announcements (FR-MSG-03)

- **Create:** `POST /api/announcements` with title, body (sanitised), 1..n targets (`Organization`, `Unit`, `Class`, `ClassCourse`, `Group`, `Role`+scope), `PublishAtUtc?` (null = now), `ExpiresAtUtc?`, `IsPinned`, `IncludeGuardians`. `Draft` saves without sending. `PUT` edits while `Draft`/`Scheduled`; once `Published` only expiry/pin/withdraw change (body edits create a visible "edited" marker, Q19).
- **Who may target what:** needs `Announcements.Create` at the **authoring unit** (UnitAccess) and the target inside the author's scope: a `Unit`/`Class`/`Organization` target must lie at or below a unit where the author holds the permission; a **Teacher** (relationship rule `OwnClassCourses`) may target only own ClassCourses and their groups and may not use `Organization`/`Unit`/`Class`/`Role`; Manager/Supervisor/Org Admin per the matrix (Supervisor: supervised courses' ClassCourses). Anything outside is 404 for a hidden target, 403 for a missing permission.
- **Audience resolution (`IAudienceResolver`)** reuses existing queries, nothing new in the model: `Organization`/`Unit` = active role holders and active students at/below the unit (the `NotificationRecipientResolver` shape + `ClassStudent` placements); `Class` = active class students; `ClassCourse` = active `CourseEnrollment`s; `Group` = `GroupMember`s; `Role` = valid role assignments at/below the unit (`ValidFrom/To` honoured, inactive users excluded). `IncludeGuardians` adds the **linked guardians with a portal account** of every student reached (D30), once each. The result is deduplicated and snapshotted into `AnnouncementRecipient` by the publish job in batches of 500 (large audiences never run inside the request). **Bypass site 2** (allow-listed): the resolver reads people across units below the author's scope boundary only through these queries, client-matched and live-only.
- **Publish job:** `Publish` kicks `announcement-publish:{id}` (or schedules it at `PublishAtUtc` through the keyed scheduler, moved on edit, cancelled on withdraw); it resolves, writes recipients, then publishes **one** `Messaging.Announcement` event per batch of recipients through the normal publisher (so channels, preferences, quiet hours and caps all apply). Expiry: `announcement-expire:{id}` flips the status and hides it; pinned announcements sort first.
- **Reading:** `GET /api/me/announcements` (paged, active, pinned first, unread flag), `POST …/{id}/read`; `GET /api/announcements` (authors/admins: by status, with `AudienceCount`, delivery summary counts per channel). `GET /api/announcements/audience-preview` returns the **count** (and a capped sample of names for the author's own scope) before publishing.
- **Course board interplay:** `CourseAnnouncement` keeps working unchanged; a ClassCourse-target announcement also appears on that course's announcements page through a read-only join (Q19 decides whether to migrate the board onto this model later).

## 8. Moderation (FR-MSG-04)

- **Report:** `POST /api/messages/{id}/report` by any participant (not the sender), once per (message, reporter); stores the reason code, optional note, the **frozen context window** (the reported message and the 5 before and after, text only) and `ModerationUnitPath` = the sender's **home unit path** (so Org Admins at or above that unit, the matrix's "Moderate", see it; a client-root admin sees all). The reporter is told it was received; the sender is not told who reported.
- **Queue and review:** `GET /api/moderation/reports` (`Messaging.Moderate`, scoped by `ModerationUnitPath`, status filter), `GET …/{id}` (the frozen context; never the live conversation), `POST …/{id}/resolve` with an action: `Dismiss`, `HideMessage` (state `HiddenByModerator`, tombstone for participants), `DeleteMessage`, `Restrict` (creates a `MessagingRestriction` with an end date), `LockConversation`. Every action is audited and notifies the reporter with the outcome code only.
- **Blocking** is the user-side tool (section 6.7); reporting and blocking are independent.
- **Privacy rules:** no endpoint lists or reads another user's conversations; moderators see only reported context; a moderation read is itself an audit entry; platform admins have no extra read path. Retention of reported context follows the report (Q21).
- **Abuse limits:** per-user send rate (`Messaging.MaxPerMinute`, default 30), per-user daily new-conversation cap (`Messaging.MaxNewConversationsPerDay`, default 50), report rate cap, attachment type allow-list, body length cap; over a limit is 429 `rate_limited`.

## 9. Permissions, relationships, isolation, probe rows

**New permissions** (matrix proposal following PRD §3.4 "Messaging / announcements": Org Admin *Moderate*, Manager *Announce*, Supervisor *Announce*, Counselor *Message*, Teacher *Message, Announce (own courses)*, Student/Guardian *Message (rules)*): `Messaging.Send`, `Messaging.Moderate`, `Announcements.View`, `Announcements.Create` (create/edit/withdraw own and, for admins, any in scope), `Comms.Manage` (provider status, rules matrix, caps; Org Admin at the client root, like D18). Built-in role defaults reconcile to `BuiltInRoleDefaults.CurrentVersion + 1`; the D16 escalation guard stays satisfied because Manager holds a superset of what it may assign.

**Relationship rules extended** (D42): Teacher `Announcements.Create` only with the `OwnClassCourses` relationship; Student/Guardian `Messaging.Send` is further narrowed by `MessagingRule`; conversations are not relationship-resource routes (they are participant-checked, below).

| Route | Permission | Rel / extra check | Wrong caller |
|---|---|---|---|
| `GET/POST /api/conversations`, `GET /api/conversations/{id}` | `Messaging.Send` | participant; `Messaging` flag; rules on create | non-participant (including an Org Admin) **404**; rule failure 403 `messaging_not_allowed` |
| `POST /api/conversations/{id}/messages`, `GET …/messages`, `POST …/read`, `PUT/DELETE …/messages/{id}` | `Messaging.Send` | participant (not left); not restricted; limits | 404 non-participant; 403 restricted; 429 limits |
| `GET /api/messages/search` | `Messaging.Send` | only the caller's conversations | never other users' rows |
| `PUT/DELETE /api/me/blocks/{userId}`, `GET /api/me/blocks` | `Messaging.Send` | self | - |
| `POST /api/messages/{id}/report` | `Messaging.Send` | participant, not the sender | 404 others; 409 duplicate |
| `GET /api/moderation/reports`, `GET/POST …/{id}` | `Messaging.Moderate` | `UnitAccess` at `ModerationUnitPath` | outside scope 404; teacher/student 403 |
| `POST/GET/PUT/DELETE /api/announcements…`, `…/publish`, `…/withdraw`, `…/audience-preview` | `Announcements.Create` / `View` | `UnitAccess` at the authoring unit **and** target-in-scope check; Teacher own ClassCourses | out-of-scope target 404; student 403 |
| `GET /api/me/announcements`, `POST …/{id}/read` | `Announcements.View` | own `AnnouncementRecipient` row | 404 others |
| `GET/POST /api/me/push-subscriptions`, `DELETE …/{id}` | authenticated | own rows | 404 others |
| `GET/PUT /api/me/notification-preferences` (exists) | authenticated | own; mandatory events refused when switched off (409 `notification_mandatory`) | - |
| `GET/PUT /api/comms/messaging-rules`, `GET /api/comms/provider-status`, `GET/PUT /api/comms/settings` | `Comms.Manage` | client root unit | 403 below root |
| `POST /api/integrations/email/events` | anonymous, signature + timestamp | the email relay webhook | bad 401, oversize 413, repeats 429 |
| `POST /api/dev/comms/outbox`, `GET /api/dev/comms/outbox` | Development only | not mapped elsewhere (test proves it) | 404 |

**Isolation:** every new table gets `<Entity>_is_isolated_per_client` (and `_and_subtree` for the unit-scoped ones); conversation tables additionally get **participant isolation tests** (a second user of the same client, a client admin, a platform admin without participation: all get nothing); the bypass allow-list gains four entries with reasons (dispatcher, audience resolver, moderation queue by `ModerationUnitPath`, contact resolver). **Probe rows** for every route in the same PR as the route, milestone constant `M6`, student/guardian callers added to the matrix where the route is theirs (the M4b lesson: the plan lists the expected outcome per caller before the code).

## 10. Contracts (Lead contract waves; each a PR before its BE/FE tasks start)

| Wave | Content |
|---|---|
| **6a Channels** | `NotificationChannels` unchanged; `DeliveryStatuses`, `SendOutcomes` (internal), `NotificationEventDefinition` gains `DefaultChannels` and `Mandatory`; `PushSubscriptionDto`, `SavePushSubscriptionRequest`; `EffectivePreferenceDto` (per event/channel: effective value, source = default/user, mandatory); contact summary for "where we send" (masked address, verified flag); `CommsSettingsDto`; `ProviderStatusDto` per channel; settings `Comms.EmailEnabled/SmsEnabled/PushEnabled`, `Comms.MaxEmailPerDay/MaxSmsPerDay/MaxPushPerDay`, `Comms.QuietHours.Start/End` (+`Enabled`), `Comms.DeliveryRetentionDays`; ProblemCodes `notification_mandatory`, `rate_limited`, `provider_not_configured` (exists), `feature_disabled` (exists); ar/en labels; event `Comms.CapReached`; the delivery template keys per event. Permissions `Comms.Manage`. |
| **6b Messaging** | Permissions `Messaging.Send`, `Messaging.Moderate`; `ConversationDto`, `ConversationListItemDto` (unread, last message, muted), `MessageDto` (+attachments, tombstone state), `CreateConversationRequest`, `SendMessageRequest`, `MessageQuery` (keyset), `MessageSearchResultDto`, `MessagingRuleDto`/`SaveMessagingRulesRequest`, `BlockDto`; routes and per-route permission doc comments; ProblemCodes `messaging_not_allowed` (+reason codes), `conversation_closed`, `message_too_long`; events `Messaging.NewMessage`; SignalR method names; settings `Messaging.ReadReceipts`, `Messaging.MaxPerMinute`, `Messaging.MaxNewConversationsPerDay`, `Messaging.MaxGroupSize`; `RelationshipRules` and built-in defaults version bump. |
| **6c Announcements** | Permissions `Announcements.View/Create`; `AnnouncementDto`, `AnnouncementListItemDto`, `CreateAnnouncementRequest` (+targets), `UpdateAnnouncementRequest`, `AnnouncementTargetKinds`, `AnnouncementStatuses`, `AudiencePreviewDto`, `MyAnnouncementDto`; routes; ProblemCodes `announcement_target_invalid`, `announcement_not_editable`; event `Messaging.Announcement`; `AuditCatalog` entries. |
| **6d Moderation** | `ReportMessageRequest`, `MessageReportDto`, `ReportQuery`, `ResolveReportRequest` + `ModerationActions`, `RestrictionDto`; routes; ProblemCodes `report_duplicate`; `AuditCatalog` entries `MessageReport`, `MessagingRestriction`, `MessagingRule`, `Conversation`; event `Messaging.ReportResolved`. |

Wave 6a merges before Tasks 2-6; 6b before 8-10; 6c before 12-13; 6d before 15. FE contracts follow the same waves (FE tasks are in the M6 frontend proposal, not here).

## 11. Migrations (one per PR, each with a working `Down()`; whoever merges second regenerates on top of `main`)

`AddNotificationDeliveries` (Task 2: deliveries + suppressions), `AddPushSubscriptions` (Task 5), `AddEmailEventReceipts` (Task 3, the platform receipt table, allow-listed like `ZoomWebhookReceipt`), `AddMessaging` (Task 8: conversations, participants, messages, attachments + FULLTEXT), `AddMessagingRules` (Task 9: rules, blocks, restrictions), `AddAnnouncements` (Task 12: announcements, targets, recipients), `AddModeration` (Task 15: reports). FULLTEXT with ngram needs the index created with raw SQL in the migration (EF cannot model it); the model-drift test must not flag it (documented in the task).

## 12. Task breakdown (sizes: S ≤ 0.5 day, M ≈ 1-2 days, L ≈ 3-5 days of one backend agent; lane BE unless stated)

| # | Task | Size | Depends on | Notes / acceptance highlights |
|---|---|---|---|---|
| 1 | **Contract 6a** (channels, preferences, settings, push DTOs) | S | M5 contracts | Lead. Contract tests, labels ar/en. |
| 2 | **Outbox kernel:** `NotificationDelivery`, publisher write path, dispatcher job (claim/lease/classify/backoff), caps and TTL, fake channel, retention job | **L** | 1 | Tests: no delivery without contact; one per (notification, channel); crash during `Sending` re-delivers after lease; backoff schedule; caps hold and release next day; two dispatchers never double-claim (parallel test); isolation. |
| 3 | **Email channel:** `IEmailChannel` over MailKit, ar/en templates, account emails moved onto the outbox (transactional), suppression + bounce webhook with receipts | M | 2 | Provider decision Q1; webhook follows the Zoom checklist (cap, signature, window, receipt). |
| 4 | **SMS:** `ISmsProvider`, fake, one real adapter, UCS-2 length handling, caps, opt-out suppression | M | 2, Q2 | Segments and truncation tests; no body in logs. |
| 5 | **Web Push:** VAPID keys, subscribe/unsubscribe routes, sender, 404/410 cleanup, minimal payload, service-worker contract note for FE | M | 2 | Subscription isolation; payload contains no private data (test). |
| 6 | **Preferences and delivery policy:** catalog defaults/mandatory, effective preference API, contact resolver (minor rule), quiet hours (`Held`), per-client channel switches, provider status | M | 2, 3-5 | Mandatory event cannot be disabled (409); quiet hours across time zones and DST; transactional bypasses. |
| 7 | **Contract 6b** (messaging) | S | 1 | Lead. |
| 8 | **Messaging kernel:** conversations, participants (access list), messages, attachments, unread, read receipts (setting), group rules, limits | **L** | 7 | Participant isolation (admin non-participant 404); direct pair uniqueness under parallel create; keyset paging; attachment validation via the asset path. |
| 9 | **Messaging rules and blocks:** `MessagingRule` matrix + seed defaults, `IMessagingPolicy` using M3 relationships, blocks, restrictions | M | 8 | Matrix of role pairs × relationship (table-driven test); student-to-student off by default; guardian only for own children's teachers. |
| 10 | **Search + realtime + throttled message notifications** | M | 8, 2 | Search never leaves the caller's conversations; Arabic ngram test; one unread notification per conversation. |
| 11 | **Contract 6c** (announcements) | S | 1 | Lead. |
| 12 | **Announcements:** model, targets, scope checks, audience resolver, draft/schedule/publish/expire/withdraw jobs, pin, audience preview | **L** | 11, 2 | Target-in-scope matrix per role (teacher own course only); audience equals the spec for each target kind (incl. guardians, expired role assignments excluded); schedule moves on edit; large audience batches. |
| 13 | **Announcement delivery:** fan-out through the publisher, per-channel counts, read tracking, `GET /api/me/announcements` | M | 12 | Overflow `Held` under caps; read state isolation. |
| 14 | **Contract 6d** (moderation) | S | 8 | Lead. |
| 15 | **Moderation:** reports with frozen context, queue by `ModerationUnitPath`, resolve actions, restrictions enforced on send, audit | M | 14, 9 | A sibling-unit admin never sees the report; moderator cannot read the live conversation; restriction blocks send 403 until it ends. |
| 16 | **Probe-row and isolation suite for M6** (QA) | M | 3-15 | The per-route expectations of section 9 for every caller incl. student/guardian. |
| 17 | **Security review** (QA): outbox claim races, SSRF-free webhooks, secrets in logs, push payload privacy, messaging authorization, announcement scope escapes | M | 15 | Signed off before M6 exit. |
| 18 | **E2E exit demos** (QA): (1) teacher announces to a class, students and guardians get in-app + email (fake) with preferences honoured; (2) student-guardian-teacher conversation under rules, a report, a moderation action | M | all | One E2E stack at a time. |
| 19 | **Dev trigger and docs** (`/api/dev/comms/outbox`, `docs/runbook-comms.md`: relay, SPF/DKIM/DMARC, SMS sender id, VAPID keys, caps) | S | 2-5 | Development-only route + test proving it. |

Critical path: 1 → 2 → (3, 4, 5 in parallel) → 6; 7 → 8 → 9/10; 11 → 12 → 13; 14 → 15; QA 16-18 last. Two backend agents can finish M6 backend in about **3 weeks**; messaging (8-10) and announcements (12-13) are independent after the outbox kernel merges, so they split cleanly. Everything stacks on the Task 2 PR only for the publisher write path (small, reviewed shared-infrastructure change with its own tests).

## 13. Open questions that need an owner decision (defaults in force are the proposal's assumptions)

**Providers and delivery**
1. **Q1 Email relay and bounce capture.** Stay on SMTP/MailKit; which relay (the client's own SMTP, SES, SendGrid, Mailgun, …)? Does it give a bounce/complaint **webhook** (preferred) or only a mailbox? One platform sender domain with SPF/DKIM/DMARC, or per-client domains? *Default: one platform relay with a webhook; one platform "from" domain.*
2. **Q2 SMS provider.** Which gateway for Saudi/MENA numbers (e.g. Unifonic, Taqnyat, Msegat, Twilio)? Who registers the **sender ID** and pays? Expected volume and a monthly budget (drives the daily caps)? One provider in v1 per FR-NOT-03. *Default: adapter behind `ISmsProvider`; the real one chosen by the owner; caps 500/day/client.*
3. **Q3 Which events may use SMS and push?** Proposed SMS-eligible: credentials/security codes, absence notifications to guardians (M5), attendance threshold alerts, urgent announcements; everything else email/push/in-app. Should SMS/push bodies ever contain grades or other student data, or only "you have a new …"? *Default: no student data in SMS/push bodies.*
4. **Q4 Web Push scope.** Is installed-PWA push on iOS (16.4+) and Android in scope for acceptance? VAPID subject (a platform mailto). Notification permission prompt timing is an FE/UX topic. *Default: yes, best effort, no native app (D14).*
5. **Q5 Quiet hours.** Default window (proposal: 21:00-07:00 local) for SMS and push only; per client, per unit or per user? Urgent/mandatory events bypass? Which time zone (client-level `Jobs:TimeZone` today; per unit?) *Default: client-level setting, off by default, urgent bypass.*
6. **Q6 Students as recipients.** Do students receive email/SMS themselves (many have none), or do messages for minors go to the **primary guardian**? Age threshold? Which events are "for the guardian" (absence, grade published, announcements)? *Default: students get in-app and push/email only if they have a verified contact; SMS for a student goes to the primary guardian.*
7. **Q7 Contact verification.** Verify mobile numbers (OTP) and emails before the first SMS/email delivery, or trust imported data? OTP needs the SMS provider itself. *Default: trust data in v1; unverified is shown in the preference page; a later task adds OTP.*
8. **Q8 Mandatory (non-disableable) events.** Proposal: account activation, password reset, security notices, absence notices to guardians (M5), moderation outcomes. Confirm the list; a user can still opt out of SMS entirely by number (legal opt-out).
9. **Q9 Caps and overflow.** Confirm default daily caps per client and whether a broadcast over the cap **holds the overflow until tomorrow** (default) or is **refused** at publish with a count.
10. **Q10 Language of outbound messages.** Use the recipient's own language preference if the profile has one, else the unit default (`Ui.DefaultCulture`)? Is there a user-level language setting today (FE says UI language is per unit/browser)? *Default: unit default until a user setting exists.*
11. **Q11 Retention.** Delivery rows 90 days (no bodies), suppressions until changed; OK? Any statutory retention for communications with minors (PDPL) the owner knows of?
12. **Q12 Branding and sender identity.** Per-client "from name" and logo in email templates (settings), or platform branding only? Reply-to address policy.

**Messaging**
13. **Q13 Default messaging matrix.** Confirm: staff roles → anyone in scope; Teacher ↔ own students and their guardians; Student → own teachers and counselor only, **never other students** (FR-MSG-02 default); Guardian → teachers/counselor of own children only; Counselor ↔ students and guardians in scope. Who edits the matrix (Org Admin at root only, D18 style)?
14. **Q14 Guardians and blocks.** May guardians message each other (siblings' parents)? May a student block a teacher (suppresses nothing in class channels, only direct)? Does a block remove the blocked person from shared **groups**? *Default: no guardian-guardian; block affects direct conversations only.*
15. **Q15 Group conversations.** Who may create groups (staff only by default), maximum size (50), may students be added to groups created by staff, may groups be class-linked automatically (a class conversation per ClassCourse) or only ad hoc?
16. **Q16 Read receipts.** Default off per client? When on, who sees "seen by" (everyone in the conversation, or only staff)? Never shown to students about teachers' reading?
17. **Q17 Attachments.** Allowed types/sizes for messages (reuse `Files.MaxUploadMb`, a stricter message cap such as 10 MB), virus scanning (the `IPackageScanner`-style no-op seam), image previews.
18. **Q18 Edit/delete and retention of messages.** Sender may edit/delete for N minutes (default 15) or any time? What happens to a user's messages when the account is deleted/expired (kept anonymised for the other side)? Retention period for messages of closed years?

**Announcements and moderation**
19. **Q19 Announcements model.** Unify with the per-course `CourseAnnouncement` board (migrate and drop it later) or keep two surfaces? Single body in the author's language or **both ar and en bodies** (the other kinds store `NameAr/NameEn`)? Is an "edited" marker enough after publish, or must edits re-notify? Does org-wide targeting need a second approver?
20. **Q20 Guardians as audience.** When an announcement targets students (class/course/group), do their guardians receive it automatically (`IncludeGuardians` default true for staff-level announcements, false for teacher course posts)? Guardians of students in several targeted groups get one copy.
21. **Q21 Moderation scope.** Confirm only **Org Admin** (matrix: Moderate) reviews, at the reported sender's home unit and above; may Managers view reports (read-only)? How long are frozen contexts kept after resolution (default 180 days)? Penalty menu: dismiss, hide, delete, restrict (N days), lock conversation: complete or too much? Is there an appeal path or a platform-level escalation?
22. **Q22 Feature flag defaults.** `Messaging` default **off**, enabled per unit (like `VirtualClasses`)? Should announcements work with the flag off (they are part of core communication)? *Default: announcements always on; messaging behind the flag.*

## 14. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Provider choice/cost not decided (Q1-Q3) | SMS spend runaway; delivery gaps | Fakes first, caps on by default, per-event SMS allow-list, provider status page; real adapters are small (M) and last. |
| Duplicate or lost sends (crash between provider call and status write) | Double SMS (cost), missed message | At-least-once with lease + provider message id; idempotency key for providers that support it; alert on `Sending` leases expiring repeatedly. |
| Outbox volume (org-wide announcements: thousands of recipients × channels) | DB growth, Hangfire/MySQL throughput | Batched publish job (500), set-based inserts, bounded dispatcher batches, retention 90 days, indexes on `(Status, NextAttemptAtUtc)`; load test in Task 2 with 50k rows. |
| Arabic SMS encoding (UCS-2, 70 chars/segment) | Cost multiplies, truncation | Length-aware renderer and a segment cap per template; tests with Arabic text. |
| Email deliverability (SPF/DKIM/DMARC, shared domain reputation) | Mail lands in spam | Runbook (Task 19), single platform sender, suppression on hard bounce, no marketing content. |
| Privacy: messaging with minors, student data in SMS/push, moderation reading private chats | Legal/PDPL, trust | Participant-only access, reported-context-only moderation, audited moderation reads, no student data in push/SMS bodies, owner questions Q6/Q21. |
| Messaging abuse and spam in groups | Harassment, notification floods | Default-deny rules, student-to-student off, rate/daily caps, one-notification-per-unread-conversation throttle, block + report + restriction. |
| FULLTEXT ngram quality for Arabic | Poor search | Spike inside Task 10 with real Arabic samples; fallback to `LIKE` window; keep search behind the participant join. |
| Quiet hours and time zones/DST | Messages at night or never | Evaluate in the client zone with the `RecurrenceExpander`-style gap/overlap rule, test DST days, `Held` rows have a TTL. |
| Push on iOS/PWA limits | Silent non-delivery | Treat push as best effort, show in-app regardless, subscription cleanup on 404/410, FE docs. |
| Preferences complexity (event × channel × mandatory × client switches) | Surprising behaviour | One effective-preference function with a table-driven test; the preference page shows the **effective** value and its source. |
| Unit-scoped queries vs participant-scoped access | Isolation bugs | Messaging tables tenant-owned with explicit participant checks in one `ConversationAccess` helper (never ad hoc), participant isolation tests, security review item. |
| Shared-infrastructure change in the publisher (Task 2) touches every module's notifications | Regressions | Small PR, the in-app path untouched, the full notification suite as the gate, merged before anything stacks on it. |
| Scope creep (email digests, SMS OTP login, chat reactions/threads) | Schedule | Out of v1, listed below. |

**Out of scope (v1):** email digests/summaries, SMS OTP for login/verification (Q7 later), message reactions, threads, voice/video calls (Zoom covers sessions), message translation, per-client provider credentials, mobile-native push (D14), marketing/bulk email, chatbots, message editing history, e2e encryption.

## 15. PRD / DEV-PLAN points to confirm or amend

- FR-MSG-02 says "student-to-student off by default" but not who edits rules or the default matrix for other pairs: Q13 proposes the matrix of section 6.2 to be written into the PRD as a decision (D58).
- FR-NOT-03 says "SMS (provider abstraction, one provider in v1)": the provider is not named anywhere (Q2); "bounce logging" (Q1) assumes a relay that reports bounces.
- DEV-PLAN M6 lists "Messaging: conversations, rules, UI, receipts (FR-MSG-01/02)": receipts are made optional per client here (Q16) to match "optional read receipts".
- §3.4 lists "Moderate" only for Org Admin; Q21 asks whether Managers see reports read-only.
- The `Messaging` flag exists in §4.4 but no flag covers announcements (Q22).
- `Notification` rows carry only parameters, not rendered text: the email/SMS/push templates are therefore **code (resources)**, which means a template change needs a release; per-client template editing is explicitly out of v1 (Q12).
