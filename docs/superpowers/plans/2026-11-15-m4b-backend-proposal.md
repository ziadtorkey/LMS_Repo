# M4b Backend Proposal: Zoom live sessions, SCORM, H5P

> **Status: DRAFT proposal for the Team Lead (no code).** Written by Backend developer 2 (BE2) in the M4a plan format so the Lead can turn it into the M4b plan, the wave 3 contract PR and the issues. Companion of the "Later: M4b" section of `2026-11-14-m4a-teaching-learning.md`. Owner decisions applied: **D49** (one platform Zoom app, secrets only in server configuration), **D50** (no LTI), **D51** (M4b = Zoom, SCORM, H5P; SCORM/H5P served from a sandboxed origin with a strict CSP **after a security spike**). M4b is sized only after the spike (Task 2) and the Zoom capacity question (Open question Z1) are answered.

**Goal:** A teacher schedules a Zoom session (single or recurring) from a ClassCourse lesson; students see it as a module, get a platform join link inside a join window and a reminder; after the session the teacher attaches (or the platform receives) the recording link and the participants are handed to the M5 attendance module. A teacher uploads a SCORM 1.2 / 2004 package or an H5P file into a lesson; students launch it in a sandboxed player; completion and score flow into module completion and the grade-event outbox. All bilingual, isolated per client, subtree and relationship, with untrusted package content never able to touch the app origin, other clients' data or the network.

**Spec:** `docs/PRD.md` FR-VC-01, FR-STD-01, FR-STD-02, FR-CNT-01 (Live Session, SCORM, H5P kinds; LTI removed by D50), FR-ATT-01 (live sessions as an attendance source), X-02, X-05, X-07, X-08, §4.4 feature flags (`VirtualClasses`, `ScormContent`), D49, D50, D51. Master plan: `docs/DEV-PLAN.md` M4 items 8–10.

---

## 1. What M4b builds on (verified on the open M4a PRs and `main`, 2026-11-12)

| Seam | Where | M4b use |
|---|---|---|
| Module kernel | `LessonModule` (Kind, `RefId`, visibility, target, completion rule), `IModuleAccess`/`ModuleAccess.VisibleTo`, `IModuleWriter.CreateForRefAsync / UpdateMetadataAsync / DeleteForRefAsync` (#391) | `ModuleKinds.LiveSession`, `Scorm` and `H5p` already exist in the contract catalog; each M4b feature creates its module through `IModuleWriter` and reads through `ModuleAccess`. `DELETE /api/modules/{id}` already refuses non-content kinds. |
| Completion | `CompletionRecorder.RecordAsync(module, student, Viewed / Marked / Submitted / Scored, score)` (#391) | Live session: `Viewed` on join. SCORM/H5P: `Scored` with the percentage and `Submitted` on completion. `CompletionRules.ForActivity` returns `[]` for these kinds today, so wave 3 must define their rules. |
| Grade seam | `IGradeEvents.RecordAsync(GradeEventInput)` and `GradeSourceKind {Assignment, Quiz}` (#385) | SCORM/H5P scores need new enum values (`Scorm`, `H5p`): a domain + contract change in wave 3. |
| Files | `IFileStorage` (upload with type/size checks, signed GET/range URL, `OpenReadAsync`, soft delete), setting `Files.MaxUploadMb`; M4a asset service (#349) | Package archives are stored through `IFileStorage`; extracted package files need a **prefix store** (many small objects) that `IFileStorage` does not offer: Task 6 adds a narrow `IPackageStore` over the same MinIO client. |
| Jobs | `IBackgroundJobs.Enqueue / EnqueueAsSystem / AddOrUpdateRecurring` | Validation/extraction jobs, Zoom reminders and polling. There is **no delayed or scheduled enqueue** yet: Task 5 adds `ScheduleAsSystem(TimeSpan/DateTimeOffset, job)` to `IBackgroundJobs` (small, reviewed infrastructure change). |
| Notifications | `INotificationPublisher`, event catalog in `NotificationEvents` | Wave 3 adds `Live.Reminder` (named recipients, params Title/Course/StartsAtUtc). |
| Settings and flags | `SettingKeys`, `FeatureFlags.VirtualClasses`, `FeatureFlags.ScormContent` (catalog exists, default false) | Both flags gate the creation endpoints (404-by-flag is not used elsewhere: an unlicensed unit gets 403 `feature_disabled`, to be confirmed with the Lead, Q4). |
| Security headers | `SecurityHeadersMiddleware` sends `X-Frame-Options: DENY` and `Content-Security-Policy: frame-ancestors 'none'` on every response | The package host must send its own headers (CSP, `frame-ancestors <app origin>`, no `X-Frame-Options: DENY`); the middleware needs a per-host policy hook (Task 6). |
| Hosting | One ASP.NET host serves the Blazor WASM client and `/api` from one origin | A second origin for untrusted content is a deployment decision (spike S1, Q-S1). |

## 2. Decisions this proposal assumes (confirm in review)

1. **Zoom (D49):** one platform Server-to-Server OAuth app; `ZoomOptions` (account id, client id, secret, host pool, webhook secret) bound from configuration/secret store only. No per-client Zoom settings, tables or credentials. Sessions are tagged with the client and class course in the meeting topic/`tracking_fields` for support, never trusted as authorisation.
2. **Zoom host model:** a Zoom meeting needs a licensed host user. Assumed default: a **configured pool of platform host users**; each session gets a host chosen by an allocator that refuses overlapping use of one host (see Z2). The alternative (the teacher's own Zoom user) needs every teacher to hold a licence in the one account and is not proposed.
3. **Zoom events:** recordings and participants are learned from **Zoom webhooks** (`meeting.ended`, `recording.completed`) verified by the secret token and a timestamp window, with a **polling fallback** (Reports/Recordings API) for a missed webhook. If the platform has no public HTTPS endpoint for webhooks (Q-Z3), polling is the only path and the teacher can paste the recording link by hand (FR-VC-01 allows "recording link attached after the session").
4. **Packages:** SCORM and H5P share one **package kernel** (upload, hardening, validation job, storage layout, serving token); the two formats differ only in validator, launch resolution and tracking adapter. Single-SCO SCORM first; multi-SCO/sequencing is out of v1 (Q-S5).
5. **Untrusted content never runs on the app origin** (D51). The recommended layout is a **separate site (host name) for package content** with a strict CSP and an iframe `sandbox` without `allow-same-origin`, per-launch signed paths instead of cookies (S1). The path-on-the-app-origin layout is the documented fallback only if the owner cannot provide a second host name.
6. **Completion and grades:** a SCORM/H5P attempt writes completion through `CompletionRecorder` and a grade event through `IGradeEvents` exactly like quizzes (revision only when the final score changes).
7. **Live session attendance:** M5 owns attendance. M4b exposes `ILiveSessionAttendanceSink` (an interface in `Lms.Domain`, no implementation yet) and writes participant rows; M5 reads them. No attendance table in M4b.

## 3. Entities (all unit-scoped like M4a: unit fields copied from the class of the ClassCourse, `IOrgUnitScoped`, repather-covered, an isolation test each)

| Entity | Notes |
|---|---|
| `LiveSessionSeries` (audited) | `ClassCourseId`, `LessonKey`, recurrence rule (weekly, interval, `UntilUtc`/`Count`, max 52 occurrences), `HostKey`. The authoring unit of a recurring session. |
| `LiveSession` (audited) | `SeriesId?`, `ClassCourseId`, `ModuleId`, `Provider` (`Zoom`), `ExternalMeetingId`, `StartsAtUtc`, `DurationMinutes`, `Status` (`Scheduled`, `Live`, `Ended`, `Cancelled`, `SyncFailed`), `HostUserKey` (the pool host, never exposed), `JoinUrl`/`StartUrl` held **encrypted** (data protection) and never returned in lists, `RecordingUrl?`, `RecordingSource` (`Provider`/`Manual`), `ReminderSentAtUtc`. Unique `(ExternalMeetingId)`; unique live key on `(HostUserKey, slot)` is **not** used: overlap is checked by the allocator under a lock (Z2). |
| `LiveSessionParticipant` (not audited, volume) | `SessionId`, `StudentId?`, `UserId?`, `DisplayName`, `JoinedAtUtc`, `LeftAtUtc`, `DurationSeconds`, `MatchState` (`Matched`, `Guest`). Matching by email/user id from the Zoom report; unmatched stay `Guest` and are never attached to a student guess. |
| `ZoomWebhookReceipt` (not audited) | `EventId`, `ReceivedAtUtc`, `SignatureOk`, `Processed`: idempotency and replay protection. Not unit-scoped (platform data): the one table that is **not** `IOrgUnitScoped`, justified like the audit tables, with a retention job. |
| `PackageUpload` (audited) | `ClassCourseId`, `ModuleId?`, `Kind` (`Scorm`, `H5p`), `FileId` (archive in `IFileStorage`), `Status` (`Uploaded`, `Validating`, `Ready`, `Rejected`), `RejectReason` (code), counters (entries, uncompressed bytes), `StorePrefix`, `Sha256`. |
| `ScormPackage` (audited) | `UploadId`, `Version` (`Scorm12`, `Scorm2004`), `Title`, `LaunchHref`, `ManifestSha256`, one `ScormSco` row (identifier, href, mastery score). |
| `H5pContent` (audited) | `UploadId`, main library, title, `LaunchPath`, `ContentSha256`. |
| `PackageAttempt` (not audited) | `PackageId`, `StudentId`, `Number`, `Status`, `CmiJson` (hard cap 64 KB), `ScoreRaw/Min/Max`, `TotalTimeSeconds`, `LastCommitAtUtc`, `CommitCount`. Unique `(PackageId, StudentId, Number)`. One row for SCORM (the CMI data model) and H5P (last xAPI result). |
| `PackageLaunchToken` | not stored: a signed, expiring token (data protection) carrying package id, student id, attempt id, issued/expires, nonce; replay limited by expiry and attempt binding. |

`AuditCatalog` gains `LiveSessionSeries`, `LiveSession`, `PackageUpload`, `ScormPackage`, `H5pContent`. Wave 3 also extends `GradeSourceKind`.

## 4. Endpoints (every route gets probe rows in the same PR; staff routes `RequirePermission` + `RelationshipAccess.CheckClassCourseAsync`, a miss is 404)

**Live sessions** (`LiveSessions.Manage` for writes, `LiveSessions.View` for reads; Teacher own class courses, Student own enrollments, D42)
- `GET/POST /api/class-courses/{id}/live-sessions` (paged list; create single or recurring with module settings `ModuleSettingsInput`, creates the module through `IModuleWriter`)
- `GET/PUT/DELETE /api/live-sessions/{id}` (update reschedules at Zoom; delete cancels the meeting; a series edit has `scope=This|ThisAndFollowing`)
- `POST /api/live-sessions/{id}/join` → `{ url, expiresAtUtc, serverNowUtc }`: students only inside the join window (`Live.JoinWindowMinutes` setting, default 15 before start until end), staff get the host start URL (Manage only). Records `CompletionSignal.Viewed`. 404 for an invisible module (D51), 409 `live_session_not_open` outside the window.
- `PUT /api/live-sessions/{id}/recording` (manual link, http/https only) ; `GET /api/live-sessions/{id}/participants` (staff, paged)
- `POST /api/integrations/zoom/webhook` (anonymous by design, **signature-verified**, idempotent by event id; the only anonymous M4b route, listed in the probe matrix with its own checks)
- `GET /api/live-sessions/provider-status` (staff): `{ configured, healthy }` so the UI can say "ask your platform administrator" without exposing configuration.

**Packages** (`Packages.Upload` for writes, `Packages.View` for reads)
- `POST /api/class-courses/{id}/packages` (multipart; type and size from `Files.MaxUploadMb`, answers 202 with `PackageUploadDto{ status: Uploaded }`; validation runs as a system job), `GET /api/packages/{id}` (status, reject reason, manifest title), `POST /api/packages/{id}/module` (attach to a lesson through `IModuleWriter`, only when `Ready`), `DELETE /api/packages/{id}`.
- `POST /api/packages/{id}/launch` (student, visible module only) → `{ launchUrl, attemptId, serverNowUtc }` where `launchUrl` is on the package host with a signed path.
- `POST /api/package-attempts/{id}/commit` (the runtime shim posts CMI/xAPI data; rate limited per attempt, size capped, key allow-listed) and `POST /api/package-attempts/{id}/finish`.
- **Package host** (a separate minimal pipeline, not `/api`): `GET /p/{token}/{**path}` streams extracted files with the strict headers; no cookies, no auth header, the token is the only credential.

## 5. Permissions, flags, relationship, audit

- **Wave 3 permissions:** `LiveSessions.{View, Manage}`, `Packages.{Upload, View}`. Matrix proposal following M4a: OrgAdmin and Manager `View, Manage/Upload` (so the D16 escalation guard stays satisfied, as D52 did for `Modules.Manage`), Supervisor `View`, Teacher `View, Manage/Upload` restricted to **own class courses** (relationship rules), Student `View` restricted to **own enrollments**, Counselor none, Guardian none.
- **Flags:** `FeatureFlags.VirtualClasses` gates every live-session write and `join`; `FeatureFlags.ScormContent` gates package upload and launch (H5P rides the same flag unless the Lead wants a separate `H5pContent` flag, Q4). Reads of existing sessions/packages are not gated.
- **Audit:** the five entities above; participants, attempts and webhook receipts are not audited row by row. Package rejects and webhook signature failures are logged as security events (`AuditSecurityEvents`) without payloads.
- **Secrets:** `Zoom:*` only in configuration; startup validation fails fast **only when VirtualClasses is enabled for any unit**; the provider reports `configured=false` otherwise. No secret in logs, DTOs, audit or the database.

## 6. Zoom design

- `IMeetingProvider` (Domain): `CreateMeetingAsync`, `UpdateMeetingAsync`, `DeleteMeetingAsync`, `GetParticipantsAsync`, `GetRecordingsAsync`, `GetHealthAsync`; `ZoomMeetingProvider` in Infrastructure; `FakeMeetingProvider` for tests and local runs.
- **Auth:** S2S OAuth `account_credentials` grant; the token is cached until shortly before expiry (singleton, one refresh in flight), never logged.
- **Rate limits and capacity (D49 risk):** one account serves every client. All calls go through a **single outbound limiter** (token bucket per Zoom rate-limit class, honouring `429 Retry-After`), a **bounded queue** and a per-client daily create cap (setting) so one client cannot starve the rest. When the provider is down, rate limited or the host pool is exhausted, create/update answer `503`/409 `provider_unavailable` with `Retry-After`; **the session row is kept in `SyncFailed`** so the teacher sees it and a recurring job retries (no silent loss).
- **Hosts and concurrency (Z2):** the allocator picks a pool host with no overlapping live session (buffer 10 minutes) under a database lock; concurrent creates for the same slot are serialised and the loser gets another host or `provider_unavailable`. Zoom's per-licence concurrent-meeting rule is the hard limit.
- **Recurring:** the platform owns the series: it creates N individual meetings (max 52, one per occurrence), not a Zoom recurring meeting, so each occurrence keeps its own host allocation, join window, reminder and recording. Editing "this and following" recreates the future meetings.
- **Join links:** students never receive the raw Zoom URL in a list; `POST .../join` returns it only inside the window. Registration/waiting-room settings are fixed by the platform (waiting room on, join before host off, no cloud recording unless the unit enables it).
- **Reminders:** `Live.Reminder` through a scheduled system job at `Live.ReminderMinutes` (default 30) before start, to the module's targeted students; re-scheduled on update, cancelled on delete, idempotent via `ReminderSentAtUtc`.
- **After the session:** webhook (or polling) → status `Ended`, participants import (`MatchState`), recording link attach; then `ILiveSessionAttendanceSink` is called for M5 (a no-op registration until M5).

## 7. SCORM and H5P design (subject to the spike)

- **Upload and hardening (shared):** archive size ≤ `Files.MaxUploadMb`; **streamed** extraction counting real bytes (never trusting headers); caps proposed for the spike to confirm: ≤ 10 000 entries, ≤ 1 GB uncompressed, per-entry ratio ≤ 100:1 and total ratio ≤ 50:1, path depth ≤ 20, name ≤ 255. Reject: entries that normalise outside the root (`..`, absolute paths, drive letters, backslashes), symlinks/hardlinks, duplicate names after case folding, device/reserved names, executables and server-side scripts (`.exe .dll .so .php .jsp .asp .cgi`…), encrypted entries. Extraction runs in a system job in a temp area, then uploads objects under `packages/{orgId}/{packageId}/` with **server-assigned content types from an extension allow-list** (never the zip's claim, no HTML sniffing).
- **SCORM:** `imsmanifest.xml` parsed with DTD processing **prohibited** and a size cap (XXE/billion laughs); version detected (1.2 vs 2004 3rd/4th ed.); organisations/resources resolved, the launch `href` must exist inside the package; sequencing and multiple SCOs rejected in v1 (`package_invalid`, Q-S5). The runtime API (`API` / `API_1484_11`) is a small client shim inside the launch page's parent frame forwarding to the commit endpoint by `postMessage`; the server keeps a **key allow-list** and size caps, maps `cmi.core.lesson_status` / `completion_status` / `success_status` and `score.raw/min/max` to completion and grade.
- **H5P:** `h5p.json` and library folders validated (size/depth/extension rules above); the player is a **vendored, pinned h5p-standalone** build served from the package host (no CDN); xAPI statements arrive by `postMessage` and only `completed/passed/scored` results are kept (capped). A full H5P server (PHP) is explicitly not used.
- **Serving (S1 recommended):** `https://<content-host>/p/{token}/…` with `Content-Security-Policy: default-src 'none'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors <app origin>`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cross-Origin-Resource-Policy: same-site`-style isolation as the spike decides, **no cookies**, `Cache-Control: private`, range support; iframe `sandbox="allow-scripts allow-forms"` (no `allow-same-origin`, no top navigation, no popups). `unsafe-eval` is the open item (Q-S3).
- **Cross-tenant paths:** the token binds package id + attempt + student; the store key is built from the token's package id only; any path component is validated against the package's file index, so a path never selects another package or client.

## 8. Tasks (one issue and one PR each; lanes as in M4a; numbering independent of the M4a plan)

### Task 1: Contract, wave 3 (Lead contract)
**PRD:** FR-VC-01, FR-STD-01/02, D49–D51. **Depends on:** M4a contracts merged. Permissions `LiveSessions.*`, `Packages.*` and the matrix/relationship rules; DTOs and routes for sections 3–4; `CompletionRules.ForActivity` for `LiveSession` (None, Manual, View) and `Scorm`/`H5p` (Submit, Score); `GradeSourceKind.Scorm/H5p`; `ProblemCodes` (`provider_unavailable`, `live_session_not_open`, `package_invalid`, `package_too_large`, `package_not_ready`, `feature_disabled`); `NotificationEvents.LiveReminder`; settings (`Live.JoinWindowMinutes`, `Live.ReminderMinutes`, `Live.MaxCreatesPerDay`, `Packages.MaxEntries`, `Packages.MaxUncompressedMb`, `Packages.CommitMinIntervalSeconds`); `AuditCatalog`; ar/en labels; contract tests.

### Task 2: Security spike, SCORM/H5P hosting (QA + BE2)
**Deliverable:** an ADR (`docs/adr/00xx-package-hosting.md`) plus a throw-away proof of concept (not merged to `main` code paths) answering the spike questions in section 9: host-name vs path layout, CSP and sandbox set, `unsafe-eval` need with three real SCORM packages and two H5P files, token and cache design, zip limits against a crafted-archive corpus (zip-slip, bombs, symlinks, polyglots), commit rate limits, H5P player choice. **Task 6–8 are not sized or started before this lands.**

### Task 3: Meeting provider kernel and live session authoring (BE)
`LiveSession`, `LiveSessionSeries`, `IMeetingProvider`, `FakeMeetingProvider`, `ILiveSessionAttendanceSink`, authoring endpoints through `IModuleWriter`, `VirtualClasses` gate, closed class/year guard, isolation tests. Tests first: `Teacher_schedules_single_session_with_module`; `Recurring_creates_one_session_per_occurrence_max_52`; `Student_sees_only_visible_session_modules`; `Teacher_of_other_class_course_gets_404`; `Flag_off_refuses_writes`; `Closed_class_rejects_writes`; `LiveSession_is_isolated_per_client_and_subtree`.

### Task 4: Zoom provider (BE)
S2S OAuth, token cache, host-pool allocator with overlap lock, outbound limiter and queue, `provider_unavailable`, `SyncFailed` recovery job, secrets handling, provider status endpoint, `ZoomOptions` validation. Tests with a stubbed HTTP handler: `Token_is_cached_and_refreshed_once_under_concurrency`; `Rate_limited_call_honours_retry_after`; `Host_pool_never_double_books_a_slot_under_concurrency`; `Provider_down_keeps_the_session_in_sync_failed_and_the_sweep_retries`; `Secrets_never_appear_in_logs_or_dtos`; `Client_daily_cap_protects_other_clients`.

### Task 5: Live session lifecycle: join, reminders, webhooks (BE)
Join endpoint and window, `Viewed` completion, `IBackgroundJobs.ScheduleAsSystem` (infrastructure addition), reminder job and `Live.Reminder`, webhook endpoint (signature, timestamp window, idempotency receipt table, polling fallback), participants import, recording link, attendance sink call. Tests: `Join_outside_window_is_409_and_invisible_module_is_404`; `Student_never_receives_the_start_url`; `Webhook_with_a_bad_signature_or_old_timestamp_is_rejected`; `Replayed_webhook_is_processed_once`; `Participants_match_by_user_and_unknown_stay_guests`; `Reminder_is_sent_once_and_rescheduled_on_update`.

### Task 6: Package kernel (BE, after the spike)
Upload, streamed hardening, validation job and states, `IPackageStore`, serving token, package host pipeline and headers (per-host security headers hook in `SecurityHeadersMiddleware`), quotas, `Packages.*`, `ScormContent` gate. Tests: a crafted-archive corpus (`Zip_slip_entry_is_rejected`, `Decompression_bomb_is_rejected_by_real_bytes_not_headers`, `Symlink_entry_is_rejected`, `Too_many_entries_is_rejected`, `Executable_extensions_are_rejected`, `Duplicate_after_case_folding_is_rejected`), `Package_path_never_leaves_its_package`, `Token_from_another_attempt_or_client_is_refused`, isolation tests.

### Task 7: SCORM (BE)
Manifest validation (XXE-safe), version detection, launch resolution, runtime shim contract, commit endpoint with allow-list/size/rate limits, completion and `Scored`/`Submitted` signals, grade event revisions, attempt handling. Tests: `Manifest_with_external_entities_is_rejected`; `Scorm12_and_2004_lesson_status_map_to_completion`; `Commit_over_size_or_rate_is_refused`; `Unknown_cmi_keys_are_dropped`; `Score_writes_a_grade_event_once_per_change`; `Student_cannot_launch_an_invisible_module`.

### Task 8: H5P (BE)
`h5p.json` validation, library checks, vendored player assets on the package host, xAPI endpoint, score and completion. Tests: `Invalid_h5p_json_is_rejected`; `Xapi_result_maps_to_score_and_completion`; `Statements_of_other_attempts_are_refused`; isolation tests.

### Task 9: M4b probes and the security checklist (BE + QA)
Probe rows for every route (including the webhook and the package host), the checklist in `docs/` (zip, XXE, CSP, token, cross-tenant), isolation tests per table, mutation checks on the visibility and token paths.

*Frontend (separate proposal):* schedule dialog and join states, package upload/status, sandboxed player with the `postMessage` shim. *QA:* package security review and two E2E demos (live session with the fake provider; SCORM package completing a module).

## 9. Open questions

**For the security spike (Task 2):**
- **S1 Origin layout.** Separate host name (cookie-less, own site) vs a path on the app origin with CSP `sandbox`. Who provisions DNS/TLS/ingress? Does the deployment allow a second host name in dev and CI (E2E)?
- **S2 Sandbox flags** that real SCORM/H5P need (`allow-scripts`, `allow-forms`, `allow-popups` for external links?) and whether anything requires `allow-same-origin`.
- **S3 CSP vs real content.** How many packages need `unsafe-eval`, inline handlers, `data:`/`blob:` workers, or external fonts/CDNs? Policy for packages that fail a strict CSP: reject at upload or run with a relaxed CSP (never network access)?
- **S4 Tracking transport.** `postMessage` shim from the player frame vs same-site fetch with the token; commit rate and size limits that real players survive; behaviour on tab close (`sendBeacon`).
- **S5 SCORM scope.** Single-SCO only? Sequencing/navigation rules and multi-SCO courses rejected or ignored? SCORM 2004 editions supported.
- **S6 Zip limits** (entries, uncompressed size, ratios, depth) against the largest legitimate packages, and where extraction runs (job memory/disk, temp area cleanup).
- **S7 Malware.** Is an antivirus scan (ClamAV) required before `Ready`? Content-type allow-list contents; are Flash/Java legacy assets rejected?
- **S8 H5P player.** Pinned h5p-standalone vs another embedded player; offline library sets vs "libraries inside the package"; licence check of every shipped asset.
- **S9 Token design.** Lifetime, binding (attempt, student), single launch vs reusable, caching headers, and revocation when a module is hidden or the student is unenrolled mid-attempt.

**For the Lead / owner:**
- **Z1 Zoom plan capacity.** One account: licensed host count, concurrent meeting and participant limits, API rate-limit class. Must be confirmed before Task 4 is sized.
- **Z2 Host model.** Platform host pool (assumed) vs one host user vs teacher accounts; who administers the pool and how a teacher is told no host is free.
- **Z3 Webhooks.** Does the deployment expose a public HTTPS endpoint Zoom can call? If not, polling plus manual recording links only.
- **Z4 Recording policy.** Cloud recording on by default, per unit setting, or manual only; retention and who may view (privacy for minors).
- **Z5 Participant matching.** Match by email/user id only, or require a registrant model (changes the join flow)? Guests are never attached to students automatically.
- **Q4 Feature flags.** Disabled flag answer (403 `feature_disabled` vs 404) and whether H5P needs its own flag.
- **Q5 Per-client caps.** Default `Live.MaxCreatesPerDay` and package quotas per client.

## 10. Risks

- **Shared Zoom account (D49):** noisy clients, licence exhaustion and rate limits are shared; mitigated by the limiter, caps and explicit `provider_unavailable`, not removable.
- **Untrusted packages are the largest attack surface of the product:** hosting, zip handling, XML parsing and third-party JS each need QA review; a bug here can reach other clients. The spike and the checklist are gates, not formalities.
- **Hosting dependency:** the recommended origin layout needs infrastructure outside the repository; without it the fallback weakens isolation and needs a stricter review.
- **Scheduling infrastructure gap:** the missing delayed-job API is small but touches shared infrastructure; it ships in Task 5 with its own tests.
- **M5 coupling:** participant data and attendance are consumed by M5; the sink interface must be agreed before M5 starts.

## 11. PRD / plan notes found while writing

- FR-CNT-01 still lists "LTI tool" (removed by D50) and DEV-PLAN M4 item 11 likewise.
- FR-VC-01 says "participants imported into attendance" but attendance is M5 (FR-ATT-01): M4b stops at the sink interface and the participant rows.
- `CompletionRules.ForActivity` (wave 2) is empty for `LiveSession`, `Scorm` and `H5p` by design; wave 3 must fill it or those kinds cannot be created.
- `GradeSourceKind` has only `Assignment` and `Quiz`; SCORM/H5P grade events need two new values.
- The PRD §4.4 flag `ScormContent` exists in the catalog; there is no separate H5P flag.
