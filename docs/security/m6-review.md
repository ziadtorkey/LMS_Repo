# M6 security review checklist: message leaks, safeguarding floor, moderation scope, attachments, notification content, abuse

For the M6 security review (plan Task 32, `docs/superpowers/plans/2026-11-20-m6-communication.md`, PR #726). Derived from the plan (owner decisions D65-D77, Lead decisions L1-L25, the bypass/system-context table and the route/permission/relationship oracle), the QA reviews of #732, #729, #747, #734, #749, #750, #751, #756 and the M5 findings F1-F10 (`docs/security/m5-review.md`, issues #736-#745). Modelled on `docs/security/m5-review.md`. Grows as the tasks merge; the wave-1 rows can start with Tasks 5, 9, 10, 11.

How to use: every row names **what to check**, **how** (an automated test to write or run, or a manual probe) and the **expected result**. Tick a row only with evidence (test name, request/response, or screenshot) in the PR or issue. A row marked **blocked by** depends on a task or an owner answer that has not landed; list it as blocked, do not skip it. Findings are filed as `bug` + `sev:*` against the owning lane (BE1/BE2 delivery and announcements, BE3/BE4 messaging and moderation, FE1/FE2 pages).

Conventions: "participant" = an active `ConversationParticipant` row (a left member reads only up to `LeftAtUtc`, never sends). A participant or relationship miss is an **identical 404** (status, headers, body shape, comparable timing); a missing permission is 403; the `Messaging` flag off is 403 `feature_disabled`. "Non-participant" includes the Org Admin, a moderator, a platform admin, a client admin of another client and a guardian of a participant (L6, D66). "Leak" = any trace of a conversation, message, member, attachment name, unread count or read state the caller may not see: value, count, name, existence, timing.

## 0. Preconditions

| # | Check | How | Expected |
|---|---|---|---|
| 0.1 | Test world: two clients (A, B), two schools in A, a class course with a teacher, two students, a guardian of one student, a guardian of two children, a counselor, a supervisor, a manager, an Org Admin, a user with two roles (Teacher + Guardian), a blocked pair, a restricted user, one direct conversation, one teacher-run group, one manager-owned group, one reported message | `MessageLeakWorld` test kit (Task 31 probe suite) | Seed text and names carry unique marker strings so `Assert.DoesNotContain` finds a leak |
| 0.2 | Open owner questions listed with status | Plan "Remaining owner questions" (D67 email relay, D71 retention pending legal advice, D73 region) | Rows that depend on one are marked blocked or use the default in force |
| 0.3 | The M5 findings that apply to M6 are tracked | F1 (#736), F2 (#737), F3 (#738), F4 (#739) and the rest of #740-#745 | Each is open, fixed or waived with a link; none is silently assumed |

## 1. Participant-only reads (L6, Review Focus 1)

The rule: **`ConversationAccess` is the only gate** for every conversation, message, attachment, member, search, unread and realtime path. No route lists or reads another user's conversation for anyone.

| # | Check | How | Expected |
|---|---|---|---|
| 1.1 | Every route in the plan's route table that takes a conversation, message or attachment id goes through `ConversationAccess` | **Mutation check:** a test that bypasses the helper in one route (reads the entity directly) and fails; plus an architecture test that no endpoint type outside the helper queries `Message`, `ConversationParticipant` or `MessageAttachment` for a user-supplied id | Pass; the mutated build fails `MessageLeakAttackTests` (record the failing test name) |
| 1.2 | Non-participant via every conversation route (`GET` conversation, messages, members, attachment URL; `POST` message, read, members, leave, mute, report) | Callers: another student, the Org Admin, a moderator, a platform admin, an admin of client B, the guardian of a participant, a teacher of the class (not a member), a user who never joined | Identical 404 to a non-existent id on every route; never 403, never an empty 200 that proves existence |
| 1.3 | Id guessing | Messages, attachments and conversations by sequential/other users' ids and by ids of another client | Identical 404; no timing class difference between "exists, not yours" and "does not exist" |
| 1.4 | Left member | Leave, then read before and after `LeftAtUtc`, send, add attachment, mute | Reads up to `LeftAtUtc` only; send and upload 403/404; no member list change visible after leaving |
| 1.5 | Search | `GET /api/messages/search?q=` for a word that exists only in another user's conversation, with `?conversationId=` of a foreign conversation, with paging past the end | No hit, no count, no snippet from a conversation the caller is not in; foreign `conversationId` is 404; `Messaging.SearchPerMinute` (20) gives 429 |
| 1.6 | Unread totals and counters | `GET /api/me/messaging/unread-total`, nav badge, `MessageReceived` after a non-participant's post | Counts only the caller's own participant rows; never a total that includes a left or foreign conversation |
| 1.7 | Member lists (L5, D77) | A guardian and a student read a group's member list | Display name and role label only: no contact data, no linked child or student name, no other class or group membership (`Guardian_member_list_shows_names_and_roles_only`) |
| 1.8 | Error bodies of 403, 404, 409, 422, 429 | Trigger each on a foreign conversation | Body is a `ProblemDetails` with a code and ids only: no message text, name, participant list or conversation title |
| 1.9 | Realtime (L13, `NotificationsHub`) | Anonymous connect; client tries to join another user's group; invoke a method with a conversation or user id; receive after leaving | Anonymous 401; no client-callable group join or id-taking method; events are ids only (no text, names, DTOs) and reach only participants' own user groups after commit; flag off: no messaging method is sent |
| 1.10 | Context switch | Switch active context (X-Lms-Context) between two assignments in different units | Inbox, unread and hub groups reflect only the new context's participant rows; the previous context's counts do not linger in the client state |
| 1.11 | Unread count after sign-out and context change | Client test | `MessagingState` cleared (found in the #734 review) |

## 2. Name enumeration and recipient search (leak item a, L21, D69)

| # | Check | How | Expected |
|---|---|---|---|
| 2.1 | Recipient search | A student types a classmate's name; a guardian types another guardian's; a teacher types a student of another class | The forbidden user never appears; the allowed user **does** appear even when more than the candidate window of refused users match first (found in #747: a cap before the policy filter hides allowed users) |
| 2.2 | `?to=` deep link | `/messages/new?to={id}` for a forbidden user and a non-existent id | Identical reaction and response (status, body, length); neutral reason `blocked_or_rule` only |
| 2.3 | Length and timing | `Recipient_search_and_deep_link_do_not_enumerate_names`: forbidden-name query vs no-match query, N repetitions | Response length equal; timing within tolerance (median, not a single sample) |
| 2.4 | Near-miss rows (`RecipientDto`, hint null) | Student and guardian callers | Visible classes only, no names of users the caller may not message |
| 2.5 | Block tampering | `PUT/DELETE /api/me/blocks/{userId}` for a user the caller could not message; read another user's blocks | Neutral 404; no route reads or removes another user's block; a block and a rule are one reason to the sender |
| 2.6 | Search rate | Enumerate by single letters | `Messaging.SearchPerMinute` 429 with `Retry-After`; min query length 2 |

## 3. Safeguarding floor: the D69 rules matrix (L4, L5, L25, Review Focus 2)

Defaults are the ceiling: `Deny < Relationship < SameUnit < Allow`, `min(stored, default)`, narrowing only, best role pair wins, never a mix.

| # | Check | How | Expected |
|---|---|---|---|
| 3.1 | The defaults equal the plan's D69 table | `DefaultMatrixTests`: the whole matrix compared cell by cell with the table in the plan (found in #747: Manager/Org Admin to Guardian and staff-to-staff defaulted to `Allow` client-wide where the plan says "in scope") | Test passes; a changed default fails the build |
| 3.2 | The API cannot loosen | `PUT /api/comms/messaging-rules` with any cell above its default, an unknown role pair, a stored row looser than default | 400 `rules_looser_than_default` naming the cells, whole request refused; a stored looser or unknown row is ignored at read time |
| 3.3 | Enforcement points (L25) | Create a direct conversation and add a group member under a rule; then tighten the rule, end a relationship, add a block or a restriction, lock the conversation | Created only when `CanStartAsync` / `CanAddToGroupAsync` allows; afterwards every post checks `CanPostAsync`; the conversation becomes read-only for both with history kept and `ConversationChanged` sent |
| 3.4 | Restrictions at every send | A restricted user sends in a direct, a group and a reply | 403 `restricted` on every send; `CanStartAsync` alone does not check restrictions (found in #747: Task 10 must call it, with a test) |
| 3.5 | Multi-role users | A user with Teacher + Guardian reaches a student | Pair by pair; best pair wins; the narrower stored row cannot widen another pair |
| 3.6 | Students and guardians in groups (D74, D77) | Student posts in any group (including a ClassCourse group); guardian posts in a teacher-run group and in a manager/counselor-owned group; student replies privately to the teacher | Student 403 in every group; guardian allowed only in a teacher-run group (reason `group_guardians_read_only` elsewhere); private reply opens the direct conversation under Student to Teacher `Relationship` without quoting the group |
| 3.7 | Groups | Student or guardian creates a group; a group of two with a student and an admin; group size over 50 | 403; 422; 422 `group_full`; the rule is checked owner to member, never pairwise |
| 3.8 | Relationship dates | A guardian link or teacher assignment that ended yesterday or starts tomorrow | `Relationship` rule needs a live link; ended and future links refuse |
| 3.9 | Announcement reply path | Reply to an announcement from a student/guardian | Goes through the same rules; no new route that bypasses them |
| 3.10 | Clock | Rules and relationship dates near midnight in `Asia/Riyadh` | Local days only through `IClientCalendar` (found in #747: a UTC `Today()` must go before the client calendar is used) |

## 4. Moderation scope (D70, L16, Review Focus 3)

| # | Check | How | Expected |
|---|---|---|---|
| 4.1 | Org Admin only | Hold list: who has `Messaging.Moderate`; call every `/api/moderation/*` route as Manager, Teacher, Counselor, Supervisor, platform admin, sibling-unit admin, client-B admin | Only the Org Admin role holds it (D70); others 403; sibling or other client 404 on a report they cannot see |
| 4.2 | Frozen context only | Reported message in a long conversation; moderator opens the report; the conversation later gets new messages and the original is edited/deleted | The moderator sees the reported message plus 5 before and 5 after, names and text only, **snapshot taken at report time**; no route reads the live page; attempt `GET /api/conversations/{id}` with a report id: 404 |
| 4.3 | Reporter anonymity | Report DTO, queue, stats, audit detail, notifications | No reporter id or name anywhere in the moderator view; anonymity by omission is pinned by a probe row (a reporter's marker string never appears) |
| 4.4 | Attachments | Moderator requests an attachment of the reported message, of a non-reported message, of another conversation | Only the reported message's attachments; each read audited (`ModerationRead`); others 404 |
| 4.5 | Audited reads | Report detail, report list, stats, attachment URL | Each writes an audit event in the catalog (`ModerationRead`); a test fails when a moderation GET is not audited (found in #749: the detail GET is not documented as audited) |
| 4.6 | Route set | Enumerate `/api/moderation/*` | Exactly the nine routes of the contract; a new route fails the route-set test |
| 4.7 | Group read-only route | Owner sets a group read-only | Returns no message content; a moderator cannot use it to read |
| 4.8 | Actions | `Dismiss` (bulk), `Remove`, `Warn`, `Restrict` 1/7/30 days, `RemoveAndRestrict`, `LockConversation` | Scope checked per report; UI says "restrict", never "block"; a block is untouched by any action; every status change audited |
| 4.9 | Safety concern | `SafetyConcern` report | Sorts first; its notice is mandatory and urgent; the review target hours shown |
| 4.10 | Reporter's outcome | Reporter, sender, other participants | `MyReportStatus` and `Messaging.ReportResolved` reach the reporter only; never the action or the moderator |
| 4.11 | Retention | Report context 180 days after resolution; removed original 180 days | Retention jobs per client; values are settings (D71); test with the fake clock |

## 5. Attachments under D65 and L12 (Review Focus 7, leak item d)

| # | Check | How | Expected |
|---|---|---|---|
| 5.1 | Upload at attach time | Upload into a conversation the caller left, never joined, is restricted in, or (student in any group / guardian outside a teacher-run group); upload to another author's or a published announcement | 404/403 at attach time, not only at send |
| 5.2 | Pending upload binding | Attach another user's pending upload, or one bound to another conversation; attach after 1 h | 404; pending uploads of 24 h purged (system-context site 8) |
| 5.3 | File types | Polyglot, type-confused, HTML, SVG, XML, JS, archive and macro Office uploads; oversize; > 5 files; renamed extensions | 422 `attachment_invalid` / 413; content sniffed server side; `IFileScanner` called |
| 5.4 | Download | Signed URL after the access check | Valid 5 minutes; reuse after expiry refused; URL of another conversation refused; `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`; inline only jpeg/png/webp |
| 5.5 | Signed URL never leaks | Search logs, audit details, realtime payloads, `Referer` of a following request | `Signed_attachment_url_never_in_logs_referer_or_realtime_payloads` |
| 5.6 | File names | A name with markup or path characters | Rendered as text; no path traversal on storage; no remote image embedding |
| 5.7 | Upload floods | `Messaging.UploadsPerMinute` and `Announcements.UploadsPerMinute` | 429 with `Retry-After` |

## 6. Notification content, delivery and no SMS (L8-L13, D66, D76, Review Focus 4 and 5)

| # | Check | How | Expected |
|---|---|---|---|
| 6.1 | No SMS anywhere (D76, L9) | Search `src`, tests, `*.resx`, settings catalog, DTOs for `Sms`; write an `Sms` outbox row by hand; `PUT /api/me/channels/sms/consent`; an SMS preference in the PUT body | Only the enum value and the guards remain; **no SMS resx key** (found in #756: `Setting_Comms_SmsEnabled`, `Setting_Comms_MaxSms*`, `Prefs_Channel_Sms` linger in the FE stack); the hand-written row is refused and never claimed; consent route 404; preference ignored or 400 (`NoSmsAttackTests`, `No_event_lists_sms`, `No_sms_column_for_any_user`) |
| 6.2 | Push content (L9a) | Render every push template with real events | No message text, student name, grade, reason or address; "New message from {SenderName}" at most; announcement title only when not `Sensitive`; a resource test fails on `{Points}`, `{Body}`, `{Reason}`, `{StudentName}` in push templates |
| 6.3 | Email content | Notification emails for grade, attendance, excuse, message | Nothing the recipient cannot see in the app; never message bodies; `IsNotification` set on every non-account template with a test that fails when unset (found in #728 and #750) |
| 6.4 | Logs and delivery rows | Grep logs and `DeliveryOutbox` after a delivery | Codes and ids only: no address, text, token or provider response body; suppressions hold SHA-256 only; DTOs mask addresses |
| 6.5 | Recipients (L10) | A guardian's child event; a student event | Goes to the guardian's own contact under the guardian's preferences; the resolver never redirects one user's event to another's contact; missing contact writes no row |
| 6.6 | Preferences and consent (L11) | Switch off a mandatory event; enable a channel the event does not offer; opt in to push | 409 `notification_mandatory`; **one rule** for page, PUT and outbox writer (found in #751: `CheckChange`, `Resolve` and the planner disagree); push consent is a server-side `ChannelConsent` record with text version and audit (found in #732: consent is browser-only until Task 9), permission asked only on a click |
| 6.7 | Switches at send time | Turn a channel off or opt out **after** publish, while a row is held or retrying | The row is skipped (`channel_disabled`, `preference_off`) and not sent; mandatory still goes (found in #750) |
| 6.8 | Outbox claim races (system-context site 1) | Two dispatchers, a crashed worker, lease expiry, two nodes at a cap | No double claim; a held row is never lost; a row in `Sending` returns after the lease; caps count sent rows per client day (overshoot of a few rows across nodes is noted); the dispatcher never runs in a platform context |
| 6.9 | Provider not configured | Production host with the defaults | Rows wait visibly (`NotConfigured`), never silently `Sent` or silently expired; `Email:Provider=Log` is an explicit choice; no fake channel registered outside Development/test (found in #750: no provider registered means silent expiry) |
| 6.10 | Mandatory and urgent | Urgent announcement, mandatory event | Skip quiet hours and caps, not suppression; urgent only for authors with unit-level `Announcements.Manage`; push **and** email |
| 6.11 | Quiet hours and DST | Rows held across midnight and `Europe/London` DST | Released at the end of the window in the client zone (`IClientCalendar`) |
| 6.12 | Webhook (L22) | `POST /api/integrations/email/events` | Signature and timestamp required; bad or stale 401, oversize 413, repeats 429, duplicate 200 no-op; the organization comes from the outbox row; one client's bounce never suppresses in another (`Webhook_event_for_client_a_never_suppresses_in_client_b`) |
| 6.13 | Push service worker | `push-sw.js` | No `fetch` handler or cache; `notificationclick` opens only a same-origin app-relative URL (found in #732: `openWindow(data.url)` has no check); local consent cleared on unsubscribe |
| 6.14 | Secrets | SMTP password, VAPID private key, webhook secret | Only in configuration/secret store; a test asserts none appears in logs, DTOs, audit or delivery rows; `P256dh`/`Auth` protected with Data Protection |

## 7. Caching and headers (M5 F1, #736)

| # | Check | How | Expected |
|---|---|---|---|
| 7.1 | **One middleware for `/api`** sets `Cache-Control: private, no-store` unless the route sets its own (closes M5 F1, #736) | `ApiCacheControlMiddlewareTests`: a 200 conversation read, a message list, a search, an unread total, a recipient search, an announcement read, a grade read, an export, and a 401/403/404/429 error | Every response under `/api` carries `private, no-store` (the library reader keeps its own); the check is by **route family**, not by sample: a probe row per route family in the route table asserts the header, so a new route without it fails |
| 7.2 | Attachment downloads | Signed URL response | `Cache-Control: private, no-store`, `Referrer-Policy: no-referrer`, `Content-Disposition: attachment`, `nosniff` |
| 7.3 | No shared cache keyed by URL | Response caching registered? | None registered; a test fails when response caching or output caching is added without a `Vary` and `private` rule |
| 7.4 | `serverNowUtc` | Every timed response | Present (Global Constraints); no client clock decides a window |

## 8. Read receipts (leak item c)

| # | Check | How | Expected |
|---|---|---|---|
| 8.1 | Students see no read state | `Students_never_see_seen_by_or_staff_read_state` | No `SeenBy`, `SeenByCount` or last-read of staff in any student/guardian DTO; a test pins the DTO property names |
| 8.2 | Across participants | `Seen_by_count_never_reaches_a_non_staff_participant`; `Comms` read-receipts setting off | A participant sees their own read mark and, where the setting allows, only what the setting allows; the setting off hides it from everyone but the caller |
| 8.3 | Read marks | `POST …/{id}/read` by a non-participant | Identical 404; `ConversationRead` goes to the caller's other tabs only |

## 9. Abuse limits (L8, L21, Review Focus 8)

| # | Check | How | Expected |
|---|---|---|---|
| 9.1 | Send floods | `Messaging.*PerMinute` | 429 `rate_limited` + `Retry-After`; the draft survives on the client; status announces start and end only |
| 9.2 | Conversation spam | Many new conversations to distinct users | Capped; no enumeration through the cap's response |
| 9.3 | Report floods and duplicates | Same reporter, same message; many reports | 409 `report_duplicate`; report rate cap 429 |
| 9.4 | Announcement author cap | `Announcements.MaxPerAuthorPerDay` (10) | 429 `rate_limited` |
| 9.5 | Test email abuse | `POST /api/me/channels/email/test` | 3 a day, 429; address masked in the response |
| 9.6 | Runaway delivery | A loop publishing to a large audience | Email and push stay under the client caps and the per-recipient limit; overflow held and collapsed to one "N updates" row |
| 9.7 | Platform ceilings | A cap above `Comms:PlatformMax*` | 422 `comms_cap_above_ceiling`; settings only at the client root by `Comms.Manage` |

## 10. Isolation and bypass sites

| # | Check | How | Expected |
|---|---|---|---|
| 10.1 | Every new table has an isolation test | `DeliveryOutbox`, `ContactSuppression`, `PushSubscription`, `ChannelConsent`, `Conversation`, `ConversationParticipant`, `Message`, `MessageAttachment`, `MessagingRule`, `UserBlock`, `MessagingRestriction`, `AnnouncementRecipient`, `MessageReport` (tenant); `Announcement`, `AnnouncementTarget`, `AnnouncementAttachment`, `MessageReportUnit` (unit-scoped); `EmailEventReceipt` (platform, allow-listed) | A test per table; conversation tables also get participant isolation (another user, a client admin, a moderator, a platform admin: nothing) |
| 10.2 | `IgnoreQueryFilters` sites are exactly the plan's list | `IsolationBypassGuardTests` allow-list vs sites 1-9 | Each site carries `// ISOLATION-BYPASS: <reason>` and an allow-list entry; a site not listed fails the guard; a site that the M3 relationship queries serve without a bypass is removed from the list |
| 10.3 | System-context jobs | Dispatcher, publish/expire, retention, orphan-upload cleanup, board migration | Organization from a trusted source, work inside that client's context only, never platform context; foreign-org payload does nothing (`Publish_job_with_a_foreign_org_payload_does_nothing`, `Retention_jobs_purge_only_own_client_rows`) |
| 10.4 | Cross-client reads | Recipient search, policy lookups, audience resolver, moderation queue, contact resolver | `Recipient_search_never_returns_another_clients_user`, `Policy_lookup_never_crosses_clients`, `Audience_never_includes_another_clients_user`, `Client_b_admin_never_sees_client_a_reports`, `Contact_resolution_never_crosses_clients` |
| 10.5 | Settings isolation | Client A raises caps, switches channels | Never affects client B's delivery (`One_clients_settings_never_affect_another_clients_delivery`) |

## 11. Announcements (L14)

| # | Check | How | Expected |
|---|---|---|---|
| 11.1 | Scope | Teacher targets a class they do not teach; manager targets another unit; student authors | Out-of-scope target 404 (`announcement_target_invalid` for a malformed one); student 403 |
| 11.2 | Audience | Audience preview and publish | One copy per person; active users only; guardians default on for unit authors and off for a teacher's course post; the audience never includes another client's user; the recipient snapshot is taken at publish |
| 11.3 | Sanitising | Body with script, event handlers, `javascript:` links, remote images | Sanitised on write and rendered safely; no remote image embedding |
| 11.4 | Reads | Non-recipient reads `GET /api/comms/announcements/{id}` | 404; recipient row required, or author/admin in scope; reach counts visible to authors only |
| 11.5 | Edit and withdraw | Edit after publish; withdraw | "Edited" marker, never re-notify; withdrawn disappears from feeds; not editable states 409 `announcement_not_editable` |

## 12. Findings carried in from the PR reviews

Open items from the QA reviews to confirm closed before sign-off. Each is a row above; this list is the tracker.

| Source | Finding | Row |
|---|---|---|
| #747 | Recipient search caps candidates before the policy filter | 2.1 |
| #747 | Defaults looser than the plan's "in scope"; no `DefaultMatrixTests` | 3.1 |
| #747 | `CanStartAsync` does not check restrictions | 3.4 |
| #750 | Switch and preference not re-checked at send time | 6.7 |
| #750 | No provider registered fails silent (rows expire) | 6.9 |
| #751 | `CheckChange`, `Resolve` and the planner disagree | 6.6 |
| #732 | Push consent is browser-only; notification-click URL unchecked | 6.6, 6.13 |
| #756 | SMS resx keys linger in the FE stack | 6.1 |
| #749 | Detail GET and list not audited; context must be a snapshot; route set not pinned | 4.2, 4.5, 4.6 |
| #728 | `IsNotification` defaults false | 6.3 |
| M5 F1 (#736) | No `Cache-Control: private, no-store` on authenticated API responses | 7.1 |

## Sign-off

The review is signed off only when: every row is ticked with evidence or listed blocked with a reason, section 12 has no open row, `MessageLeakAttackTests`, `ModerationScopeAttackTests`, `MessagingAttachmentAttackTests`, `NoSmsAttackTests`, `NotificationContentTests` and `AbuseLimitTests` exist and pass, and the mutation check of row 1.1 fails when the helper is bypassed. Do not sign off while an M5 finding that M6 depends on (F1 #736, F2 #737) is open.
