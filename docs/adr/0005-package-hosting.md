# ADR 0005: Package hosting for SCORM and H5P

Status: **draft, awaiting QA3 sign-off** (2026-11-13; revised after QA3's review of da307f73). Issue #470 (M4b Task 3). Evidence: spike #444 (`docs/spikes/2026-11-12-m4b-package-host.md`, prototype branch `spike/m4b-package-host`), QA3's re-run of the prototype (2026-11-13), crafted-archive corpus #493. Pass/fail rules: proposal section 7.9 (#427). PRD D51, D56, X-05, FR-STD-01/02.

## Context
M4b lets teachers attach SCORM and H5P packages: zips of third-party JavaScript, run by students. The content must not read app cookies or storage, call the API as the user, navigate the app, or reach the network, yet must report completion and score.

## Decision

### 1. Origin
Package content is served from a **separate registrable domain** (PRD D56: no app-origin fallback; no sibling sub-domain fallback). Only dev and CI use `app.localhost` / `content.localhost`. App cookies stay host-only (no `Domain=` attribute) as defence in depth. The content origin comes from configuration, never from request data.

### 2. Package-host response headers (every response)
- `Content-Security-Policy: default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors <app origin>; sandbox allow-scripts`. The header `sandbox` is `allow-scripts` only (proposal 7.4's `allow-forms` is dropped; `form-action 'none'` stays). No package may ever relax `connect-src`, add `allow-same-origin`, or gain any other flag; the only permitted relaxation is the single `'unsafe-eval'` decision of S3.
- `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: private, no-store`; no `X-Frame-Options`; no `Set-Cookie`; no CORP, COEP or COOP.
- CORS exactly `Access-Control-Allow-Origin: *`, no credentials, methods `GET, HEAD, OPTIONS`, no custom request headers allowed.
- Content types are assigned by the server from an extension allow-list. Every file outside the allow-list, and any top-level SVG (`Sec-Fetch-Dest: document`), is served as an attachment with `application/octet-stream`.
- The `sandbox` CSP directive is mandatory (a top-level load otherwise runs at the real origin; spike F1a); asserted by a test.

### 3. App-page headers
Every app page that embeds a package sends `frame-src <package origin>` (spike F1b: it alone stops a package navigating its own frame to an outside URL with data; `<meta http-equiv=refresh>` takes the same path). Asserted by a test on each embedding page. The iframe carries `sandbox="allow-scripts"` only.

### 4. Transport and server enforcement
The app page transfers one `MessageChannel` port after the iframe loads; window messages are ignored; the shim never receives learner identity. The page guards shape, size and rate, and makes the authenticated call itself, but **the server enforces everything the page only guards**:
- key allow-list; unknown keys are dropped;
- caps: message 8 KB, value 4 KB, `cmi` 64 KB per attempt;
- per-attempt rate (`Packages.CommitMinIntervalSeconds`);
- highest `seq` wins per attempt (commits arrive unordered; spike F4);
- `finish` carries the last `seq` and is the flush barrier;
- preview tokens are refused by `commit` (there is no commit route for a preview).

### 5. Tokens
A path token `/p/{token}/...`: payload `{package, student, attempt (when it exists), aud = content host, iat, exp, preview}`, HMAC-signed with a key derived from a purpose string unique to the content host (a token of another purpose is rejected everywhere else), constant-time verified, one identical 404 for forged, expired, malformed, foreign-audience and revoked tokens. Lifetime `Packages.TokenMinutes` (proposal 120, hard cap 8 h); the app page re-launches at 80 % through `POST /api/packages/{id}/launch`, which re-checks access. **Revocation marker** per `(package, student)`, written when the module is hidden, the student is unenrolled or the package is deleted; cached 30 s per node, no per-request DB read; the bound stays 30 s per node. Tokens never appear in logs (`/p/*` is redacted; a test asserts it) and `Referrer-Policy: no-referrer` stays on.

### 6. Preview
`POST /api/packages/{id}/preview`: staff only (`Packages.View` plus the class-course relationship), 404 on a miss, 409 `package_not_ready` when validation has not finished. Returns `{ launchUrl, serverNowUtc }` with a preview-flagged token. A preview has no attempt, so it records no attempt, completion or grade event, and there is no preview commit route: the commit endpoint refuses preview tokens, and the player never commits in preview (a test proves it). A student can never obtain a preview token.

### 7. Storage (X-05)
Extracted packages live in a per-client bucket or prefix that includes the OrgUnit; the object key is built from the token's package id only, and every requested path is validated against the package's file index. The content host streams the object after the token check; there are no pre-signed URLs.

### 8. Archive caps (S6)
Decided now so T8a and corpus #493 agree: at most 10 000 entries; at most 1 GB uncompressed (`Packages.MaxUncompressedMb`); per-entry compression ratio at most 100:1 and total at most 50:1; directory depth at most 20; entry name at most 255; a manifest size cap; `h5p.json`, `content.json` and `library.json` at most 1 MB with JSON `MaxDepth` 32; streamed extraction counting real bytes (never trusting headers); a per-client quota (`Packages.MaxClientStorageMb`); a time, memory and disk budget for the validation job. S6 stays open until T8a proves every crafted zip.

### 9. H5P player (S8)
Pin and vendor `h5p-standalone` at an exact version; the version and licence of every shipped asset are recorded before T11 merges. H5P libraries come only from inside the package (validated like any entry) and are never fetched remotely (`connect-src 'self'`). H5P uses the same CSP and sandbox as SCORM.

### 10. SCORM scope and failures
Design A (shim injected into the launch document, single-SCO scope) is the v1 design; Design B (per-launch sub-domain) is the fallback only if the corpus shows nested-frame packages are in scope (S10). A package that fails the strict CSP, needs nested frames or is a nested archive is rejected with `package_invalid` and the contract reason codes (`needs_relaxed_policy`, `needs_nested_frames`, `nested_archive`).

## Results per question
Pass with evidence, or **open**; an open question blocks only the tasks it names. Dates are evidence dates.

| Q | Result | Evidence | Blocks while open |
|---|---|---|---|
| S1 origin layout | **pass on loopback**; real-environment assertion (cross-site, cookie-free) is infrastructure work and **open** | Spike F2 (2026-11-12) | T9a deploy |
| S2 sandbox and probes | **pass for the hostile-page probes**; **open for the 5 real packages** | Spike 17 probes (2026-11-12). QA3 re-run (2026-11-13) with `connect-src 'self'` and `Access-Control-Allow-Origin: *` plus 10 extra probes: own-host `fetch`/XHR work; credentialed fetch refused (wildcard CORS); `fetch` to the outside (cors and no-cors), to the app origin `/secret`, WebSocket, EventSource, `sendBeacon`, `<link rel=prefetch>` and an outside script produced 0 requests to the outside in every variant; the 17 probes, top-level sandbox and `frame-src` results unchanged | T9b, T10 (real packages) |
| S3 `'unsafe-eval'` count | **open** | strict CSP blocks `eval` (spike); count needs the real corpus | T9b may not allow `'unsafe-eval'` before this closes |
| S4 transport | **pass** (prototype) | Spike F3/F4 (2026-11-12); server enforcement specified in section 4 | none |
| S5 single-SCO scope | **open** | needs the real corpus and the multi-SCO rejection reason | T10 scope final |
| S6 crafted archives | caps **decided** (section 8); acceptance **open until T8a** | corpus #493 (123 zips, approved at 8fbe2d2b) is the evidence base | T8a, T8b merge |
| S7 antivirus | **open, owner call** (recommendation: ClamAV as a pluggable scan step) | none yet | T8b |
| S8 h5p-standalone | **decided** (section 9); versions and licences recorded before T11 merges | none yet | T11 merge |
| S9 tokens | **decided** (section 5); forged/expired/malformed refused in the prototype (2026-11-12); session token, revocation and long-session re-run **open** | prototype to be re-run with the new token | T9a merge |
| S10 Design A vs B | Design A **assumed**, decision **open** | needs the nested-frame count and the prelude run on the 3 real SCORM files (multi-page navigation, BOM, `<meta charset>`/UTF-16, missing `<head>`, own `window.API`, caught parent-walk `SecurityError`) | T10 scope final |

## Consequences
- With this ADR approved, T9a, T9b, T10, T11 and T17 can start building the decided design. The open rows above are acceptance evidence that must close before the task named in the last column merges. The real-package rows need the 3 SCORM and 2 H5P files (`docs/spikes/corpus/README.md`), which are third-party downloads that need the owner's approval.
- A change to the hosting design after sign-off needs a new QA review of the changed part.
