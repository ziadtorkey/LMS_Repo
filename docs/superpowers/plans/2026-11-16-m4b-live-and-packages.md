# M4b Live Sessions & Packages Implementation Plan

> **Status: DRAFT for review (Team Lead, 2026-11-13).** Merges the QA-approved backend proposal (PR #427, `lead/m4b-backend-proposal` @ `1a870a27`, BE2/BE3), the QA-approved frontend proposal (PR #440, `lead/m4b-fe-proposal` @ `30f9f415`, FE2) and the package-host spike (PR #444, `docs/spikes/2026-11-12-m4b-package-host.md`, BE4) into one task list for **M4b** (D51). Owner decisions applied: D49 (one platform Zoom app), D50 (no LTI), D51 (M4 split, sandboxed package origin after a QA spike), **D53** (platform Zoom host pool), **D54** (public webhook endpoint, polling fallback), **D55** (cloud recording off by default, per-unit enable), **D56** (separate registrable domain for package content, no app-origin fallback). Lead technical decisions: SCORM shim **Design A** (injected into the launch document, framesets rejected, `frame-src 'none'` on the content host), and the defaults for the still-open Z1, Z5, Q4 and Q5 listed under "Decisions" (each is configuration and can change without a code change). Team: Lead, BE1–BE4, FE1, FE2, QA1–QA3, UI/UX Designer. GitHub issues are created by the Lead after this plan is reviewed.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. In this project each task = one GitHub issue and one PR; the lane (Lead contract / BE1–BE4 / FE1–FE2 / QA1–QA3 / UX) is marked on each task.

**Goal:** A teacher schedules a Zoom session (single or recurring) on a lesson of their own ClassCourse; students see it as a module, get a reminder and a platform join link inside a join window; after the session the recording link arrives by webhook (or polling, or the teacher pastes it) and the participants are stored for M5 attendance. A teacher uploads a SCORM 1.2 / 2004 (single-SCO) package or an H5P file into a lesson; the platform validates it in a hardened background job; students launch it in a sandboxed iframe served from a separate registrable domain; completion and score flow into module completion and the grade-event outbox. All bilingual (ar RTL / en), isolated per client, subtree and relationship; untrusted package content can never touch the app origin, other clients' data or the network.

**Architecture:** Same modular monolith. Two new feature areas plug into the M4a kernels: **Live sessions** (`Domain/LiveSessions`, `Infrastructure/LiveSessions`, `Features/LiveSessions`) behind `IMeetingProvider` with a `ZoomMeetingProvider` (`Infrastructure/Zoom`) and a `FakeMeetingProvider`; **Packages** (`Domain/Packages`, `Infrastructure/Packages`, `Features/Packages`) with one package kernel (upload, streamed hardening, validation job, `IPackageStore`) and two format adapters (SCORM, H5P). Untrusted content is served by a **separate host-matched pipeline** (`Lms.Api/PackageHost/`) answering only on the configured content origin with its own headers and signed path tokens. Both features create modules through `IModuleWriter`, read through `IModuleAccess`, write completion through `ICompletionRecorder` and scores through `IGradeEvents` (M4a seams; never by editing `Infrastructure/Content`).

**Tech Stack:** unchanged from M4a plus: Zoom Server-to-Server OAuth REST API (typed `HttpClient`, no SDK); a **vendored, pinned h5p-standalone** build served from the content host (never a CDN; licence checked in the ADR); `System.IO.Compression` streamed with real-byte counting; Hangfire scheduled jobs (`IBackgroundJobs.ScheduleAsSystem`, new). No new client packages.

**Spec:** `docs/PRD.md` FR-VC-01, FR-STD-01, FR-STD-02, FR-CNT-01 (Live Session, SCORM, H5P kinds), FR-ATT-01 (live sessions as an attendance source, consumed in M5), X-01, X-02, X-05, X-07, X-08, X-10, §4.4 feature flags (`VirtualClasses`, `ScormContent`), decisions D42, D49–D56. Master plan: `docs/DEV-PLAN.md` §2 M4 items 8–10 (item 11, LTI, dropped by D50). Proposals: `2026-11-15-m4b-backend-proposal.md` (#427), `2026-11-15-m4b-frontend-proposal.md` (#440); spike `docs/spikes/2026-11-12-m4b-package-host.md` (#444). Previous plan: `2026-11-14-m4a-teaching-learning.md`.

## Scope

**In scope**
- Live sessions: single and recurring (weekly, max 52 occurrences) Zoom meetings per lesson module; platform host pool allocator; join window and join link; in-app reminder; recording link from Zoom (webhook or polling) or pasted by the teacher; participant import with safe matching; `ILiveSessionAttendanceSink` seam for M5; provider status for the UI.
- Packages: SCORM 1.2 and 2004 (single SCO) and H5P upload; streamed zip/XML/JSON hardening; validation job with time/memory/disk budgets; per-client storage quota; content host on a separate registrable domain with CSP `sandbox`, signed path tokens and revocation; SCORM API prelude (Design A); H5P via vendored h5p-standalone; port-based tracking bridge with `seq`; teacher preview (records nothing); completion and grade events.
- FE for both, the M4b part of Home and course home, accessibility/RTL hardening; QA probe suite, package-host security review, two E2E exit demos.

**Out of scope (v1)**
- LTI 1.3 (D50); Microsoft Teams or any second provider (the abstraction stays ready); per-client Zoom credentials or a provider settings screen (D49).
- Zoom Web SDK embedded in the page (join always opens a Zoom link in a new tab); Zoom registrants; email/SMS reminders (in-app only; M6 channels).
- Attendance records and screens (M5 consumes the participant rows through the sink).
- Multi-SCO packages, SCORM sequencing and navigation, framesets / nested-frame SCOs (**Design B** per-launch origins is not built; such packages are rejected with `needs_nested_frames`).
- H5P authoring or a PHP H5P server; a package editor; teacher reports of every student's package attempts (M5 reports).
- An app-origin fallback for package content (D56); relaxing `connect-src` or adding `allow-same-origin` for any package.

## What M4b builds on (to re-check on `origin/main` when each task starts)
- **M4a kernels** (merged or in final review): `LessonModule`, `IModuleAccess`, `IModuleWriter.CreateForRefAsync / UpdateMetadataAsync / DeleteForRefAsync` (#391), `ICompletionRecorder` (#391), `IGradeEvents` + `GradeSourceKind {Assignment, Quiz}` (#385), asset service and `IFileStorage` (#384/#349), `IBackgroundJobs.Enqueue / EnqueueAsSystem / AddOrUpdateRecurring` (#434), `INotificationPublisher`, `SettingKeys`, `FeatureFlags.VirtualClasses` / `ScormContent` (default off), `SecurityHeadersMiddleware` (`X-Frame-Options: DENY`, `frame-ancestors 'none'`).
- **Contract catalog:** `ModuleKinds.LiveSession`, `Scorm`, `H5p` already exist; `CompletionRules.ForActivity` returns `[]` for them (filled by Tasks 1 and 2); `BuiltInRoleDefaults.CurrentVersion = 5`.
- **FE (M4a):** `AddModuleMenu`, `ModuleDialog`, `ModuleMetadataForm` (#389), `ModuleViewerPage` / `ModuleBody` (#394), `AssetUploader` / `ApiHttp.UploadAsync` (#387), `MyCoursePage`, Home cards, `NotificationText` parity, dialog focus (#418).
- **Prerequisite:** M4a exit (Tasks 29–33 of the M4a plan) green on `main` before the M4b exit demo; M4b code may start on any merged seam.

## Decisions

| # | Decision | Source |
|---|---|---|
| 1 | One platform Server-to-Server OAuth Zoom app; `Zoom:*` options (account id, client id, client secret, webhook secret token, host pool) only in server configuration / secret store, never in the database, DTOs, logs, audit or the repo. | D49 |
| 2 | **Host pool:** a configured list of licensed platform host users (`Zoom:HostPool`); the allocator picks a host with no overlapping session (buffer `Zoom:HostBufferMinutes`, default 10) under a database lock; the licence count is simply the pool size (**Z1 = configuration**). Exhausted pool → 409 `provider_unavailable`. | D53 |
| 3 | **Events:** public HTTPS webhook `POST /api/integrations/zoom/webhook` (URL-validation challenge, signature over the raw body, 5-minute timestamp window, body cap, rate limit, idempotent receipts); recurring polling job as fallback; manual recording links always allowed. | D54 |
| 4 | **Recording:** Zoom meetings are created with `auto_recording = none` unless the setting `Live.CloudRecordingEnabled` (Bool, unit-inherited, default **false**) is true at the ClassCourse's unit; then `cloud`. Teachers may always attach a link manually. | D55 |
| 5 | **Meeting settings fixed by the platform:** waiting room on, join before host off, topic = the teacher's session title only (validated, no markup, no client/class/student data; the dialog says participants see it); client and ClassCourse go into Zoom `tracking_fields` for support only, never for authorisation. | #427 §2 |
| 6 | **Recurring:** the platform owns the series and creates one Zoom meeting per occurrence (max 52); occurrences are generated in the wall-clock time of the IANA `TimeZoneId` sent with the create request (DST-aware) and stored in UTC; edit scope `This` / `ThisAndFollowing`. | #427 §6, Q-G3 |
| 7 | **Participant matching (Z5 default):** a participant is `Matched` only by Zoom user id / email equal to an enrolled student's account; everything else is `Guest` and is never auto-attached to a student. | #427 Z5 |
| 8 | **Feature flags (Q4 default):** `VirtualClasses` gates live-session writes and `join`; `ScormContent` gates package upload, attach and launch for **both SCORM and H5P** (no separate H5P flag). Off → **403 `feature_disabled`**. Reads of existing sessions/packages are not gated. The UI does not pre-hide by flag (no capabilities read in v1) and maps the code to a plain message. Turning `VirtualClasses` on while Zoom is not configured or unhealthy → 409 `provider_not_configured`. | #427 §5, #440 Q-G1 |
| 9 | **Caps (Q5 defaults, all settings, per client unless noted):** `Live.JoinWindowMinutes` 15, `Live.ReminderMinutes` 30, `Live.MaxCreatesPerDay` 200, `Packages.MaxEntries` 10 000, `Packages.MaxUncompressedMb` 1 024, `Packages.MaxClientStorageMb` 10 240, `Packages.TokenMinutes` 120 (hard cap 480), `Packages.CommitMinIntervalSeconds` 2; fixed in code: compression ratio 100:1 per entry and 50:1 total, path depth 20, name 255, validation job timeout 5 min, 2 validation jobs per server and 1 per client, commit message 8 KB / value 4 KB / `cmi` 64 KB per attempt, webhook body 256 KB. Archive size uses the existing `Files.MaxUploadMb`. | #427 §7.1/§7.7, Q5 |
| 10 | **Content host:** separate registrable domain (`PackageHost:Origin`, e.g. `lms-content.example`), never a subdomain of the app; app cookies stay host-only (no `Domain=`), asserted by a test; dev/CI pair is **`http://localhost:<app>` and `http://127.0.0.1:<content>`** (guaranteed cross-site). DNS/TLS/ingress for production is an infrastructure prerequisite owned by the owner. | D56, #427 §7.6 |
| 11 | **SCORM API (Design A):** the content host injects a prelude `<script>` at the top of `<head>` of the launch document (server side, cached by hash) defining `window.API` / `window.API_1484_11` plus in-memory `localStorage` / `sessionStorage` / `document.cookie` stand-ins; it talks to the app over the private `MessagePort`. Launch pages that are framesets or nest the SCO in a child frame are rejected (`package_invalid` / `needs_nested_frames`); the host sends `frame-src 'none'`. | #427 §7.2, spike F6 |
| 12 | **Package-host headers (every response, errors included):** `Content-Security-Policy: sandbox allow-scripts allow-forms; default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors <app origin>`, `nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: private, no-store`, CORS `*` without credentials for `GET/HEAD/OPTIONS`, no `X-Frame-Options`, no CORP, **never `Set-Cookie`**. `'unsafe-eval'` only if the ADR (Task 3, S3) signs it, on the content host only. The app origin adds only `frame-src <content origin>`. | #427 §7.3–7.4, spike F1/F1a/F1b |
| 13 | **Tokens:** `/p/{token}/{**path}`, token = base64url(json{package, student?, attempt?, preview, aud, iat, exp}) + HMAC-SHA256 under a Data Protection purpose unique to the host; constant-time verify; forged/expired/malformed/foreign-audience → one identical 404; lifetime `Packages.TokenMinutes`; revocation markers per (package, student) checked through a 30 s in-memory cache; the app re-launches at 80 % of the lifetime; `/p/*` scrubbed from logs. | #427 §7.5, spike F5 |
| 14 | **Tracking:** only the app page calls the API; the frame holds no token or cookie. One `MessageChannel` per player, window messages ignored (origin is `"null"` and carries no trust); commits carry `seq` (server keeps the highest), `finish` is a flush barrier, the page flushes on `pagehide`; the server enforces the key allow-list, sizes and rate. No learner name/id ever reaches the frame (`cmi.core.student_name` reads return an opaque constant). | #427 §7.7, #440 G5, spike F3/F4 |
| 15 | **Malware (S7 default):** a pluggable `IPackageScanner` step before `Ready` with a no-op implementation in v1; legacy Flash/Java/executable assets are rejected by the extension allow-list. ClamAV is a later configuration if the owner asks. | #427 S7 |
| 16 | **SyncFailed (Q-G2):** no manual retry button in v1; the recurring sweep retries and the UI explains the state. **Class course tab (Q-G4):** "Live sessions" is a tab next to Plan, Forum and Announcements. **Fullscreen (Q-G10):** the container's Fullscreen button only (`allow=""` on the frame). **Third-party accessibility (Q-G8):** a short note in the player, no teacher acknowledgement. | #440 |
| 17 | **Data processing for minors (Z6):** recordings off by default (D55), participants stored only for enrolled matches plus guest display names; a DPA with Zoom and parent deletion requests are owner/legal topics tracked outside this plan (`needs-owner` issue if raised). | #427 Z6 |

## Global Constraints
- **Every M0–M4a Global Constraint still applies** (by reference: `X-Lms-Context`, Guid v7, utf8mb4, default culture `ar`, explicit permissions per built-in role, `UnitAccess.CheckUnitAsync` / `RelationshipAccess.CheckClassCourseAsync` with 404 on a miss, ProblemDetails `code` from `ProblemCodes`, `// ISOLATION-BYPASS: <reason>` + allow-list entry, no new `DbSet` properties, two `Program.cs` lines per module, one migration per PR with a working `Down()`, `[Audited]` + an isolation test per business entity, probe rows per module in the same PR as the route, FE API interface + fake per module, ar/en resource parity, RTL screenshots, `PlacementGuard.EnsureWritableAsync` on every write, server time in every timed response).
- **Isolation shapes (all `IOrgUnitScoped`, unit fields copied from the class of the ClassCourse, repather-covered):** `LiveSessionSeries`, `LiveSession`, `LiveSessionParticipant`, `PackageUpload`, `ScormPackage`, `ScormSco`, `H5pContent`, `PackageAttempt`, `PackageRevocation`. **The one platform table** is `ZoomWebhookReceipt` (not tenant data, like the audit tables): it carries no client data beyond the event id and type, gets an allow-list entry with its reason and a retention job (30 days).
- **Relationship rules (D42):** Teacher reaches only own ClassCourses for `LiveSessions.*` and `Packages.*`; Student only own active enrollments for `LiveSessions.View` and `Packages.View`; Guardian and Counselor none. Student reads go through `IModuleAccess` (a hidden, out-of-window or other-group module is 404 for the session, the join, the package launch and the attempt).
- **Secrets:** `Zoom:*` and the token key material only in configuration / Data Protection; a test asserts no secret, join/start URL or package token appears in logs, DTO lists or audit rows. `JoinUrl` / `StartUrl` are stored encrypted (Data Protection) and returned only by `POST …/join`.
- **Untrusted input:** zip entries, manifests, `h5p.json`, CMI/xAPI data and webhook bodies are hostile until validated; never rendered as HTML in the app; never used as ids, paths or URLs without validation.
- **Audit:** `[Audited]` on `LiveSessionSeries`, `LiveSession`, `PackageUpload`, `ScormPackage`, `H5pContent`; participants, attempts, revocations and webhook receipts are not audited row by row. Package rejects and webhook signature failures are security events (`AuditSecurityEvents`) without payloads.
- **Migrations:** `AddLiveSessions` (Task 4), `AddZoomWebhookReceipts` (Task 7), `AddPackages` (Task 8), `AddPackageAttempts` (Task 9), `AddScormPackages` (Task 10), `AddH5pContent` (Task 11). Whoever merges second regenerates on top of `main`.
- **FE:** `ILiveSessionsApi`, `IPackagesApi`, each with HTTP implementation + fake (`Lms:UseFakeApi=true`) that mimics the server rules the UI relies on. `docs/DESIGN.md` + the lms-ui skill (`PageHeader`, `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, one primary action, 360 px labelled cards, logical CSS, ISO dates `yyyy-MM-dd HH:mm` left to right). Key prefixes `Live_*`, `Package_*`, `Error_<code>`. Files under ~300 lines (split the schedule dialog, recurrence preview, player, bridge and status views).
- **E2E:** one stack at a time (`e2e.lock`, port 5132); the package demo needs the second origin (`127.0.0.1:<content port>`) started by the E2E fixture.

## Review Focus
1. **Visibility and relationship bypass:** a student of another group/class or a teacher of another ClassCourse gets 404 on sessions, join, participants, packages, launch, preview and attempts; a School-2 admin cannot see School-1 sessions or packages. (Tasks 4, 6, 8, 9, 20.)
2. **Join link leakage:** a student never receives the start URL, never receives a join URL outside the window or in a list, and the URL is never in the page markup before the click. (Tasks 6, 15, 20.)
3. **Webhook integrity:** signature over raw bytes in constant time, stale/missing/bad → 401, oversize → 413, replay processed once, URL validation answered, no payload in logs. (Tasks 7, 20.)
4. **Package containment:** zip-slip, bombs, symlinks, XXE, nested archives, path escapes and cross-package/cross-client paths all refused; every package-host response carries the CSP `sandbox` and never `Set-Cookie`; the iframe never gets `allow-same-origin`; the app trusts only the port; tokens are audience-bound, revocable and scrubbed from logs. (Tasks 8–11, 17, 21.)
5. **Score integrity:** completion and grade events come from server-validated commits only, once per change, never from preview, never out of `seq` order. (Tasks 10, 11, 23.)

---

### Task 1: Contract — wave 3a: live sessions — lane: Lead contract

**PRD:** FR-VC-01, FR-CNT-01 (Live Session), X-07, D49, D53–D55. **Depends on:** M4a wave 2 contract merged. Merges before Tasks 4–7 and 13–15 start.

**Files:**
- Modify: `src/Lms.Shared/Authorization/Permissions.cs`, `BuiltInRoles.cs`, `RelationshipRules.cs`, `src/Lms.Shared/ProblemCodes.cs`, `src/Lms.Shared/Auditing/AuditCatalog.cs`, `src/Lms.Shared/Content/ModuleCatalogs.cs` (`CompletionRules.ForActivity`), `src/Lms.Shared/Notifications/NotificationEvents.cs`, `src/Lms.Shared/Configuration/SettingKeys.cs`, `SettingCatalog.cs`, `src/Lms.Infrastructure/Access/BuiltInRoleDefaults.cs` (version 6), `src/Lms.Client/Resources/SharedResource.{ar,en}.resx`
- Create: `src/Lms.Shared/LiveSessions/LiveSessionCatalogs.cs`, `LiveSessionDtos.cs`, `src/Lms.Shared/Routes.LiveSessions.cs`
- Test: `tests/Lms.UnitTests/Contracts/LiveSessionContractTests.cs` (new), `BuiltInRoleMatrixTests.cs`, `AuthorizationContractTests.cs`, `tests/Lms.UnitTests/Access/BuiltInRoleDefaultsTests.cs`, `tests/Lms.IntegrationTests/Access/BuiltInRoleReconcilerTests.cs`

**Interfaces:**
- Permissions `LiveSessions.{View, Manage}`. Matrix: OrgAdmin + Manager `View, Manage` (D52 pattern), Supervisor `View`, Teacher `View, Manage` (own class courses), Student `View` (own enrollments); Counselor, Guardian none. `RelationshipRules` extended.
- Catalogs: `LiveSessionStatuses {Scheduled, Live, Ended, Cancelled, SyncFailed}`, `RecordingSources {Provider, Manual}`, `ParticipantMatchStates {Matched, Guest}`, `SeriesEditScopes {This, ThisAndFollowing}`, `LiveSessionLimits` (title 1–200, duration 15–480 min, occurrences ≤ 52). `CompletionRules.ForActivity(LiveSession) = [None, Manual, View]`.
- DTOs: `LiveSessionDto` (no URLs), `LiveSessionListItemDto`, `CreateLiveSessionRequest` (TitleAr/En, `LessonKey`, `StartsAtUtc`, `DurationMinutes`, optional `Recurrence {IntervalWeeks, DaysOfWeek, UntilUtc | Count, TimeZoneId}`, `ModuleSettingsInput`), `UpdateLiveSessionRequest` (+ `Scope`), `LiveSessionJoinDto {Url, ExpiresAtUtc, ServerNowUtc}`, `RecordingLinkRequest`, `LiveParticipantDto`, `ProviderStatusDto {Configured, Healthy}`, `UpcomingLiveSessionDto`; every timed DTO carries `ServerNowUtc`.
- Routes: `/api/class-courses/{id}/live-sessions` (GET paged, POST), `/api/live-sessions/{id}` (GET, PUT, DELETE `?scope=`), `/api/live-sessions/{id}/join` (POST), `/api/live-sessions/{id}/recording` (PUT), `/api/live-sessions/{id}/participants` (GET paged), `/api/live-sessions/upcoming` (GET, caller's sessions, max 5), `/api/live-sessions/provider-status` (GET), `/api/integrations/zoom/webhook` (POST, anonymous by design).
- ProblemCodes: `provider_unavailable`, `provider_not_configured`, `live_session_not_open`, `feature_disabled`, `recurrence_invalid`. Event `Live.Reminder` (named recipients; params Title, Course, StartsAtUtc). Settings: `Live.JoinWindowMinutes` (Int 0–60, 15), `Live.ReminderMinutes` (Int 5–1440, 30), `Live.MaxCreatesPerDay` (Int 1–10 000, 200), `Live.CloudRecordingEnabled` (Bool, unit-inherited, false). AuditCatalog: `LiveSessionSeries`, `LiveSession`. ar/en labels for every code, event and setting.

- [ ] **Step 1: Write the failing tests** (`LiveSessionContractTests`; matrix, defaults-version 6, relationship, reconciler, label parity). **Step 2–4** FAIL → implement → PASS; 0 warnings. **Step 5: Commit** `feat(contracts): M4b wave 3a - live session permissions, DTOs, routes and codes`.

**Acceptance:** contract tests green; every new code/event/setting has ar and en labels; no route left without a DTO; QA approves the matrix.

### Task 2: Contract — wave 3b: packages (SCORM, H5P) — lane: Lead contract

**PRD:** FR-STD-01, FR-STD-02, FR-CNT-01 (SCORM, H5P), X-05, D51, D56. **Depends on:** Task 1 merged; ADR draft of Task 3 (token, preview and commit shapes). Merges before Tasks 8–11 and 16–17 start.

**Files:** Modify the Task 1 set plus `src/Lms.Domain/Grading/GradeSourceKind.cs` (reviewed domain change); Create `src/Lms.Shared/Packages/PackageCatalogs.cs`, `PackageDtos.cs`, `PackageTrackingDtos.cs`, `src/Lms.Shared/Routes.Packages.cs`; test `tests/Lms.UnitTests/Contracts/PackageContractTests.cs`; `BuiltInRoleDefaults` version 7.

**Interfaces:**
- Permissions `Packages.{Upload, View}`. Matrix: OrgAdmin + Manager `Upload, View`, Supervisor `View`, Teacher `Upload, View` (own class courses), Student `View` (own enrollments); Counselor, Guardian none.
- Catalogs: `PackageKinds {Scorm, H5p}`, `PackageUploadStatuses {Uploaded, Validating, Ready, Rejected}`, `ScormVersions {Scorm12, Scorm2004}`, `PackageRejectReasons {unsafe_path, too_many_entries, too_large, compression_ratio, symlink, duplicate_name, bad_name_encoding, executable, encrypted, nested_archive, manifest_invalid, xml_unsafe, multi_sco, needs_nested_frames, needs_relaxed_policy, h5p_invalid, validation_timeout, quota_exceeded, scan_failed}`, `PackageAttemptStatuses {InProgress, Completed}`, `ScormCmiKeys` (allow-list for 1.2 and 2004), `PackageTrackingLimits` (8 KB / 4 KB / 64 KB). `CompletionRules.ForActivity(Scorm|H5p) = [None, Submit, Score]`. `GradeSourceKind` += `Scorm`, `H5p`.
- DTOs: `PackageUploadDto {Id, Kind, Status, RejectReason?, Title?, Version?, MainLibrary?}`, `AttachPackageModuleRequest` (lesson, titles, `ModuleSettingsInput`), `PackageLaunchDto {LaunchUrl, AttemptId, TokenExpiresAtUtc, ServerNowUtc, Resumed}`, `PackagePreviewDto {LaunchUrl, TokenExpiresAtUtc, ServerNowUtc}`, `PackageCommitRequest {Seq, Format (Scorm|Xapi), Data}`, `PackageFinishRequest {LastSeq}`, `PackageAttemptDto` (status, score, completion as the server judged).
- Routes: `/api/class-courses/{id}/packages` (POST multipart → 202), `/api/packages/{id}` (GET, DELETE), `/api/packages/{id}/module` (POST), `/api/packages/{id}/launch` (POST), `/api/packages/{id}/preview` (POST), `/api/package-attempts/{id}/commit` (POST), `/api/package-attempts/{id}/finish` (POST). The content-host path `/p/{token}/{**path}` is not an `/api` route and is not in `Routes`.
- ProblemCodes: `package_invalid` (with `reason`), `package_too_large`, `package_not_ready`, `package_commit_rejected`. Settings: `Packages.MaxEntries`, `Packages.MaxUncompressedMb`, `Packages.MaxClientStorageMb`, `Packages.TokenMinutes`, `Packages.CommitMinIntervalSeconds` (defaults in Decision 9). AuditCatalog: `PackageUpload`, `ScormPackage`, `H5pContent`. ar/en labels incl. one `Error_package_reason_<code>` per reject reason.

- [ ] **Steps 1–5** as Task 1. **Commit** `feat(contracts): M4b wave 3b - package permissions, DTOs, routes and codes`.

**Acceptance:** as Task 1; every reject reason has a plain-words ar/en label; `GradeSourceKind` change covered by the M4a grade-event tests.

### Task 3: Package hosting ADR — real-package and crafted-archive corpus, sign-off — lane: BE4 (+ BE3 for S6) · QA3 signs

**PRD:** D51, D56, X-05, FR-STD-01/02. **Depends on:** spike #444 merged (evidence doc). **Gate:** Tasks 9, 10, 11 and 17 are not started before this ADR carries QA3's approving review and `qa:approved`. Task 8 may start its upload half once Task 2 merges; its caps come from this ADR.

**Files:** Create `docs/adr/00NN-package-hosting.md` (next free number), `docs/spikes/corpus/README.md` (real packages by hash/licence; crafted zips committed under `tests/Lms.IntegrationTests/Packages/Corpus/` — BE3 builds them so Task 8 reuses them); update the throw-away prototype on `spike/m4b-package-host` (never merged).

**Content:** for S1, S2 (opaque-origin probes a–d), S3 (`'unsafe-eval'` count), S4, S5, S6, S7, S8 (h5p-standalone version + licence of every shipped asset), S9 (session token + revocation marker + re-launch with a > 10 min session) and S10 (Design A confirmed: count of frameset/nested-frame packages in the corpus), the result, evidence and date per the pass/fail table of #427 §7.9; the prototype re-run with `connect-src 'self'` + CORS `*` and the `localhost` / `127.0.0.1` pair.

**Acceptance:** each question is "pass" with evidence or explicitly "open" (an open question blocks only the tasks it names); corpus = 3 real SCORM (1.2, 2004 4th ed., one authoring-tool export) + 2 real H5P + the crafted list of #427 §7.9; QA3 approves. A change to the hosting design after sign-off needs a new QA review. **Commit** `docs(adr): package hosting for SCORM and H5P`.

### Task 4: Live session kernel and authoring — lane: BE1

**PRD:** FR-VC-01, FR-CNT-01, D49, D53. **Depends on:** Task 1. Uses `FakeMeetingProvider` until Task 5 merges.

**Files:**
- Create: `src/Lms.Domain/LiveSessions/LiveSessionSeries.cs`, `LiveSession.cs`, `LiveSessionParticipant.cs`, `IMeetingProvider.cs`, `ILiveSessionAttendanceSink.cs`, `RecurrenceExpander.cs`; `src/Lms.Infrastructure/LiveSessions/LiveSessionService.cs`, `FakeMeetingProvider.cs`, `NoOpAttendanceSink.cs`, configurations, migration `AddLiveSessions`; `src/Lms.Api/Features/LiveSessions/LiveSessionsModule.cs`, `LiveSessionEndpoints.cs`, `LiveSessionValidators.cs`
- Test: `tests/Lms.UnitTests/LiveSessions/RecurrenceExpanderTests.cs`; `tests/Lms.IntegrationTests/LiveSessions/LiveSessionAuthoringTests.cs`, `LiveSessionIsolationTests.cs`; `Security/ProbeRows/LiveSessionProbeRows.cs`

**Interfaces:** entities per #427 §3 (`JoinUrl` / `StartUrl` encrypted with Data Protection, `HostUserKey` never exposed); `IMeetingProvider {CreateMeetingAsync, UpdateMeetingAsync, DeleteMeetingAsync, GetParticipantsAsync, GetRecordingsAsync, GetHealthAsync}`; create/list/get/update (`scope`)/delete endpoints; module created through `IModuleWriter.CreateForRefAsync(…, ModuleKinds.LiveSession, sessionId, …)`, one module per occurrence; `VirtualClasses` gate (403 `feature_disabled`); `PlacementGuard`; `Live.MaxCreatesPerDay` per client; a provider failure keeps the row in `SyncFailed` (no silent loss).

- [ ] **Step 1: Write the failing tests** `Teacher_schedules_single_session_with_module`; `Recurring_creates_one_session_per_occurrence_max_52`; `Recurrence_follows_wall_clock_time_across_dst`; `Edit_this_and_following_recreates_future_occurrences_only`; `Student_sees_only_visible_session_modules`; `Teacher_of_other_class_course_gets_404`; `Flag_off_refuses_writes_with_feature_disabled`; `Closed_class_rejects_writes`; `Daily_create_cap_is_enforced_per_client`; `Provider_failure_keeps_session_in_sync_failed`; `Lists_never_contain_join_or_start_urls`; isolation tests for `LiveSessionSeries`, `LiveSession`, `LiveSessionParticipant`. **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(live): live session kernel and authoring`.

**Acceptance:** tests green; probe rows for every route; repather coverage green; no URL in any list DTO.

### Task 5: Zoom provider — S2S OAuth, host pool, limiter, sync sweep, provider status — lane: BE2

**PRD:** FR-VC-01, D49, D53, D55. **Depends on:** Task 1; Task 4's `IMeetingProvider` interface (BE1 opens it first as a tiny PR or BE2 codes against the merged Task 4).

**Files:** Create `src/Lms.Infrastructure/Zoom/ZoomOptions.cs`, `ZoomTokenCache.cs`, `ZoomMeetingProvider.cs`, `ZoomHostAllocator.cs`, `ZoomOutboundLimiter.cs`, `ZoomSyncSweepJob.cs`, `ZoomServiceCollectionExtensions.cs`; `src/Lms.Api/Features/LiveSessions/ProviderStatusEndpoint.cs`. Test `tests/Lms.UnitTests/Zoom/*`, `tests/Lms.IntegrationTests/Zoom/ZoomProviderTests.cs` (stubbed `HttpMessageHandler`), `HostAllocatorConcurrencyTests.cs`.

**Interfaces:** `account_credentials` grant, token cached until shortly before expiry, one refresh in flight; host allocator under a database lock with `Zoom:HostBufferMinutes`; token bucket per Zoom rate-limit class honouring `429 Retry-After`, bounded queue; 409/503 `provider_unavailable` with `Retry-After`; meetings created with Decision 5 settings and `auto_recording` per `Live.CloudRecordingEnabled` (D55); `ZoomSyncSweepJob` (recurring system job) retries `SyncFailed` rows; `GET /api/live-sessions/provider-status` → `{configured, healthy}` with no configuration detail; `FakeMeetingProvider` selected when `Zoom` is not configured in Development.

- [ ] **Step 1: Tests** `Token_is_cached_and_refreshed_once_under_concurrency`; `Rate_limited_call_honours_retry_after`; `Host_pool_never_double_books_a_slot_under_concurrency`; `Exhausted_pool_answers_provider_unavailable`; `Cloud_recording_only_when_unit_setting_enabled`; `Topic_contains_only_the_session_title`; `Provider_down_keeps_session_in_sync_failed_and_sweep_retries`; `Secrets_never_appear_in_logs_or_dtos`; `Provider_status_reveals_no_configuration`. **Step 2–5** → commit `feat(live): Zoom provider with host pool and outbound limiter`.

**Acceptance:** tests green; no secret in logs (log-capture assertion); provider status probe rows.

### Task 6: Live session lifecycle — join window, reminders, recording link, participants read, attendance sink — lane: BE1

**PRD:** FR-VC-01, FR-CNT-04, FR-ATT-01 (seam), X-07. **Depends on:** Task 4.

**Files:** Modify `src/Lms.Domain/Jobs/IBackgroundJobs.cs` + `src/Lms.Infrastructure/Jobs/HangfireBackgroundJobs.cs` (add `ScheduleAsSystem(DateTimeOffset, job)` / `Delete(jobId)` — reviewed shared-infrastructure change with its own tests); Create `src/Lms.Infrastructure/LiveSessions/LiveReminderJob.cs`, `src/Lms.Api/Features/LiveSessions/JoinEndpoint.cs`, `RecordingEndpoints.cs`, `ParticipantEndpoints.cs`, `UpcomingEndpoint.cs`. Test `tests/Lms.IntegrationTests/LiveSessions/JoinTests.cs`, `ReminderTests.cs`, `RecordingAndParticipantsTests.cs`, `tests/Lms.IntegrationTests/Jobs/ScheduleAsSystemTests.cs`.

**Interfaces:** `POST …/join`: students inside `[start − Live.JoinWindowMinutes, end]` by server clock get the join URL, staff with `Manage` get the start URL; records `CompletionSignal.Viewed`; 409 `live_session_not_open` outside, 404 for an invisible module. Reminder scheduled at `start − Live.ReminderMinutes` to the module's targeted students, re-scheduled on update, removed on cancel, idempotent via `ReminderSentAtUtc`. `PUT …/recording` (http/https only, `Manage`, source `Manual`). `GET …/participants` (staff, paged). `GET /api/live-sessions/upcoming` (student: own visible sessions; teacher: own class courses; max 5). On `Ended`, `ILiveSessionAttendanceSink.SessionEndedAsync(sessionId)` is called after commit (no-op until M5).

- [ ] **Step 1: Tests** `Join_outside_window_is_409_and_invisible_module_is_404`; `Join_uses_server_clock`; `Student_never_receives_the_start_url`; `Join_records_viewed_completion`; `Reminder_is_sent_once_and_rescheduled_on_update`; `Cancel_removes_the_reminder`; `Recording_link_rejects_non_http_schemes`; `Participants_are_staff_only_and_relationship_checked`; `Upcoming_lists_only_visible_sessions_max_five`; `Attendance_sink_called_once_after_end`; `ScheduleAsSystem_runs_with_system_context`. **Step 2–5** → commit `feat(live): join window, reminders, recordings and participants`.

**Acceptance:** tests green; probe rows for the new routes; `Live.Reminder` delivered in ar and en.

### Task 7: Zoom webhooks and polling fallback — lane: BE2

**PRD:** FR-VC-01, D54, D55, X-08. **Depends on:** Tasks 5, 6 (recording/participant writers).

**Files:** Create `src/Lms.Domain/LiveSessions/ZoomWebhookReceipt.cs`; `src/Lms.Infrastructure/Zoom/ZoomWebhookVerifier.cs`, `ZoomEventProcessor.cs`, `ZoomPollingJob.cs`, `ZoomReceiptRetentionJob.cs`, `ParticipantMatcher.cs`, `VirtualClassesEnableGuard.cs` (registered through the feature-flag enable hook, in this folder); migration `AddZoomWebhookReceipts`; `src/Lms.Api/Features/Integrations/ZoomWebhookEndpoint.cs`, `DevZoomEventsEndpoint.cs` (Development only: signs and posts a fake event for the E2E demo); `docs/runbook-zoom.md` (app, host pool, webhook URL, secrets). Test `tests/Lms.UnitTests/Zoom/ZoomWebhookVerifierTests.cs`, `tests/Lms.IntegrationTests/Zoom/ZoomWebhookTests.cs`, `ParticipantMatchingTests.cs`, `PollingFallbackTests.cs`.

**Interfaces:** handling order per #427 §4: 256 KB cap read before anything else and rate limit (429 without DB access) → `endpoint.url_validation` answered with `{plainToken, encryptedToken}` → `x-zm-signature` over the **raw bytes** with `CryptographicOperations.FixedTimeEquals` (401, no hint) → 5-minute timestamp window (401) → idempotent receipt (a known event id is 200 and not processed again). `meeting.started/ended` update status; `recording.completed` attaches the recording (source `Provider`) only when cloud recording was enabled for the unit; participants imported by the report API with Decision 7 matching. Polling job covers sessions `Ended` without a webhook in the last N hours. Enabling `VirtualClasses` with Zoom unconfigured/unhealthy → 409 `provider_not_configured`. If the feature-flag module has no enable hook, the hook interface is added in a separate small PR reviewed by the Lead (the guard itself stays in `Infrastructure/Zoom`). `ZoomWebhookReceipt` is the documented platform table (allow-list entry, 30-day retention).

- [ ] **Step 1: Tests** `Webhook_with_bad_missing_or_stale_signature_is_401`; `Webhook_signature_is_checked_over_the_raw_body`; `Webhook_oversize_body_is_413`; `Webhook_url_validation_challenge_is_answered`; `Replayed_webhook_is_processed_once`; `Webhook_rate_limit_answers_429_without_db`; `Recording_completed_attaches_provider_link`; `Participants_match_by_user_or_email_and_unknown_stay_guests`; `Polling_fills_a_missed_webhook`; `Enabling_the_flag_without_zoom_options_is_409`; `Webhook_failure_logs_a_security_event_without_payload`; `Receipts_older_than_30_days_are_purged`. **Step 2–5** → commit `feat(live): Zoom webhooks, polling fallback and participant import`.

**Acceptance:** tests green; webhook probe rows (401/413/429/200); the anonymous route is the only anonymous M4b `/api` route and is listed in the probe matrix.

### Task 8: Package kernel — upload, streamed hardening, validation job, store, quotas, purge — lane: BE3

**PRD:** FR-STD-01, FR-STD-02, X-05. **Depends on:** Task 2; crafted corpus from Task 3; caps confirmed by the ADR (Task 3) before merge.

**Files:** Create `src/Lms.Domain/Packages/PackageUpload.cs`, `IPackageStore.cs`, `IPackageValidator.cs`, `IPackageScanner.cs`; `src/Lms.Infrastructure/Packages/PackageService.cs`, `ZipHardening.cs`, `SafeXml.cs`, `SafeJson.cs`, `MinioPackageStore.cs`, `PackageValidationJob.cs`, `PackagePurgeJob.cs`, `NoOpPackageScanner.cs`, `PackageContentTypes.cs` (extension allow-list), migration `AddPackages`; `src/Lms.Api/Features/Packages/PackagesModule.cs`, `PackageEndpoints.cs`. Test `tests/Lms.UnitTests/Packages/ZipHardeningTests.cs`, `SafeXmlTests.cs`; `tests/Lms.IntegrationTests/Packages/PackageUploadTests.cs`, `PackageIsolationTests.cs`; `Security/ProbeRows/PackageProbeRows.cs`.

**Interfaces:** `POST /api/class-courses/{id}/packages` (`Packages.Upload` + relationship; `ScormContent` gate; archive in `IFileStorage`; 202 `Uploaded`); validation as a system job with the Decision 9 budgets (timeout → `validation_timeout`, 2 per server / 1 per client, per-job temp disk budget, cleanup in `finally` + sweep); rules of #427 §7.1 (real-byte counting, NFC + case-fold duplicates, Zip64, UTF-8 / CP437 names, control/BOM/bidi chars, executables, encrypted, nested archives); `SafeXml` (DTD prohibited, no resolver, entity chars 0); `SafeJson` (1 MB, depth 32); objects under `packages/{orgId}/{packageId}/` with server-assigned content types; per-client quota before and during extraction; `IPackageScanner` hook; format detection dispatches to `IPackageValidator` implementations (registered by Tasks 10/11; until then `Rejected` `manifest_invalid`); `GET /api/packages/{id}`, `POST /api/packages/{id}/module` (only `Ready`; `IModuleWriter`), `DELETE` (purge job of prefix + archive, retried).

- [ ] **Step 1: Tests** the crafted corpus: `Zip_slip_entry_is_rejected`, `Decompression_bomb_is_rejected_by_real_bytes_not_headers`, `Symlink_entry_is_rejected`, `Too_many_entries_is_rejected`, `Executable_extensions_are_rejected`, `Duplicate_after_case_folding_or_nfc_is_rejected`, `Nested_archive_is_rejected`, `Encrypted_entry_is_rejected`, `Bidi_or_control_chars_in_names_are_rejected`, `Zip64_is_read_with_64_bit_counters`; `Xxe_and_billion_laughs_are_rejected_without_network`; `Quota_and_job_timeout_stop_a_bomb`; `Rejected_upload_purges_temp_at_once`; `Deleting_a_package_purges_its_prefix`; `Attach_requires_ready`; `Flag_off_refuses_upload`; `Teacher_of_other_class_course_gets_404`; isolation test `PackageUpload_is_isolated_per_client_and_subtree`. **Step 2–5** → commit `feat(packages): package kernel with hardened validation job`.

**Acceptance:** every crafted zip rejected with the intended reason; no file written outside the job temp area (asserted); probe rows; isolation test.

### Task 9: Package host — separate pipeline, headers, path tokens, launch and preview — lane: BE4

**PRD:** X-05, FR-STD-01/02, D51, D56. **Depends on:** Task 3 signed, Task 8.

**Files:** Create `src/Lms.Api/PackageHost/PackageHostPipeline.cs` (host-matched branch on `PackageHost:Origin`), `PackageHostHeaders.cs`, `PackageTokenService.cs`, `PackageFileEndpoint.cs`, `PackageRevocationCache.cs`, `LogScrubbing.cs`; `src/Lms.Domain/Packages/PackageAttempt.cs`, `PackageRevocation.cs`; `src/Lms.Infrastructure/Packages/PackageAttemptService.cs`, migration `AddPackageAttempts`; `src/Lms.Api/Features/Packages/LaunchEndpoints.cs`, `CommitEndpoints.cs`; `docs/runbook-package-host.md` (DNS/TLS/ingress, proxy log scrubbing sample). Modify `SecurityHeadersMiddleware` only to add `frame-src <content origin>` on app responses (origin from configuration). Test `tests/Lms.IntegrationTests/PackageHost/*`.

**Interfaces:** `GET /p/{token}/{**path}` with the Decision 12 headers on every response (errors included); every path component checked against the package's file index; store key built from the token's package id only; types outside the allow-list and top-level SVG (`Sec-Fetch-Dest: document`) served as attachments; tokens per Decision 13; revocation markers written when a module is hidden, a student unenrolled or a package deleted. `POST /api/packages/{id}/launch` (student, visible module; creates or resumes the attempt; same `attemptId` on re-launch), `POST /api/packages/{id}/preview` (staff; preview token, no attempt). `POST /api/package-attempts/{id}/commit` / `finish`: caller owns the attempt, `seq` monotonic (lower ignored), sizes and `Packages.CommitMinIntervalSeconds` enforced, preview refused, data handed to the format adapter (`IPackageTracker`, registered by Tasks 10/11). App cookies host-only (test).

- [ ] **Step 1: Tests** `Package_host_responses_carry_csp_sandbox_nosniff_and_no_xfo`; `No_response_of_the_package_host_sets_a_cookie`; `Error_responses_carry_the_same_headers`; `Package_path_never_leaves_its_package`; `Token_from_another_package_client_or_audience_is_404`; `Forged_expired_and_malformed_tokens_are_identical_404`; `Revoked_student_token_is_refused_within_the_cache_window`; `Top_level_svg_and_unknown_types_are_attachments`; `Logs_never_contain_the_token`; `App_responses_add_frame_src_for_the_content_origin_only`; `App_cookies_are_host_only`; `Launch_on_in_progress_attempt_returns_same_attempt`; `Student_cannot_launch_an_invisible_module`; `Students_cannot_preview_and_preview_token_is_refused_by_commit`; `Out_of_order_commits_resolve_by_seq`; `Commit_over_size_or_rate_is_refused`; isolation tests for `PackageAttempt`, `PackageRevocation`. **Step 2–5** → commit `feat(packages): sandboxed package host, tokens, launch and tracking`.

**Acceptance:** tests green; QA3 security review approved; probe rows; runbook merged.

### Task 10: SCORM — manifest, Design A prelude, runtime mapping, completion and grade — lane: BE3

**PRD:** FR-STD-01, FR-CNT-04, FR-ASG-02/FR-QZ-03 pattern (grade outbox). **Depends on:** Tasks 8, 9.

**Files:** Create `src/Lms.Domain/Packages/ScormPackage.cs`, `ScormSco.cs`; `src/Lms.Infrastructure/Packages/Scorm/ScormManifestValidator.cs`, `ScormLaunchResolver.cs`, `ScormPreludeInjector.cs`, `wwwroot-package/scorm-prelude.js` (served by the content host), `ScormTracker.cs`; migration `AddScormPackages`. Test `tests/Lms.UnitTests/Packages/Scorm/*`, `tests/Lms.IntegrationTests/Packages/ScormTests.cs`.

**Interfaces:** `imsmanifest.xml` through `SafeXml`; version detection (1.2 / 2004); single SCO only (`multi_sco` otherwise); launch `href` resolved against the package root only; frameset / child-frame launch pages → `needs_nested_frames`; the prelude injected at the top of `<head>` (cached by hash) defines `API` / `API_1484_11`, storage stand-ins, opaque `student_name`/`student_id`, and posts on the port; `ScormTracker` maps `lesson_status` / `completion_status` / `success_status` and `score.raw/min/max` to `CompletionSignal.Submitted` / `Scored` and writes `IGradeEvents` (`GradeSourceKind.Scorm`) only when the final score changes; unknown CMI keys dropped; `suspend_data` kept for resume.

- [ ] **Step 1: Tests** `Manifest_with_external_entities_is_rejected`; `Scorm12_and_2004_are_detected`; `Multi_sco_manifest_is_rejected`; `Frameset_launch_page_is_rejected_in_design_a`; `Prelude_is_injected_first_in_head_of_the_launch_document_only`; `Scorm12_and_2004_lesson_status_map_to_completion`; `Unknown_cmi_keys_are_dropped`; `Student_name_is_never_sent_to_the_frame`; `Score_writes_a_grade_event_once_per_change`; `Resume_returns_suspend_data`; `ScormPackage_is_isolated_per_client_and_subtree`. **Step 2–5** → commit `feat(packages): SCORM 1.2 and 2004 runtime`.

**Acceptance:** the ADR's real SCORM corpus (in-scope subset) validates, runs and completes in an integration test with the content host; grade events idempotent.

### Task 11: H5P — validation, vendored player, xAPI score — lane: BE4

**PRD:** FR-STD-02, FR-CNT-04. **Depends on:** Tasks 8, 9; S8 decision in the ADR.

**Files:** Create `src/Lms.Domain/Packages/H5pContent.cs`; `src/Lms.Infrastructure/Packages/H5p/H5pValidator.cs`, `H5pLaunchPage.cs`, `XapiTracker.cs`; vendored player under `src/Lms.Api/PackageHost/vendor/h5p-standalone/<version>/` with `LICENSES.md`; migration `AddH5pContent`. Test `tests/Lms.UnitTests/Packages/H5p/*`, `tests/Lms.IntegrationTests/Packages/H5pTests.cs`.

**Interfaces:** `h5p.json` / `content.json` / `library.json` through `SafeJson`; library folders follow the kernel entry rules; the content host serves a generated launch page that loads the pinned player from the content host (never a CDN) and posts xAPI on the port; only `completed` / `passed` / `scored` results kept (capped); `Scored` / `Submitted` completion and `GradeSourceKind.H5p` events once per change.

- [ ] **Step 1: Tests** `Invalid_h5p_json_is_rejected`; `Deep_or_huge_h5p_json_is_rejected`; `Player_assets_are_served_from_the_content_host_with_the_sandbox_csp`; `Xapi_result_maps_to_score_and_completion`; `Statements_of_other_attempts_are_refused`; `Non_result_statements_are_dropped`; `H5pContent_is_isolated_per_client_and_subtree`. **Step 2–5** → commit `feat(packages): H5P player and xAPI scoring`.

**Acceptance:** the two real H5P files run and score under the strict CSP; player licence recorded.

---

### Task 12: M4b design spec — live session surfaces and package player — lane: UX (UI/UX Designer)

**PRD:** X-01, X-10. **Depends on:** Task 1 (names); runs in parallel with Tasks 13–16.

**Files:** Modify `docs/DESIGN.md` (new patterns: session status chips, join panel with countdown, recurrence preview, provider banners, package status stepper, player frame + toolbar + preview banner); mock screenshots in `docs/screenshots/ux-m4b/` (ar/en, 1440/360, light/dark); lms-ui skill notes if new components are introduced.

**Acceptance:** FE1/FE2 confirm the spec covers Tasks 14–17; the Lead approves; no code. **Commit** `docs(design): M4b live session and package player patterns`.

### Task 13: Live session client, fake and module kind plumbing — lane: FE1

**PRD:** FR-VC-01, FR-CNT-01. **Depends on:** Task 1. (#440 G1, size S)

**Files:** Create `src/Lms.Client/Services/LiveSessions/ILiveSessionsApi.cs`, `LiveSessionsApi.cs`, `FakeLiveSessionsApi.cs`; Modify `ModuleKindIcons`, `ModuleBody.razor` (placeholders for LiveSession / Scorm / H5p), `ClientServices.cs`, resx (`Live_*`, `Module_Kind_*`). Test `tests/Lms.Client.Tests/LiveSessions/FakeLiveSessionsApiTests.cs`, `ModuleKindLabelTests.cs`.

**Tests:** `Fake_enforces_the_join_window_with_the_server_clock`, `Fake_recurring_creates_one_session_per_occurrence_max_52`, `Fake_series_edit_scope_this_vs_following`, `Fake_reports_sync_failed_and_provider_unavailable`, `Module_kinds_have_labels_and_icons_in_both_languages`. **Commit** `feat(client): live session API client and module kinds`.

### Task 14: Live session authoring — schedule, list, edit, cancel, recording — lane: FE1

**PRD:** FR-VC-01. **Depends on:** Task 13, Task 12; real API after Task 4. (#440 G2, size L)

**Files:** Create `src/Lms.Client/Pages/LiveSessions/LiveSessionsPage.razor` (tab in `ClassCourseTabs`), `LiveSessionDialog.razor`, `RecurrenceEditor.razor`, `RecurrencePreview.razor`, `LiveSessionsList.razor`, `SeriesScopeDialog.razor`, `RecordingLinkDialog.razor`, `ProviderStatusBanner.razor`. Test `tests/Lms.Client.Tests/LiveSessions/*`.

**Interfaces:** per #440 G2: one primary action "Schedule a session" (`LiveSessions.Manage`); the dialog sends the browser's IANA time zone; status chips incl. `SyncFailed` explanation; series scope prompt; recording link http/https only with its host name shown; `configured=false` → "ask your platform administrator" without a Settings link; error mapping `provider_unavailable` (Retry-After in words), `feature_disabled`, `recurrence_invalid`, module visibility/target codes on the right fields.

**Tests:** `Teacher_schedules_single_session_under_own_lesson`, `Recurring_requires_end_date_or_count_and_previews_dates`, `Recurring_is_limited_to_52_occurrences`, `Series_edit_asks_for_the_scope`, `SyncFailed_session_shows_the_explanation`, `Recording_link_rejects_javascript_scheme`, `Recording_link_shows_its_host_name`, `Provider_not_configured_shows_the_platform_admin_hint_without_a_settings_link`, `Provider_unavailable_shows_retry_after`, `Feature_disabled_shows_the_plain_message`, `Student_sees_no_authoring_controls`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): live session scheduling and management`.

### Task 15: Live session student side — join, countdown, recording, participants, reminder labels — lane: FE1

**PRD:** FR-VC-01, X-07. **Depends on:** Task 13; real API after Task 6. (#440 G3, size M)

**Files:** Create `src/Lms.Client/Components/LiveSessions/LiveSessionPanel.razor`, `JoinButton.razor`, `wwwroot/js/live-join.js`, `LiveParticipantsDialog.razor`; Modify `ModuleBody.razor`, `NotificationText` + resx (`Notif_Live_Reminder`). Test `tests/Lms.Client.Tests/LiveSessions/LiveSessionPanelTests.cs`, `JoinButtonTests.cs`.

**Interfaces:** per #440 G3: countdown from `serverNowUtc`; Join enabled only in the window; the tab is opened **synchronously** on click (`about:blank`, `noopener,noreferrer`), then pointed at the URL from `POST …/join`; failure closes it and shows the error; a blocked tab falls back to a visible "Open the meeting" link; the URL never appears in markup before the click; 409 refreshes the panel; recording link with `rel="noopener noreferrer nofollow"` and its host name; staff participants dialog with Matched/Guest chips.

**Tests:** `Join_is_disabled_before_the_window_and_enabled_inside_it_by_server_time`, `Join_opens_the_tab_synchronously_before_the_api_answers`, `Join_url_is_not_in_the_markup_before_the_click`, `Join_failure_closes_the_blank_tab_and_shows_the_error`, `Join_falls_back_to_a_visible_link_when_the_tab_is_blocked`, `Join_in_a_closed_window_refreshes_the_panel`, `Invisible_session_module_is_not_found`, `Recording_link_has_nofollow_noopener_and_shows_its_host`, `Participants_list_shows_guests_distinctly`, `Notification_types_have_both_languages`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): live session join panel and recordings`.

### Task 16: Package client, upload, validation status and attach — lane: FE2

**PRD:** FR-STD-01, FR-STD-02. **Depends on:** Task 2, Task 12; real API after Task 8. (#440 G4, size L)

**Files:** Create `src/Lms.Client/Services/Packages/IPackagesApi.cs`, `PackagesApi.cs`, `FakePackagesApi.cs`; `src/Lms.Client/Components/Packages/PackageUploadDialog.razor`, `PackageStatusView.razor`, `PackageAttachForm.razor`, `PackageRejectReason.razor`; Modify `AddModuleMenu.razor` (FE1 owns the file: FE2 sends the two entries to FE1 or edits only the agreed slot after FE1's OK), resx (`Package_*`, `Error_package_*`). Test `tests/Lms.Client.Tests/Packages/*`.

**Interfaces:** per #440 G4: `.zip` / `.h5p` and size checked against `GET /api/assets/limits` before sending; shared upload transport; status polling with a cap, backoff and stop on close, then "Check again"; reject reason shown per code in plain words (incl. `needs_nested_frames`, `multi_sco`); `Ready` shows title / version / library and the attach form with `CompletionRules.ForActivity`; delete with confirm; `feature_disabled` plain message.

**Tests:** `Rejects_wrong_extension_and_oversize_before_upload`, `Shows_progress_then_validating_then_ready`, `Polling_stops_when_the_dialog_closes`, `Polling_is_capped_and_offers_check_again`, `Rejected_package_shows_a_localized_reason_per_code`, `Ready_package_can_be_attached_with_a_completion_rule`, `Attach_is_disabled_until_ready`, `Feature_disabled_shows_the_plain_message`, `Student_sees_no_upload_entries`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): package upload, validation status and attach`.

### Task 17: Sandboxed package player and message bridge — lane: FE2

**PRD:** FR-STD-01, FR-STD-02, X-05, D51, D56. **Depends on:** Task 3 signed, Tasks 9 and 16 (Tasks 10/11 for real content). (#440 G5, size L)

**Files:** Create `src/Lms.Client/Components/Packages/PackagePlayer.razor`, `PackagePlayerToolbar.razor`, `PackageSaveState.razor`, `PackageBridge.cs` (interop wrapper), `wwwroot/js/package-bridge.js` (the only JS that touches package messages); Modify `ModuleBody.razor`. Test `tests/Lms.Client.Tests/Packages/PackagePlayerTests.cs`, `PackageBridgeTests.cs`.

**Interfaces:** per #440 G5 and Decision 14: iframe `sandbox="allow-scripts allow-forms"` (never `allow-same-origin`, top navigation or popups), `title`, `referrerpolicy="no-referrer"`, `allow=""`; one `MessageChannel` after `load`, window messages counted and dropped; port messages validated (type, numeric `seq`, 8 KB) and never rendered or used as ids; nothing sensitive posted to the frame; port and listeners removed on dispose; commits throttled/coalesced with `seq`; `finish` drains then sends the last `seq`; flush on `pagehide`; re-launch at 80 % of the token lifetime at a safe point and "Reload" on expiry (same attempt); staff Preview with "nothing is recorded" banner; Resume / Start again per server state; container Fullscreen, Exit outside the frame, 70 vh, 360 px hint, third-party accessibility note.

**Tests:** the 24 named tests of #440 G5 (incl. `Player_iframe_is_sandboxed_without_same_origin_top_navigation_or_popups`, `Port_is_transferred_after_load_and_only_port_messages_are_accepted`, `Window_messages_never_trigger_a_commit_even_from_the_player_frame`, `Origin_null_is_not_used_as_a_trust_signal`, `Nothing_sensitive_is_posted_to_the_frame`, `Port_and_listeners_are_removed_on_dispose`, `Commits_carry_seq_and_are_throttled_and_coalesced`, `Finish_drains_the_port_then_sends_the_last_seq`, `Pagehide_flushes_the_last_state`, `Expired_token_offers_reload_with_the_same_attempt`, `Preview_is_staff_only_records_nothing_and_shows_the_banner`, `Page_renders_rtl_in_arabic`). **Commit** `feat(client): sandboxed package player and port bridge`.

**Acceptance:** bUnit tests green; QA3 security review approved; the real-browser assertions are in Task 21/23.

### Task 18: Live session and package surfaces on Home and course home — lane: FE1

**PRD:** FR-VC-01, X-01. **Depends on:** Tasks 15, 17 (package states are generic via `ModuleNodeDto`). (#440 G6, size S)

**Files:** Modify `MyCoursePage.razor`, `Home.razor` (FE2 owned `Home.razor` in M4a: FE1 edits only the new card component and its one-line registration), Create `Components/LiveSessions/UpcomingLiveSessionsCard.razor`, `TodaySessionsCard.razor`. Test extends `HomeCardsTests`, `MyCoursePageTests`.

**Tests:** `Home_cards_depend_on_permissions` (extended), `Course_home_lists_upcoming_sessions_in_server_order`, `Teacher_sees_todays_sessions_with_manage`, `Notification_types_have_both_languages` (extended). **Commit** `feat(client): upcoming live sessions on Home and course home`.

### Task 19: M4b FE hardening — accessibility, 360 px, RTL sweep, E2E hooks — lane: FE2 + UI/UX Designer review

**PRD:** X-01, X-10. **Depends on:** Tasks 13–18. (#440 G7, size M)

**Files:** all M4b pages; `docs/screenshots/pr<N>/`; extend `M4aAccessibilityTests` into `M4bAccessibilityTests`.

**Interfaces:** named controls and field labels, iframe `title`, countdown / save state `aria-live`, dialog focus; `data-testid`s agreed with QA1/QA2 for Tasks 22–23; 360 px labelled cards; light + dark screenshots in ar and en; designer review against Task 12. **Commit** `fix(client): M4b accessibility and responsive sweep`.

---

### Task 20: M4b isolation and permission probe suite — lane: QA (QA2)

**PRD:** §4.2.3, FR-SEC-05, X-05. **Depends on:** BE Tasks 4–11 (grows per merge; first PR after Task 4).

**Files:** Create `tests/Lms.IntegrationTests/Security/M4bCallers.cs` (teacher A/B, students in group 1 / group 2 / another class, School-2 admin, client-B admin, guardian, anonymous), `ProbeRows/M4b*.cs`, `M4bEndpointMatrixTests.cs`, `M4bIsolationTests.cs`, `M4bVisibilityProbeTests.cs`.

**Interfaces:** every M4b route × caller (teacher A on Y's sessions, participants, packages, preview → 404; group-2 student on a group-1 session join / package launch / attempt commit → 404; student on authoring, participants, preview → 403; guardian → 403; client-B admin → 404); webhook rows (401/413/429/200 once); package-host rows (forged / foreign-client / revoked token 404, no `Set-Cookie`, CSP `sandbox` on errors); `Every_M4b_api_route_is_in_the_probe_matrix`; an isolation test per M4b table; mutation checks on `IModuleAccess` use and token audience. **Commit** `test(security): M4b endpoint matrix, visibility and isolation probes`.

### Task 21: Security review — package host, archives, tokens and player — lane: QA (QA3)

**PRD:** X-05, D51, D56, PRD §9.2. **Depends on:** Tasks 3, 8–11, 17 (grows as they merge).

**Files:** Create `tests/Lms.IntegrationTests/Security/PackageArchiveAttackTests.cs` (the full crafted corpus end to end through the upload route), `PackageHostHeaderTests.cs` (every response type, errors, top-level navigation, SVG, unknown types), `PackageTokenAttackTests.cs` (forgery, expiry, audience, revocation window, log scrubbing incl. exception pages); `tests/Lms.E2ETests/M4/PackageSandboxTests.cs` (real browser, two origins: the rendered `sandbox` attribute, the frame cannot read app storage/cookies, a forged `"null"`-origin window message is ignored, own-frame navigation to the outside is blocked by `frame-src`, and a **mutation check that fails if `allow-same-origin` is added**); `docs/security/m4b-review.md` checklist (zip, XML/JSON, CSP, token, cross-tenant, webhook, secrets).

- [ ] **Steps 1–5** → commit `test(security): M4b package host and player review`. Findings filed as `bug` + `sev:*` against the owning lane.

### Task 22: E2E exit demo 1 — teacher schedules a recurring live session; student joins; recording and participants arrive — lane: QA (QA1)

**PRD:** FR-VC-01, DEV-PLAN §1 M4 exit (live part). **Depends on:** Tasks 4–7, 13–15, 18; `FakeMeetingProvider` in the E2E stack; a dev-only signed webhook trigger (`POST /api/dev/zoom/events`, Development only, test in Task 7).

**Files:** Create `tests/Lms.E2ETests/M4/LiveSessionTests.cs`, page objects `LiveSessionsPage`, `LiveSessionDialog`, `LiveSessionPanel`.

**Scenarios (`[Theory]` `"ar"` / `"en"`; Arabic asserts `dir="rtl"`):** `Teacher_schedules_weekly_session_and_students_see_the_modules`; `Student_join_is_disabled_before_the_window_and_opens_a_new_tab_inside_it`; `Student_of_other_group_cannot_see_the_session`; `Provider_events_attach_recording_and_participants_for_the_teacher`; `Teacher_pastes_a_recording_link_when_cloud_recording_is_off`; `Reminder_appears_in_the_bell`.

- [ ] **Steps 1–5** (one E2E stack at a time, `e2e.lock`). **Commit** `test(e2e): M4b live sessions`.

### Task 23: E2E exit demo 2 — SCORM package completes a module; H5P scores — lane: QA (QA2)

**PRD:** FR-STD-01, FR-STD-02, FR-CNT-04, DEV-PLAN §1 M4 exit (package part). **Depends on:** Tasks 8–11, 16, 17, 19; the ADR corpus packages licensed for the repo.

**Files:** Create `tests/Lms.E2ETests/M4/PackageTests.cs`; E2E fixture starts the content host on `127.0.0.1:<port>` next to the app on `localhost`.

**Scenarios (ar + en):** `Teacher_uploads_scorm_sees_it_validated_previews_and_attaches_it`; `Student_completes_scorm_module_and_progress_and_grade_event_update`; `Student_resumes_scorm_after_reload_with_suspend_data`; `Rejected_package_shows_the_localized_reason`; `Student_completes_h5p_question_set_and_score_is_recorded`; `Preview_creates_no_student_data`.

- [ ] **Steps 1–5** as Task 22. **Commit** `test(e2e): M4b SCORM and H5P packages`.

---

## Exit criteria (M4b done, and with it M4)
1. Tasks 22 and 23 green in CI in ar and en; Task 20 and Task 21 green on `main`; M4a exit demos still green.
2. The package-hosting ADR (Task 3) is signed by QA and every implementation follows it; `docs/security/m4b-review.md` has no open `sev:critical` or `sev:high`.
3. No secret, join/start URL or package token in logs, DTO lists or audit (tests in Tasks 5, 6, 9).
4. Production prerequisites documented: content-host domain, TLS and ingress (`docs/runbook-package-host.md`), Zoom app + host pool + webhook URL configuration (`docs/runbook-zoom.md`, written in Task 7).
5. `docs/STATUS.md` updated; owner told M4 is done; open Z1/Z5/Q4/Q5 defaults listed for confirmation.

## Execution notes for the Team Lead
- **Issues:** one per task (23: 2 Lead, 1 ADR, 8 BE, 1 UX, 7 FE, 4 QA) on milestone "M4 Teaching & Learning", labels `M4` + `lane:*` + `area:live` / `area:packages` (create both labels), on the "LMS v1" project; replace "Task N" in Depends-on lines with issue numbers.
- **Order:** Task 1 → Tasks 4, 5, 13 start at once (live sessions do not wait for the spike). Task 3 runs in parallel; Task 2 merges after the ADR draft fixes the token/commit shapes; Task 8 starts on the upload half after Task 2; **Tasks 9, 10, 11, 17 wait for the signed ADR.**
- **Critical path:** Task 3 → Task 9 → Task 10 → Task 17 → Task 23.
- **BE lanes (split by folder):** **BE1** — `Domain/LiveSessions`, `Infrastructure/LiveSessions`, `Features/LiveSessions` (except the provider-status endpoint), the `IBackgroundJobs` addition: 4 → 6. **BE2** — `Infrastructure/Zoom`, `Features/Integrations`, `ProviderStatusEndpoint.cs`, the `ZoomWebhookReceipt` entity file: 5 → 7. **BE3** — `Domain/Packages` (upload, SCORM files), `Infrastructure/Packages` (kernel, `Scorm/`), `Features/Packages/PackageEndpoints.cs`: crafted corpus (Task 3) → 8 → 10. **BE4** — `Lms.Api/PackageHost/`, `Infrastructure/Packages/H5p/`, attempts/revocations, `Features/Packages/LaunchEndpoints.cs` / `CommitEndpoints.cs`: 3 → 9 → 11; BE4 also helps QA2 with probe rows for the package host.
- **Shared BE files (serialize merges):** `Program.cs`, `SecurityHeadersMiddleware` (Task 9 only), `ProbeRows/` (one file per PR), `IsolationBypassGuardTests.cs` allow-list (one entry: `ZoomWebhookReceipt`, Task 7), migrations, `Lms.Shared` (Lead only).
- **FE lanes:** **FE1** — `Services/LiveSessions`, `Pages/LiveSessions`, `Components/LiveSessions`, `AddModuleMenu.razor` owner: 13 → 14 → 15 → 18. **FE2** — `Services/Packages`, `Components/Packages`, `wwwroot/js/package-bridge.js`: 16 → 17 → 19. Shared FE files: resx (own prefix), `ClientServices.cs` (one line each), `ModuleBody.razor` (FE1 adds the branches in Task 13; FE2 replaces only the package placeholder in Task 17).
- **UI/UX Designer:** Task 12 first, then design review on Tasks 14–17 and the Task 19 sign-off.
- **QA:** **QA1** reviews BE1, BE2 and FE1 PRs; owns Task 22. **QA2** reviews BE3 and FE2 Task 16; owns Tasks 20 and 23. **QA3** signs the ADR (Task 3), gives the security review on Tasks 7, 8, 9, 10, 11 and 17, owns Task 21. Every review checks the Review Focus items, explicit permissions + relationship checks, `IModuleAccess` on student reads, resource parity, RTL screenshots and the one-E2E-stack lock.
- **Splits from DEV-PLAN §2 M4:** item 8 → Tasks 4–7, 13–15, 18, 22; item 9 → Tasks 3, 8–10, 16, 17, 21, 23; item 10 → Task 11 (+ 16, 17, 23); item 11 dropped (D50).

## Still open (defaults in force, confirm with the owner later)
- **Z1** Zoom licence count = `Zoom:HostPool` size (configuration).
- **Z5** Participant matching by user id / email; guests never auto-attached (Decision 7).
- **Q4** `feature_disabled` = 403; H5P under `ScormContent`; no capabilities read (Decision 8).
- **Q5** Caps as Decision 9 (all settings).
- **Z6** DPA with Zoom and parent deletion requests (owner/legal).

## PRD / DEV-PLAN inconsistencies found
- FR-CNT-01 and DEV-PLAN M4 item 11 still list LTI (removed by D50).
- FR-VC-01 says "participants imported into attendance"; attendance is M5 (FR-ATT-01). M4b stops at participant rows and `ILiveSessionAttendanceSink`.
- §4.4 has one `ScormContent` flag and no H5P flag; this plan gates H5P with it (Decision 8).
- `CompletionRules.ForActivity` is empty for LiveSession/Scorm/H5p and `GradeSourceKind` lacks Scorm/H5p; Tasks 1 and 2 fill them.
