# M4b Backend Proposal: Zoom live sessions, SCORM, H5P

> **Status: DRAFT proposal for the Team Lead (no code), revision 2 (2026-11-12)** after QA's changes-requested review of #427 at `fb3414d` and the spike result of #444. Sections 2, 4, 5, 7, 8 and 9 changed (7 and 9 largely rewritten); the PR comment answers the review point by point. Written by Backend developer 2 (BE2) in the M4a plan format so the Lead can turn it into the M4b plan, the wave 3 contract PR and the issues. Companion of the "Later: M4b" section of `2026-11-14-m4a-teaching-learning.md`. Owner decisions applied: **D49** (one platform Zoom app, secrets only in server configuration), **D50** (no LTI), **D51** (M4b = Zoom, SCORM, H5P; SCORM/H5P served from a sandboxed origin with a strict CSP **after a security spike**). M4b is sized only after the spike (Task 2) and the Zoom capacity question (Open question Z1) are answered.

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
| Security headers | `SecurityHeadersMiddleware` sends `X-Frame-Options: DENY` and `Content-Security-Policy: frame-ancestors 'none'` on every response | The package host must send its own headers (7.4: CSP with `sandbox`, `frame-ancestors <app origin>`, no `X-Frame-Options`), and the app origin needs `frame-src <content origin>` (#444 F1); the package host is a separate host-matched pipeline, not a flag in the shared middleware (Task 6). |
| Hosting | One ASP.NET host serves the Blazor WASM client and `/api` from one origin | A second origin for untrusted content, on a separate registrable domain, is a deployment decision (spike S1, 7.6; #444 verified a two-origin prototype). |

## 2. Decisions this proposal assumes (confirm in review)

1. **Zoom (D49):** one platform Server-to-Server OAuth app; `ZoomOptions` (account id, client id, secret, host pool, webhook secret) bound from configuration/secret store only. No per-client Zoom settings, tables or credentials. Sessions are tagged with the client and class course through Zoom **`tracking_fields` only**, for support and never as authorisation. **The meeting topic carries no client, class, student or other personal data** (participants see the topic): it is the teacher's session title, validated (length, no markup), and the schedule dialog tells the teacher that participants see it (Z6).
2. **Zoom host model:** a Zoom meeting needs a licensed host user. Assumed default: a **configured pool of platform host users**; each session gets a host chosen by an allocator that refuses overlapping use of one host (see Z2). The alternative (the teacher's own Zoom user) needs every teacher to hold a licence in the one account and is not proposed.
3. **Zoom events:** recordings and participants are learned from **Zoom webhooks** (`meeting.ended`, `recording.completed`) verified as in section 4 (URL-validation challenge, signature over the raw body, timestamp window, replay protection), with a **polling fallback** (Reports/Recordings API) for a missed webhook. If the platform has no public HTTPS endpoint for webhooks (Q-Z3), polling is the only path and the teacher can paste the recording link by hand (FR-VC-01 allows "recording link attached after the session").
4. **Packages:** SCORM and H5P share one **package kernel** (upload, hardening, validation job, storage layout, serving token); the two formats differ only in validator, launch resolution and tracking adapter. Single-SCO SCORM first; multi-SCO/sequencing is out of v1 (S5).
5. **Untrusted content never runs on the app origin** (D51). The recommended layout is a **separate registrable domain for package content** (not a subdomain of the app's, 7.6) with a strict CSP (including the CSP `sandbox` directive) and an iframe `sandbox` without `allow-same-origin`, signed path tokens instead of cookies (S1, #444). The path-on-the-app-origin layout is the documented fallback only if the owner cannot provide a second host name.
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
- `POST /api/integrations/zoom/webhook` (anonymous by design; the only anonymous M4b route, listed in the probe matrix with its own checks). Handling, in this order:
  1. **Body-size cap** (256 KB, read with a limit before anything else) and a **rate limit** on the anonymous route (per source IP and global; over the limit is 429 without touching the database).
  2. **URL validation:** the `endpoint.url_validation` event Zoom sends before delivering anything is answered with `{ plainToken, encryptedToken }`, where `encryptedToken` is the hex HMAC-SHA256 of `plainToken` with the webhook secret token.
  3. **Signature over the raw body:** the `x-zm-signature` header (`v0=` + hex HMAC-SHA256 of `v0:{x-zm-request-timestamp}:{raw body}` with the secret token) is computed over the **raw request bytes, before JSON parsing**, and compared in **constant time** (`CryptographicOperations.FixedTimeEquals`). A missing or bad signature is **401** (not 404), with no hint of which part failed; a failure is a security event without the payload.
  4. **Timestamp window** (5 minutes): a stale or future timestamp is 401.
  5. **Idempotency:** the event id goes into `ZoomWebhookReceipt` (unique); an event id already processed is answered **200 and not processed again** (Zoom retries legitimately re-deliver, and a 4xx would make it disable the subscription). A replayed *captured* request is refused by steps 3 and 4 once its timestamp is stale, so a replay is either 401 (stale) or a harmless 200 no-op (inside the window).

  Probe rows: bad signature 401, stale timestamp 401, missing signature 401, oversize body 413, valid event twice 200 and processed once, valid URL-validation challenge 200 with the encrypted token.
- `GET /api/live-sessions/provider-status` (staff): `{ configured, healthy }` so the UI can say "ask your platform administrator" without exposing configuration.

**Packages** (`Packages.Upload` for writes, `Packages.View` for reads)
- `POST /api/class-courses/{id}/packages` (multipart; type and size from `Files.MaxUploadMb`, answers 202 with `PackageUploadDto{ status: Uploaded }`; validation runs as a system job), `GET /api/packages/{id}` (status, reject reason, manifest title), `POST /api/packages/{id}/module` (attach to a lesson through `IModuleWriter`, only when `Ready`), `DELETE /api/packages/{id}`.
- `POST /api/packages/{id}/launch` (student, visible module only; also the re-launch that issues a fresh token, 7.5) → `{ launchUrl, attemptId, serverNowUtc }` where `launchUrl` is on the package host with a signed path.
- `POST /api/packages/{id}/preview` (staff: `Packages.View` + class-course relationship; 404 on a miss) → `{ launchUrl, serverNowUtc }` with a preview token; records no attempt, completion or grade (7.8).
- `POST /api/package-attempts/{id}/commit` (the **app page** posts what the content reported over the private port: CMI/xAPI data with a client `seq`; the server enforces key allow-list, size caps and a per-attempt rate; refuses preview tokens) and `POST /api/package-attempts/{id}/finish`.
- **Package host** (a separate minimal pipeline, not `/api`): `GET /p/{token}/{**path}` streams extracted files with the headers of 7.4; no cookies, no auth header, the token is the only credential.

## 5. Permissions, flags, relationship, audit

- **Wave 3 permissions:** `LiveSessions.{View, Manage}`, `Packages.{Upload, View}`. Matrix proposal following M4a: OrgAdmin and Manager `View, Manage/Upload` (so the D16 escalation guard stays satisfied, as D52 did for `Modules.Manage`), Supervisor `View`, Teacher `View, Manage/Upload` restricted to **own class courses** (relationship rules), Student `View` restricted to **own enrollments**, Counselor none, Guardian none.
- **Flags:** `FeatureFlags.VirtualClasses` gates every live-session write and `join`; `FeatureFlags.ScormContent` gates package upload and launch (H5P rides the same flag unless the Lead wants a separate `H5pContent` flag, Q4). Reads of existing sessions/packages are not gated.
- **Audit:** the five entities above; participants, attempts and webhook receipts are not audited row by row. Package rejects and webhook signature failures are logged as security events (`AuditSecurityEvents`) without payloads.
- **Secrets:** `Zoom:*` only in configuration; **Validation happens when the flag is turned on**, not at startup (flags are per unit and change at runtime): enabling `VirtualClasses` for a unit while `ZoomOptions` are missing or unhealthy is refused with 409 `provider_not_configured`. `GET /api/live-sessions/provider-status` stays the runtime signal (`configured=false` or `healthy=false` after the fact), and create/join answer `provider_unavailable` while it is. No secret in logs, DTOs, audit or the database.

## 6. Zoom design

- `IMeetingProvider` (Domain): `CreateMeetingAsync`, `UpdateMeetingAsync`, `DeleteMeetingAsync`, `GetParticipantsAsync`, `GetRecordingsAsync`, `GetHealthAsync`; `ZoomMeetingProvider` in Infrastructure; `FakeMeetingProvider` for tests and local runs.
- **Auth:** S2S OAuth `account_credentials` grant; the token is cached until shortly before expiry (singleton, one refresh in flight), never logged.
- **Rate limits and capacity (D49 risk):** one account serves every client. All calls go through a **single outbound limiter** (token bucket per Zoom rate-limit class, honouring `429 Retry-After`), a **bounded queue** and a per-client daily create cap (setting) so one client cannot starve the rest. When the provider is down, rate limited or the host pool is exhausted, create/update answer `503`/409 `provider_unavailable` with `Retry-After`; **the session row is kept in `SyncFailed`** so the teacher sees it and a recurring job retries (no silent loss).
- **Hosts and concurrency (Z2):** the allocator picks a pool host with no overlapping live session (buffer 10 minutes) under a database lock; concurrent creates for the same slot are serialised and the loser gets another host or `provider_unavailable`. Zoom's per-licence concurrent-meeting rule is the hard limit.
- **Recurring:** the platform owns the series: it creates N individual meetings (max 52, one per occurrence), not a Zoom recurring meeting, so each occurrence keeps its own host allocation, join window, reminder and recording. Editing "this and following" recreates the future meetings.
- **Join links:** students never receive the raw Zoom URL in a list; `POST .../join` returns it only inside the window. Registration/waiting-room settings are fixed by the platform (waiting room on, join before host off, no cloud recording unless the unit enables it).
- **Reminders:** `Live.Reminder` through a scheduled system job at `Live.ReminderMinutes` (default 30) before start, to the module's targeted students; re-scheduled on update, cancelled on delete, idempotent via `ReminderSentAtUtc`.
- **After the session:** webhook (or polling) → status `Ended`, participants import (`MatchState`), recording link attach; then `ILiveSessionAttendanceSink` is called for M5 (a no-op registration until M5).

## 7. SCORM and H5P design (spike evidence: #444, `docs/spikes/2026-11-12-m4b-package-host.md`; the ADR of Task 2 is the final word)

### 7.1 Upload and hardening (shared by SCORM and H5P)

- **Limits:** archive size ≤ `Files.MaxUploadMb`; **streamed** extraction counting real bytes (never trusting headers); caps proposed for S6 to confirm: ≤ 10 000 entries, ≤ 1 GB uncompressed, per-entry ratio ≤ 100:1 and total ratio ≤ 50:1, path depth ≤ 20, name ≤ 255.
- **Rejected entries:** names that normalise outside the root (`..`, absolute paths, drive letters, backslashes), symlinks/hardlinks, duplicate names after **NFC normalisation and case folding**, device/reserved names, executables and server-side scripts (`.exe .dll .so .php .jsp .asp .cgi`…), encrypted entries, and **nested archives** (`.zip .jar .war .7z .rar .tar .gz` inside the package; never extracted, v1 rejects them, S6 may relax to "stored as a download-only file").
- **Zip64 and names:** Zip64 archives are read (the entry and size counters use 64-bit values and the caps above); entry names are decoded as UTF-8 when the language-encoding flag (bit 11) is set, otherwise CP437; a name that does not round-trip or contains control characters, a BOM or bidi override characters is rejected.
- **Extraction job (a bomb must not starve the job server):** a system job with a **hard timeout** (proposal 5 minutes, then `Rejected` with `validation_timeout`), a **concurrency limit** (2 validation jobs per server, 1 in flight per client), streamed to a temp area with a **per-job disk budget**, temp cleanup in a `finally` and by a sweep; memory stays bounded because entries are streamed, never buffered whole.
- **Quotas:** a per-client extracted-storage quota (`Packages.MaxClientStorageMb`, Q5), checked before extraction (declared size) and again while streaming (real bytes). Over quota is `package_too_large`.
- **Store:** objects go under `packages/{orgId}/{packageId}/` with **server-assigned content types from an extension allow-list** (never the zip's claim, no HTML sniffing). **Deleting a package purges its extracted prefix** (and the archive), through a purge job that retries and is covered by a test; a `Rejected` upload purges its temp area at once.
- **XML (SCORM manifest and any XML we read):** `XmlReaderSettings { DtdProcessing = Prohibit, XmlResolver = null }`, `MaxCharactersInDocument` capped, `MaxCharactersFromEntities = 0`. **No resolver at all**: `xsi:schemaLocation`, `xml:base`, `xi:include` and external schema/DTD references are never fetched and never used to resolve a path (the launch `href` is resolved against the package root only). No schema validation that loads network or file resources.
- **H5P JSON:** `h5p.json`, `content.json` and `library.json` are parsed with a size cap (1 MB each), `JsonDocumentOptions.MaxDepth = 32` and caps on property and array counts; the library folders follow the same entry rules as above.

### 7.2 Where the SCORM API lives (answer to QA Must 1; #440 Q-G6; spike F6, question S10)

SCORM content looks for `API` (1.2) / `API_1484_11` (2004) by checking its own window and then walking `window.parent` / `opener`. The package runs in an `iframe sandbox="allow-scripts allow-forms"` **without** `allow-same-origin`, so its document has an **opaque origin**, and a walk to the app page is a cross-origin access that throws. The shim therefore cannot live in the app page, and the original text ("the launch page's parent frame") is withdrawn. #440 is right: **the content host serves the shim, the app only forwards** (Q-G6 is answered here and in the ADR, once).

There is one more constraint that the review's wrapper idea has to respect: **sandbox flags are inherited by nested frames**, so a frame nested in a sandboxed document is itself sandboxed without `allow-same-origin`, and two documents with opaque origins are never same-origin with each other. A "wrapper page that hosts the SCO in a nested same-origin frame" cannot work under `sandbox` without `allow-same-origin`. Two designs remain; the spike decides between them with the real-package corpus (S10):

| | **Design A (v1 default): shim in the launch document** | **Design B (fallback): wrapper and SCO on a per-launch origin** |
|---|---|---|
| Mechanism | The content host serves the launch HTML with a small **prelude script injected first** (`<script>` at the top of `<head>`, server side, over the extracted file, cached by hash) that defines `window.API` / `window.API_1484_11` on the SCO's own window and talks to the app over the private `MessageChannel` port (spike F3). Content that checks its own window first (the ADL `findAPI` and most authoring-tool wrappers do) finds it without any cross-window access. | `/p/{token}/_wrapper` (served by the content host) sits in the app's iframe and hosts the SCO in a **nested same-origin frame**; the wrapper owns the API and forwards to the app by `postMessage`/port. Needs a **per-launch sub-domain** (`{launch}.content.<site>`, wildcard TLS) so each launch has its own origin without `sandbox`, and the response carries CSP `sandbox allow-scripts allow-forms allow-same-origin` so that the only same-origin relationship is wrapper ↔ SCO inside one throw-away origin that holds no app data. |
| CSP | `frame-src 'none'` (no nested frames at all). | `frame-src 'self'` **on the wrapper only**; SCO pages keep `frame-src 'none'`. The CSP is per response type, not one string for the host. |
| Package scope | Single-document SCOs. A launch page that is a frameset or nests the SCO in a child frame that looks in `parent` is **rejected at validation** (`package_invalid`, reason `needs_nested_frames`). | Frameset and nested-frame SCOs work. |
| Cost | None beyond the injection. | Wildcard certificate, wildcard DNS, one more owner decision; cookie and storage access for the throw-away origin (harmless: nothing of the app lives there, but S2 must show it). |

**Recommendation:** Design A for v1 and **reject** the packages it cannot serve, so the strict sandbox stays as is. If the corpus shows that real packages in scope (S10: how many of the three real SCORM files are framesets/nested frames) need Design B, that becomes an **owner decision** (wildcard TLS, second site) and Task 7 is re-sized; it is not designed further before the evidence. The CSP of section 7.4 is correct for Design A; the Design B differences are stated above so the ADR can adopt them without rewriting the plan.

H5P (h5p-standalone) does not look for a parent API: it posts xAPI through the same port, so Design A needs nothing more for H5P.

### 7.3 Consequences of the opaque origin (answer to QA Must 2; each is a spike probe in S2/S3/S4)

| # | Consequence | What we do | Probe |
|---|---|---|---|
| a | The frame's `postMessage` events reach the app with `event.origin === "null"`, not the content host. | The app **never trusts `message` events by origin** (there is nothing to compare). It transfers one `MessagePort` after `load` and trusts **only that port** (spike F3); window-level messages are counted and dropped. If an event check is ever needed it is `event.source === frame.contentWindow`, never `origin`. #440's `Messages_from_another_origin_are_ignored` becomes "messages not on the port are ignored; `origin` is `"null"` and carries no trust". | S4 |
| b | Cookies, `localStorage`, `sessionStorage` and IndexedDB **throw** (`SecurityError`). Many SCORM/H5P packages touch them. | The injected prelude defines **in-memory stand-ins** for `localStorage`/`sessionStorage` (best effort) and a `document.cookie` accessor; anything the content wants to keep goes through the SCORM data model (`cmi.suspend_data`, `cmi.core.lesson_location`) or the H5P state in the commit, which is the standards-based way. A package that still throws on load is `Rejected`/unsupported and counts in the S2 corpus result. | S2 |
| c | The frame's own XHR/`fetch` to its content host is **cross-origin** from an opaque origin and needs CORS; `connect-src 'none'` blocks it (H5P loads its JSON by XHR). | `connect-src 'self'` and a CORS policy **on the content host only**: `Access-Control-Allow-Origin: *`, **no credentials**, methods limited to `GET, HEAD, OPTIONS`, no custom request headers. The paths are token-addressed and public-by-token, so `*` discloses nothing new. Network access to anything else stays blocked. This **supersedes** `connect-src 'none'` in the spike prototype; the spike is re-run with this policy before the ADR is signed. | S2 / S3 |
| d | `Cross-Origin-Resource-Policy: same-site` would block the frame's **own** subresources (they are cross-site to an opaque origin). | **No CORP header** on the content host (it is public-by-token and never carries cookies), no COEP/COOP requirement. | S2 |

### 7.4 Serving the package host (S1 recommended)

`https://<content-host>/p/{token}/…` is a **separate pipeline** (not `/api`, not the shared `SecurityHeadersMiddleware` with a flag): a host-matched branch with its own headers. On **every** response (including errors):

- `Content-Security-Policy: sandbox allow-scripts allow-forms; default-src 'none'; script-src 'self' 'unsafe-inline' ['unsafe-eval' only if S3 proves it]; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors <app origin>`.
  The **`sandbox` directive in the header itself** (QA Must 3) gives the same restriction when someone opens a tokenised URL top-level or when the app ever forgets the iframe attribute (defense in depth; the iframe attribute stays, #440 G5).
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: private, no-store`, **no `X-Frame-Options`** (it would forbid the embed), **no `Set-Cookie` ever** (a test asserts it on every route of the host), no CORP (7.3d), CORS as 7.3c.
- **Direct top-level navigation to `/p/{token}/…` is harmless** and tested: the response has no cookies, the token expires (7.5), the CSP sandbox gives an opaque origin without top navigation or popups, `nosniff` stops type sniffing, and **any file outside the extension allow-list is served with `Content-Disposition: attachment`** and `application/octet-stream`. **SVG is active content**: it is served as `image/svg+xml` with the same CSP (scripts in it run only inside the sandbox) and with `Content-Disposition: attachment` when `Sec-Fetch-Dest: document`, so opening it top-level downloads it.
- The **app** origin gains `frame-src <content origin>` (spike F1; its `X-Frame-Options: DENY` and `frame-ancestors 'none'` stay); the content origin comes from configuration, never from request data.
- **Cross-tenant paths:** the token binds package id + student (+ attempt); the store key is built from the token's package id only; every path component is validated against the package's file index, so a path never selects another package or client.

### 7.5 Token design (answer to QA Should 6; S9)

- **Where and what:** `/p/{token}/…` (the token is a path prefix so relative URLs inside the package keep working, spike F5); `token = base64url(json{package, student, attempt?, aud, exp, iat}).HMAC-SHA256`, constant-time verify, forged/expired/malformed all give one identical 404.
- **Audience binding:** the signed payload carries `aud = <content host>` and the key derives from a **purpose string unique to the package host** (Data Protection purpose), so the token verifies only on the content host; the app origin has no `/p/` route, and a token of another purpose is not accepted by any app endpoint.
- **Leak surface:** `Cache-Control: private, no-store`, `Referrer-Policy: no-referrer` and **log scrubbing**: the request logger, the access logs of the reverse proxy (documented sample config) and exception pages record `/p/*` as `/p/[token]/…`; a test asserts the logger output has no token.
- **Lifetime and refresh (the spike's 10 minutes cannot serve a long SCORM session: relative asset requests carry the old prefix and would start to fail):** the token has a **session lifetime** (`Packages.TokenMinutes`, proposal 120, hard cap 8 h) and revocation is decoupled from expiry: hiding the module, un-enrolling the student or deleting the package writes a **revocation marker** per `(package, student)`; the host checks a 30-second in-memory cache of markers per request, with no per-request database read. The app page calls `POST /api/packages/{id}/launch` again (re-checks access, issues a new token) when the frame is reloaded or at 80 % of the lifetime and swaps the frame only at a safe point (SCORM `Commit` then reload, resume from `suspend_data`). S9 confirms the numbers against real traffic; the mechanism (session token + marker + re-launch) is what this plan commits to.

### 7.6 Content host: separate registrable domain (answer to QA Should 7; S1)

The content host is a **separate registrable domain** (for example `lms-content.example`), **not a subdomain of the app's**: a subdomain is same-site for `SameSite=Lax`, and an app cookie with a `Domain=` attribute would be sent to it (spike F2). Also: app cookies stay **host-only** (no `Domain=`), asserted by a test on `Set-Cookie`. **Dev/CI:** the spike verified `app.localhost` / `content.localhost` in headless Chromium; whether Chromium treats two `*.localhost` names as cross-site is an S1 probe, and the guaranteed cross-site pair for CI is **`localhost` and `127.0.0.1`**. Environments without a second domain use the documented fallback (CSP-`sandbox` path layout) only after a stricter QA review.

### 7.7 Tracking transport and the server contract (spike F3, F4)

- The **app page** makes the authenticated call (`POST /api/package-attempts/{id}/commit`); the package holds no token or cookie. The commit carries a **client sequence number** (`seq`); the server keeps the highest `seq` per attempt and ignores lower ones (spike F4: parallel commits arrive out of order); `finish` is a flush barrier (the app drains the port, then sends `finish` with the last `seq`).
- The **server enforces** the key allow-list, size caps (message 8 KB, value 4 KB, `cmi` 64 KB per attempt) and a per-attempt rate (`Packages.CommitMinIntervalSeconds`); the page-side limits are only a UX guard.
- `sendBeacon` is unavailable from the frame (`connect-src`); the **app page flushes on `pagehide`** from the last port state (spike S4).
- **SCORM:** the runtime maps `cmi.core.lesson_status` / `completion_status` / `success_status` and `score.raw/min/max` to completion and grade. **H5P:** `h5p.json`/libraries validated as 7.1; the player is a **vendored, pinned h5p-standalone** build served from the content host (no CDN); xAPI statements arrive on the port and only `completed/passed/scored` results are kept (capped). A full H5P server (PHP) is explicitly not used.

### 7.8 Teacher preview (answer to QA Should 11)

`POST /api/packages/{id}/preview` (`Packages.View` + class-course relationship, staff only; 404 on a miss, 409 `package_not_ready` unless `Ready`) returns `{ launchUrl, serverNowUtc }` with a **preview token** (`aud` as above, flagged `preview`, no attempt id). The preview records **no attempt, no completion and no grade event**: its commits are accepted by the port (so the content behaves normally), answered 204 and discarded; `finish` is a no-op. A student cannot obtain a preview token (permission and relationship), and a preview token is refused by `commit` (test). The E2E demo uses it to show a package without creating student data.

### 7.9 Spike acceptance, sign-off and failure policy (answer to QA Must 4)

**Corpus (committed under `docs/spikes/corpus/` when the licence allows, otherwise referenced by hash and kept by QA):**
- **3 real SCORM packages:** one 1.2 single-SCO, one 2004 4th edition, one authoring-tool export (Articulate/iSpring-style) that ships its own player; at least one of the three must use a frameset or a nested SCO frame (this is what decides S10).
- **2 real H5P files:** one Course Presentation, one Interactive Video or Question Set (H5P content that loads JSON and media by XHR).
- **Crafted zips:** zip-slip (`..`, absolute, backslash), bombs (ratio and total), symlink/hardlink, polyglot (zip + HTML), duplicate after case/NFC fold, Zip64, nested archive, CP437 vs UTF-8 names, device names, encrypted entry, XXE/billion-laughs manifest, `xml:base`/`schemaLocation` pointing at a local file and a URL, deep and huge `h5p.json`.

**Pass/fail rule per question** (the ADR records result, evidence and date for each; a question without evidence is "open", and open blocks Tasks 6–8):

| Q | Pass when |
|---|---|
| S1 | The package host is cross-site to the app in every environment (incl. CI), app `Set-Cookie` is host-only, and no app cookie is sent to the content host (asserted by the probe). |
| S2 | All 17 spike probes stay blocked **and** the 5 real packages (or the subset the ADR names as "in scope") load, run and complete under the 7.3/7.4 policy with no `allow-same-origin`; the opaque-origin probes (a)–(d) pass. |
| S3 | The set of packages needing `'unsafe-eval'` is counted; if it is non-zero the ADR states which packages and signs `'unsafe-eval'` on the content host only. `connect-src` never exceeds `'self'` and **no package may ever relax network access**. |
| S4 | The port pattern works for SCORM and H5P; forged window messages are ignored; out-of-order commits resolve by `seq`; server-side key/size/rate limits reject the abusive commits from the spike. |
| S5 | Single-SCO scope is confirmed against the corpus and the rejection reason for multi-SCO/sequencing is shown. |
| S6 | Every crafted zip is rejected with the intended code, no file is written outside the job's temp area, and the job stays inside the time/memory/disk budget on the bomb. |
| S9 | Forged, expired, foreign-audience and revoked tokens are refused; a session longer than the 10-minute token of the first spike still works with the 7.5 mechanism. |
| S10 | The corpus result decides Design A or B (7.2) in writing. |

**Sign-off:** **QA signs the ADR** (an approving review on the ADR PR plus the `qa:approved` label) **before Tasks 6 to 8 are sized or started**, as D51 requires ("after a QA security spike"). A change to the hosting design after the sign-off needs a new QA review of the changed part.

**A package that fails the strict CSP is rejected** (`package_invalid`, reason code `needs_relaxed_policy`) with a message that names the policy rule it broke. We **never relax network access** (`connect-src`), **never add `allow-same-origin`** to the shared sandbox, and the only permitted relaxation is the single `'unsafe-eval'` decision of S3.

## 8. Tasks (one issue and one PR each; lanes as in M4a; numbering independent of the M4a plan)

### Task 1: Contract, wave 3 (Lead contract)
**PRD:** FR-VC-01, FR-STD-01/02, D49–D51. **Depends on:** M4a contracts merged. Permissions `LiveSessions.*`, `Packages.*` and the matrix/relationship rules; DTOs and routes for sections 3–4; `CompletionRules.ForActivity` for `LiveSession` (None, Manual, View) and `Scorm`/`H5p` (Submit, Score); `GradeSourceKind.Scorm/H5p`; `ProblemCodes` (`provider_unavailable`, `provider_not_configured`, `live_session_not_open`, `package_invalid`, `package_too_large`, `package_not_ready`, `feature_disabled`) with the package reject reason codes (`needs_nested_frames`, `needs_relaxed_policy`, `validation_timeout`, `nested_archive`, `quota_exceeded`); `NotificationEvents.LiveReminder`; settings (`Live.JoinWindowMinutes`, `Live.ReminderMinutes`, `Live.MaxCreatesPerDay`, `Packages.MaxEntries`, `Packages.MaxUncompressedMb`, `Packages.CommitMinIntervalSeconds`, `Packages.MaxClientStorageMb`, `Packages.TokenMinutes`); the `preview` route and `seq` on the commit DTO; `AuditCatalog`; ar/en labels; contract tests.

### Task 2: Security spike, SCORM/H5P hosting (QA + BE2)
**Deliverable:** an ADR (`docs/adr/00xx-package-hosting.md`) plus a throw-away proof of concept (not merged to `main` code paths) answering the spike questions in section 9 against the **corpus and pass/fail rules of 7.9**. **Evidence so far:** #444 (BE4, `docs/spikes/2026-11-12-m4b-package-host.md`) answers S1 (in part), S4 and S9 (in part) on loopback and leaves a 17-probe harness; what remains is the real-package corpus (S2, S3, S10), the opaque-origin probes of 7.3, the crafted-archive corpus (S6) and the antivirus/H5P decisions (S7, S8). **QA signs the ADR (7.9) before Tasks 6 to 8 are sized or started.**

### Task 3: Meeting provider kernel and live session authoring (BE)
`LiveSession`, `LiveSessionSeries`, `IMeetingProvider`, `FakeMeetingProvider`, `ILiveSessionAttendanceSink`, authoring endpoints through `IModuleWriter`, `VirtualClasses` gate, closed class/year guard, isolation tests. Tests first: `Teacher_schedules_single_session_with_module`; `Recurring_creates_one_session_per_occurrence_max_52`; `Student_sees_only_visible_session_modules`; `Teacher_of_other_class_course_gets_404`; `Flag_off_refuses_writes`; `Closed_class_rejects_writes`; `LiveSession_is_isolated_per_client_and_subtree`.

### Task 4: Zoom provider (BE)
S2S OAuth, token cache, host-pool allocator with overlap lock, outbound limiter and queue, `provider_unavailable`, `SyncFailed` recovery job, secrets handling, provider status endpoint, `ZoomOptions` validation. Tests with a stubbed HTTP handler: `Token_is_cached_and_refreshed_once_under_concurrency`; `Rate_limited_call_honours_retry_after`; `Host_pool_never_double_books_a_slot_under_concurrency`; `Provider_down_keeps_the_session_in_sync_failed_and_the_sweep_retries`; `Secrets_never_appear_in_logs_or_dtos`; `Client_daily_cap_protects_other_clients`.

### Task 5: Live session lifecycle: join, reminders, webhooks (BE)
Join endpoint and window, `Viewed` completion, `IBackgroundJobs.ScheduleAsSystem` (infrastructure addition), reminder job and `Live.Reminder`, webhook endpoint (URL-validation challenge, raw-body constant-time signature, timestamp window, body cap, rate limit, idempotency receipt table, polling fallback), enabling `VirtualClasses` validates the provider (409 `provider_not_configured`), participants import, recording link, attendance sink call. Tests: `Join_outside_window_is_409_and_invisible_module_is_404`; `Student_never_receives_the_start_url`; `Webhook_with_a_bad_signature_or_old_timestamp_is_401`; `Webhook_url_validation_challenge_is_answered`; `Webhook_signature_is_checked_over_the_raw_body`; `Webhook_oversize_body_is_refused`; `Enabling_the_flag_without_zoom_options_is_409`; `Replayed_webhook_is_processed_once`; `Participants_match_by_user_and_unknown_stay_guests`; `Reminder_is_sent_once_and_rescheduled_on_update`.

### Task 6: Package kernel (BE, after the spike)
Upload, streamed hardening, validation job and states, `IPackageStore`, serving token, package host pipeline and headers (host-matched package pipeline with the 7.4 headers, `frame-src` on the app origin, host-only app cookies, log scrubbing of `/p/`), quotas, purge on delete, job timeout and concurrency, `Packages.*`, `ScormContent` gate. Tests: a crafted-archive corpus (`Zip_slip_entry_is_rejected`, `Decompression_bomb_is_rejected_by_real_bytes_not_headers`, `Symlink_entry_is_rejected`, `Too_many_entries_is_rejected`, `Executable_extensions_are_rejected`, `Duplicate_after_case_folding_is_rejected`), `Zip64_nested_archive_and_name_encoding_cases`, `Quota_and_job_timeout_stop_a_bomb`, `Deleting_a_package_purges_its_prefix`, `Package_path_never_leaves_its_package`, `Token_from_another_attempt_client_or_audience_is_refused`, `Revoked_student_token_is_refused_within_the_cache_window`, `No_response_of_the_package_host_sets_a_cookie`, `Package_host_responses_carry_csp_sandbox_nosniff_and_no_xfo`, `Top_level_svg_and_unknown_types_are_attachments`, `Logs_never_contain_the_token`, isolation tests.

### Task 7: SCORM (BE)
Manifest validation (no XML resolver, XXE-safe), version detection, launch resolution, the SCORM API prelude injected into the launch document (Design A, 7.2; Design B only if S10 says so), `preview` (no attempt), commit endpoint with allow-list/size/rate limits and `seq` ordering, completion and `Scored`/`Submitted` signals, grade event revisions, attempt handling. Tests: `Manifest_with_external_entities_is_rejected`; `Scorm12_and_2004_lesson_status_map_to_completion`; `Commit_over_size_or_rate_is_refused`; `Unknown_cmi_keys_are_dropped`; `Score_writes_a_grade_event_once_per_change`; `Student_cannot_launch_an_invisible_module`; `Preview_records_no_attempt_and_students_cannot_preview`; `Out_of_order_commits_resolve_by_seq`; `Frameset_launch_page_is_rejected_in_design_a`.

### Task 8: H5P (BE)
`h5p.json` validation, library checks, vendored player assets on the package host, xAPI endpoint, score and completion. Tests: `Invalid_h5p_json_is_rejected`; `Xapi_result_maps_to_score_and_completion`; `Statements_of_other_attempts_are_refused`; isolation tests.

### Task 9: M4b probes and the security checklist (BE + QA)
Probe rows for every route (including the webhook with its 401/413/200 rows and the package host with the top-level-navigation checks of 7.4), the checklist in `docs/` (zip, XXE, CSP, token, cross-tenant), isolation tests per table, mutation checks on the visibility and token paths.

*Frontend (separate proposal):* schedule dialog and join states, package upload/status, sandboxed player with the `postMessage` shim. *QA:* package security review and two E2E demos (live session with the fake provider; SCORM package completing a module).

## 9. Open questions

**Spike questions (Task 2).** Status after #444 (BE4, Chromium, one hostile page on loopback; no real packages yet); pass/fail rules are in 7.9.

- **S1 Origin layout. Answered in part (#444):** separate host name + sandbox; two independent walls (opaque origin, no cookies sent to another host). **Open:** a separate *registrable domain* in every environment (7.6), who provisions DNS/TLS/ingress, and the CI pair (`localhost` / `127.0.0.1` vs `*.localhost`).
- **S2 Sandbox flags and opaque-origin behaviour.** `allow-scripts` sufficed for the probe; `allow-forms` stays (as #440 G5; `form-action 'none'` blocks submissions anyway). **Open:** (a) `event.origin` is `"null"`; (b) cookies/`localStorage`/`sessionStorage`/IndexedDB throw: which real packages break and does the in-memory stand-in hold; (c) XHR/`fetch` to the content host needs CORS and `connect-src 'self'` (re-run the prototype with it); (d) no CORP on the host. Nothing may require `allow-same-origin` on the shared policy.
- **S3 CSP vs real content.** Strict CSP blocks `eval`/`new Function` (#444). **Open:** how many of the real packages need `'unsafe-eval'`, inline handlers, `data:`/`blob:` workers or external fonts/CDNs. Policy fixed in 7.9: reject packages that fail the strict CSP; the single permitted relaxation is `'unsafe-eval'` on the content host.
- **S4 Tracking transport. Answered (#444 F3/F4):** private `MessagePort`, window messages ignored, `seq` for ordering, `finish` as a flush barrier, flush on `pagehide`. **Open:** real commit rates and the server-side limits they need.
- **S5 SCORM scope.** Single-SCO only? Sequencing and multi-SCO rejected or ignored; SCORM 2004 editions supported.
- **S6 Zip and XML limits** (7.1): entries, uncompressed size, ratios, depth, Zip64, nested archives, name encoding, per-client quota, job timeout/concurrency, `h5p.json` caps, no XML resolver, purge on delete; against the crafted corpus and the largest legitimate package; where extraction runs (job memory/disk, temp cleanup).
- **S7 Malware.** Is an antivirus scan (ClamAV) required before `Ready`? Content-type allow-list contents; are Flash/Java legacy assets rejected?
- **S8 H5P player.** Pinned h5p-standalone vs another embedded player; offline library sets vs "libraries inside the package"; licence check of every shipped asset.
- **S9 Token design. Answered in part (#444 F5):** path token, HMAC, one identical 404. **Open:** the lifetime/refresh/revocation mechanism of 7.5 against real session lengths, audience binding, log scrubbing in the proxy.
- **S10 Where the SCORM API lives (new, QA Must 1; #440 Q-G6).** Design A (shim injected in the launch document, frameset/nested-frame packages rejected) vs Design B (wrapper + SCO on a per-launch origin, `frame-src 'self'` on the wrapper, wildcard TLS); see 7.2. Decided by the real-package corpus and, if B, by the owner.

**For the Lead / owner:**
- **Z1 Zoom plan capacity (open for the owner).** One account: licensed host count, concurrent meeting and participant limits, API rate-limit class. Must be confirmed before Task 4 is sized.
- **Z2 Host model (open for the owner).** Platform host pool (assumed) vs one host user vs teacher accounts; who administers the pool and how a teacher is told no host is free.
- **Z3 Webhooks.** Does the deployment expose a public HTTPS endpoint Zoom can call? If not, polling plus manual recording links only.
- **Z4 Recording policy.** Cloud recording on by default, per unit setting, or manual only; retention and who may view (privacy for minors).
- **Z5 Participant matching.** Match by email/user id only, or require a registrant model (changes the join flow)? Guests are never attached to students automatically.
- **Z6 Data processing for minors (new, QA Should 9).** One vendor account means participant names, join times and recordings of minors from every client sit in one Zoom account. Beyond Z4 (retention): is a data-processing agreement with Zoom in place, may clients opt out of Zoom, may recordings be switched off platform-wide, who answers a parent's request for deletion?
- **Q4 Feature flags.** Disabled flag answer (403 `feature_disabled` vs 404) and whether H5P needs its own flag.
- **Q5 Per-client caps.** Default `Live.MaxCreatesPerDay`, `Packages.MaxClientStorageMb` and package quotas per client.

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
