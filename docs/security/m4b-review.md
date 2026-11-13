# M4b security review checklist: package host, archives, tokens, player, webhook

For QA3 (M4b Task 21, issue #490). Derived from ADR 0005 (`docs/adr/0005-package-hosting.md`, PR #528), the spike (`docs/spikes/2026-11-12-m4b-package-host.md`), the plan decisions 1-17 (`docs/superpowers/plans/2026-11-16-m4b-live-and-packages.md`) and the Zoom webhook rules of Task 7. Written by BE2 as preparation; QA3 owns the review and may change any row. Findings are filed as `bug` + `sev:*` against the owning lane.

How to use: every row names **what to check**, **how** (an automated test to write or run, or a manual probe) and the **expected result**. Tick a row only with evidence (test name, request/response, or screenshot) in the PR or issue. A row marked **blocked by** cannot pass until that ADR question or task closes.

Conventions: `APP` = the app origin, `CONTENT` = the content origin (`PackageHost:Origin`). In dev and CI the pair is `http://localhost:<app>` and `http://127.0.0.1:<content>` (cross-site). "One identical 404" means the same status, headers and body shape for every refusal reason.

## 0. Preconditions

| # | Check | How | Expected |
|---|---|---|---|
| 0.1 | The review runs against two **different registrable domains**, not two ports of one host | Inspect the E2E stack config; `localhost` and `127.0.0.1` count as cross-site | Cookies set by APP are never sent to CONTENT (verify in the network log) |
| 0.2 | Open ADR questions are listed with their status at review time | Read the ADR table | S3 (`unsafe-eval`), S5 (single-SCO scope), S7 (antivirus), S10 (Design A vs B), S1 real-environment: each is passed, or the dependent rows below are marked blocked, never silently skipped |
| 0.3 | The real-package corpus (3 SCORM, 2 H5P) exists, or the rows that need it are marked blocked | `docs/spikes/corpus/README.md` | Table filled in (name, source, licence, SHA-256, frameset, needs eval); downloads were approved by the owner |

## 1. Origin and cookies (ADR 1, plan decision 10, D56)

| # | Check | How | Expected |
|---|---|---|---|
| 1.1 | **Fail closed**: `PackageHost:Origin` missing, equal to APP, or the same registrable domain as APP (including a sub-domain) | Integration test per configuration | Upload, attach, launch and preview answer 409 `package_host_not_configured`; the content-host pipeline is not mapped (CONTENT paths 404 on APP); enabling `ScormContent` for a unit is refused with the same code |
| 1.2 | No app-origin fallback exists anywhere | Grep the code and the OpenAPI/route list for a package route on APP serving content | None |
| 1.3 | App cookies are host-only | Assert every `Set-Cookie` from APP has no `Domain=` | Pass; a test fails if `Domain=` appears |
| 1.4 | CONTENT never sets a cookie | Request every content response type (see 2) | No `Set-Cookie` header, ever |
| 1.5 | The content origin comes from configuration, never from request data | Send `Host`/`X-Forwarded-Host` pointing at APP or CONTENT on both pipelines | Pipelines still select by the configured origin; no response reflects the supplied host |
| 1.6 | The content pipeline answers only on CONTENT and the app pipeline only on APP | Request each route on the wrong origin | 404, no content leak |

## 2. Content-host response headers (ADR 2, plan decision 12)

Check **every response type**: launch HTML, a script, a stylesheet, an image, JSON, a font, a media file, an unknown extension, a top-level SVG, a 404 (every refusal reason), a 5xx / exception page, an OPTIONS preflight, a HEAD, a range request, a redirect (there should be none).

| # | Check | How | Expected |
|---|---|---|---|
| 2.1 | CSP present and exact on every response, errors included | A header test over the response matrix | `default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors <APP>; sandbox allow-scripts` |
| 2.2 | The `sandbox` directive is `allow-scripts` and nothing else: no `allow-same-origin`, `allow-forms`, `allow-popups`, `allow-top-navigation` | Parse the directive; mutation check: the test fails if any other flag is added | Pass |
| 2.3 | `'unsafe-eval'` absent unless ADR S3 signed it (blocked by S3) | Parse `script-src` | Absent, or present only on the content host with the recorded decision |
| 2.4 | Other headers on every response | Header test | `X-Content-Type-Options: nosniff`; `Referrer-Policy: no-referrer`; `Cache-Control: private, no-store`; no `X-Frame-Options`; no CORP, COEP, COOP; no `Set-Cookie` |
| 2.5 | CORS exactly as specified | Preflight and simple GET | `Access-Control-Allow-Origin: *`, no `Allow-Credentials`, methods `GET, HEAD, OPTIONS` only, no custom request headers allowed |
| 2.6 | A credentialed cross-origin fetch is refused | Browser probe (E2E): `fetch(CONTENT, {credentials:"include"})` from APP | Blocked (wildcard CORS) |
| 2.7 | Content types come from the extension allow-list; everything else is an attachment | Request a file of each kind and an unknown extension | Allow-listed: the mapped type; unknown: `application/octet-stream` with `Content-Disposition: attachment` |
| 2.8 | A top-level SVG never renders | Navigate to an SVG (`Sec-Fetch-Dest: document`) | Served as an attachment; the same SVG as an `<img>` inside a package works |
| 2.9 | Top-level navigation to a launch page is sandboxed | Open a package URL in a new tab (CSP `sandbox` header applies) | The page runs in an opaque origin: `document.cookie` empty, no access to APP storage |
| 2.10 | Error pages carry the same headers and leak nothing | Force 404, 413, 500 | Same CSP and headers; no stack trace, no token, no internal path |

## 3. App-page headers and the iframe (ADR 3)

| # | Check | How | Expected |
|---|---|---|---|
| 3.1 | **Every** app route that can embed a package sends `frame-src <CONTENT>` | Enumerate routes that render the player (student viewer, teacher preview, module viewer, course home); header test on each; add a route list guard so a new embedding page fails the build without it | Present on all; absent on routes that never embed |
| 3.2 | The iframe attribute is exactly `sandbox="allow-scripts"` and `allow=""` | Read the rendered DOM; mutation check: test fails if `allow-same-origin` is added | Pass |
| 3.3 | APP keeps `X-Frame-Options: DENY` / `frame-ancestors 'none'` | Header test | Unchanged; only CONTENT sets `frame-ancestors <APP>` |
| 3.4 | The frame cannot read APP storage, cookies or DOM | E2E probe from inside the frame | `localStorage`/`sessionStorage`/`document.cookie` of APP unreachable; `parent.document` throws `SecurityError` |
| 3.5 | Own-frame navigation to the outside is blocked: `location.href`, `<meta http-equiv=refresh>`, `window.open`, form submit, link click | E2E probe page | Blocked by `frame-src` / `form-action` / sandbox; 0 requests leave |
| 3.6 | The packages cannot reach the network | E2E probes: `fetch`/XHR to outside and to APP `/secret`, WebSocket, EventSource, `sendBeacon`, `<link rel=prefetch>`, outside `<script>`, `<img>` to outside | 0 requests to the outside in every variant; own-origin requests work |
| 3.7 | A forged window message is ignored | Frame posts messages with `origin "null"` pretending to be APP or claiming a token | Ignored; only the `MessageChannel` port is trusted |
| 3.8 | Re-launch at 80 % of the token life re-checks access | Hide the module, wait or shorten `Packages.TokenMinutes` | Re-launch returns 404, the player shows the unavailable state |

## 4. Tokens (ADR 5, plan decision 13)

| # | Check | How | Expected |
|---|---|---|---|
| 4.1 | Forged, tampered (each byte), truncated, empty, oversize, wrong-purpose, expired, foreign-audience and revoked tokens | `PackageTokenAttackTests`: table-driven | **One identical 404** each: same status, headers, body length class, comparable timing |
| 4.2 | A token signed for another purpose (for example an antiforgery or other Data Protection token) is refused on CONTENT, and a content token is refused everywhere else | Cross-purpose test | Refused both ways |
| 4.3 | Constant-time compare | Code review of the verify path | `CryptographicOperations.FixedTimeEquals` or equivalent, no early return on length of the secret part |
| 4.4 | Lifetime is `Packages.TokenMinutes` (default 120) with the hard cap | Set 9 hours and 0 | Capped at 8 hours; 0 or negative rejected |
| 4.5 | Revocation reaches CONTENT within 30 s per node: module hidden, student unenrolled, package deleted | Integration test with the clock; second node simulated | Refused after the window; no per-request DB read (assert query count) |
| 4.6 | A token cannot be used for another package, another student or another attempt | Swap ids in the path/token | Same 404 |
| 4.7 | **Tokens never appear in logs**: `/p/*` is redacted in request logs, exception pages and diagnostics; `Referrer-Policy: no-referrer` | Log-capture assertion across a 200, a 404 and a thrown exception | No token substring in any log line or response header |
| 4.8 | The token is never in a DTO list or an API response other than launch/preview | Grep the DTOs; test the list endpoints | None |
| 4.9 | A student can never obtain a preview token; preview tokens are refused by `commit` | Student calls preview; staff preview token used on commit | 403/404; commit refused |

## 5. Archives (ADR 8, S6; crafted corpus #493, task T8a/T8b)

Run the **whole** crafted corpus through the real upload route (not only the unit-level extractor). Each zip must end in a stable `package_invalid` (or the contract reason code) with nothing extracted outside the package prefix and no resource blow-up.

| # | Check | Expected |
|---|---|---|
| 5.1 | zip-slip (`..`, absolute path, backslash, drive letter, NUL) | Rejected; no file outside the package prefix (assert the object store listing) |
| 5.2 | Bombs: per-entry ratio > 100:1, total ratio > 50:1, > `Packages.MaxUncompressedMb`, nested archive | Rejected after counting **real bytes** (a lying header does not help); memory and disk stay within the validation budget (measure peak) |
| 5.3 | Entry-count > 10 000, depth > 20, name > 255, duplicate after case/NFC fold, device names (`CON`, `NUL`), trailing dot/space | Rejected |
| 5.4 | Symlink / hardlink entries, encrypted entries, Zip64, CP437 vs UTF-8 names, polyglot (zip + HTML), data descriptors that lie | Rejected or normalised exactly as the ADR says; never executed or served raw |
| 5.5 | Legacy/executable assets (Flash, Java, `.exe`, scripts outside the allow-list) | Rejected by the extension allow-list |
| 5.6 | XML: XXE, billion laughs, `xml:base` / `schemaLocation` to a local file and to a URL, DTD | Parsed with DTD processing prohibited and no resolver; no outbound request, no file read (assert with a canary) |
| 5.7 | JSON: `h5p.json`, `content.json`, `library.json` > 1 MB, depth > 32, duplicate keys, huge numbers | Rejected within the cap |
| 5.8 | The validation job obeys its time, memory and disk budgets; 2 jobs per server, 1 per client | Concurrency test; a job over budget fails cleanly and frees its temp files |
| 5.9 | Per-client storage quota (`Packages.MaxClientStorageMb`) | Upload over quota: refused with the documented code; nothing partial remains |
| 5.10 | Upload size uses `Files.MaxUploadMb` and is streamed (not buffered whole) | Upload a large archive; watch memory; related fix is PR #538 (`OpenReadAsync` streams from a temp file) |
| 5.11 | `IPackageScanner` runs before `Ready` (blocked by S7: antivirus decision) | A scanner that rejects marks the package invalid and nothing is servable |
| 5.12 | Rejection reasons are the contract codes (`needs_relaxed_policy`, `needs_nested_frames`, `nested_archive`) and never echo file contents | Assert the problem body |

## 6. Storage and serving (ADR 7, X-05)

| # | Check | How | Expected |
|---|---|---|---|
| 6.1 | The object key is built from the token package id only; the requested path is validated against the package file index | Path probes: `..`, `%2e%2e`, double-encoding, backslash, absolute, NUL, `//`, trailing dot, case variants | Same 404; never a file outside the package |
| 6.2 | Per-client bucket/prefix includes the OrgUnit; no pre-signed URLs are ever returned | Inspect launch/preview responses and logs | None |
| 6.3 | Cross-tenant: client B (and a sibling unit) cannot launch, preview, attach or read the validation status of client A's package; a Teacher of another class course gets 404 | Isolation test per new table (`Package`, attempt and tracking tables) and probe rows for every route | 404 (unit/tenant) or 403 (permission) exactly as the route table says |
| 6.4 | Students read packages only through `IModuleAccess` (visibility window, group target, hidden lesson/section) | Probe a hidden, scheduled-future and other-group module | 404 |
| 6.5 | Range requests and `HEAD` obey the same token and path rules | Request ranges on media | Same checks, no bypass |

## 7. Tracking transport and server enforcement (ADR 4, plan decision 14)

The page guards shape, size and rate; the **server** must enforce all of it, so test the API directly (bypassing the page).

| # | Check | Expected |
|---|---|---|
| 7.1 | Key allow-list; unknown keys dropped (not stored, not echoed) | Pass |
| 7.2 | Caps: message 8 KB, value 4 KB, `cmi` 64 KB per attempt | 413/400 with a stable code; nothing stored |
| 7.3 | Per-attempt rate `Packages.CommitMinIntervalSeconds` | 429 / dropped without data loss of the last `seq` |
| 7.4 | Highest `seq` wins; commits can arrive unordered; `finish` carries the last `seq` and flushes | Replay out of order: final state equals the highest `seq` |
| 7.5 | A student can only commit to their own attempt; a guardian, another student, another client cannot | 404/403 |
| 7.6 | Preview commits answer 204 and record nothing (no attempt, completion, grade event) | Assert tables unchanged |
| 7.7 | No learner name or id reaches the frame: `cmi.core.student_name` / `student_id` reads return the opaque constant | E2E read from inside the frame |
| 7.8 | Completion and score flow only through `ICompletionRecorder` and `IGradeEvents`; a client-supplied score above max or negative is clamped or rejected | Pass |

## 8. H5P specifics (ADR 9, task T11)

| # | Check | Expected |
|---|---|---|
| 8.1 | `h5p-standalone` is vendored at an **exact** version, served from CONTENT, never from a CDN; versions and licences recorded before T11 merges | Asset list and licence file present; no external URL in any served page |
| 8.2 | H5P libraries load only from inside the package and are validated like any entry; nothing is fetched remotely | Probe a package with a remote library reference: 0 outside requests, rejected or ignored |
| 8.3 | Same CSP and sandbox as SCORM | Header test over an H5P launch |

## 9. SCORM API prelude (plan decision 11, Design A)

| # | Check | Expected |
|---|---|---|
| 9.1 | The prelude is injected as the first element of `<head>` of **every** HTML response of a SCORM package, including multi-page SCOs, BOM, `<meta charset>`/UTF-16, missing `<head>` | Probe each case (blocked by S10 for the real SCORM files) |
| 9.2 | The per-request inline JSON block is token-bound, `no-store`, contains only the attempt's own data model and escapes `</script>` and `<!--` | Inject a hostile `suspend_data`; the HTML stays well formed and inert |
| 9.3 | A package that defines its own `window.API`, walks `parent` and catches the `SecurityError`, or uses `localStorage`/cookies keeps working with the in-memory stand-ins | Real corpus (blocked by S10) |
| 9.4 | Framesets and nested-frame SCOs are rejected (`needs_nested_frames`), and the host sends `frame-src 'none'` | Pass |

## 10. Zoom webhook (Task 7, D54, plan decision 3) and provider (Task 5, D49, D53)

| # | Check | Expected |
|---|---|---|
| 10.1 | The webhook is the **only anonymous M4b route** and is in the probe matrix | Route coverage test lists it |
| 10.2 | Order of checks: body cap 256 KB (413) before anything else; then the URL-validation challenge (itself signed); then signature over the **raw bytes**, constant time (401, no hint); then the 5-minute timestamp window (401) | Test each step in order; a reformatted body must not verify |
| 10.3 | Only failed verifications count toward the per-IP limit (429 after the threshold, no database access); a valid burst or a Zoom retry is never 429 | `Valid_burst_is_never_429`, `Repeated_failed_verifications_from_one_ip_are_429_without_db` |
| 10.4 | Duplicate event id is a 200 no-op **only after** signature and timestamp pass; processing and the receipt are in **one transaction**; a failure answers 5xx and nothing is lost | `Replayed_webhook_is_processed_once`, `Failure_after_receipt_does_not_lose_the_event` |
| 10.5 | A webhook for client A's meeting cannot touch client B's rows; the meeting lookup is the only bypass site and is allow-listed; an unknown meeting id is a 200 no-op | `Webhook_for_client_a_meeting_cannot_touch_client_b_rows` |
| 10.6 | `ZoomWebhookReceipt` holds no client data (event id, time) and is purged after 30 days | `Receipt_holds_no_client_data`, `Receipts_older_than_30_days_are_purged` |
| 10.7 | Participant matching: only an authenticated Zoom user with a verified email equal to an enrolled student is `Matched`; a guest who types a student email stays `Guest` | `Guest_typing_a_student_email_stays_guest` |
| 10.8 | The dev events endpoint is mapped only in Development | Not mapped in Production/Test hosts |
| 10.9 | Secrets (`Zoom:*`, webhook token) never appear in logs, DTOs, audit, errors or the repo; provider status reveals only `{configured, healthy}` | Log-capture test; grep the repo and responses |
| 10.10 | Zoom meeting settings are fixed by the platform: waiting room on, join before host off, topic = title only, tracking ids only (`client`, `classCourse`), cloud recording only when `Live.CloudRecordingEnabled` is on at the unit | Inspect the create body in tests |
| 10.11 | Host pool: never double-booked under concurrency; per-client share enforced; exhausted pool is 409 `provider_unavailable` with `Retry-After`; the allocator reads only slot columns across clients | `Host_pool_never_double_books_a_slot_under_concurrency`, `Allocator_reads_only_slot_columns_and_returns_no_foreign_rows` |
| 10.12 | Join URLs and start URLs never appear in lists, logs or audit; a student never receives the start URL | `Lists_never_contain_join_or_start_urls`, `Student_never_receives_the_start_url` |

## 11. Cross-cutting

| # | Check | Expected |
|---|---|---|
| 11.1 | Every new route has a probe row and `RequirePermission` (or `IAccessEvaluator`); no new route relies on the UI hiding it | `Every_M1_api_route_is_in_the_probe_matrix` and the M4 matrices green |
| 11.2 | Every new table has an isolation test; `IgnoreQueryFilters` appears only at the allow-listed sites with `// ISOLATION-BYPASS:` | `IsolationBypassGuardTests` green; the M4b entries are exactly: allocator slot read (T5), meeting-id lookup and `ZoomWebhookReceipt` (T7), organization list of the recurring Zoom jobs |
| 11.3 | Feature flags: `VirtualClasses` and `ScormContent` off means 403 `feature_disabled` for writes, join and launch; reads of existing items are not gated | Test both states |
| 11.4 | Audit and notifications carry no URLs, tokens or secrets | Inspect the entries produced by a launch, a join and a webhook |
| 11.5 | ar/en parity and RTL for the player, status and error states | Resource parity test; RTL screenshot |
| 11.6 | The E2E sandbox suite runs in a real browser with two origins and includes the **mutation check** that fails if `allow-same-origin` is added | `PackageSandboxTests` |

## 12. Sign-off

- [ ] Every row is passed with evidence, or explicitly blocked with the ADR question or task that blocks it and an owner.
- [ ] Open questions S1 (real environment), S3, S5, S7, S10 are recorded with their decision.
- [ ] Findings filed (`bug` + `sev:*`, lane label) and linked here.
- [ ] `docs/security/m4b-review.md` (this file) updated with the final results and the commit it was verified at.
