# ADR 0005: Package hosting for SCORM and H5P

Status: **draft, awaiting QA3 sign-off** (2026-11-13). Issue #470 (M4b Task 3). Evidence: spike #444 (`docs/spikes/2026-11-12-m4b-package-host.md`, prototype branch `spike/m4b-package-host`). Pass/fail rules: proposal section 7.9 (#427).

## Context
M4b lets teachers attach SCORM and H5P packages: zips of third-party JavaScript, run by students. The content must not read app cookies or storage, call the API as the user, navigate the app, or reach the network, yet must report completion and score (PRD D51, D56, X-05, FR-STD-01/02).

## Decision
1. **Separate cookie-less host** for package content (different site where the owner can, else a sibling sub-domain with host-only app cookies, no `Domain=` attribute). Dev and CI use `app.localhost` / `content.localhost`.
2. **Package-host response headers, on every response:** `Content-Security-Policy: default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors <app origin>; sandbox allow-scripts`, plus `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: private, no-store`, no `X-Frame-Options`, no `Set-Cookie`, server-assigned content types from an extension allow-list. `connect-src 'self'` (with CORS `*` on the package host) replaces the spike's `'none'` so H5P content can load its own JSON and media; it never grants network access beyond the package host, and **no package may relax it** (S3). The prototype re-run with this policy is still to do (S2).
3. **The `sandbox allow-scripts` CSP directive is mandatory** (a top-level load of the package URL otherwise runs at the real origin; spike F1a). Tested by asserting the header.
4. **Every app page that embeds a package sends `frame-src <package origin>`** (spike F1b: it is the only thing that stops a package navigating its own frame to an outside URL with data). Tested by asserting the header on each embedding page.
5. **iframe `sandbox="allow-scripts"` only.** `allow-same-origin`, `allow-forms`, `allow-popups`, `allow-top-navigation`, `allow-modals` stay off.
6. **Transport:** the app page transfers one `MessageChannel` port after the iframe loads; window messages are ignored; the app page validates shape, size, a key allow-list and rate, and makes the authenticated call itself. Commits carry a client sequence number (`seq`); the server resolves by `seq`; `LMSFinish` is a flush barrier. The shim never receives learner identity.
7. **Access tokens:** a path token `/p/{token}/...` bound to package + student, HMAC-signed, constant-time verified, one identical 404 for forged/expired/malformed. A **session token with a revocation marker and a re-launch endpoint** replaces the spike's fixed 10-minute token, so sessions longer than 10 minutes keep working and access removal takes effect within the revocation bound (30 s per node; the cache is per instance). `Referrer-Policy: no-referrer` and log redaction of `/p/*` are required.
8. **Design A (shim injected into the launch document, single-SCO scope)** is the v1 design; Design B (per-launch sub-domain) is the fallback only if the corpus shows frameset or nested-frame packages are in scope (S10). Packages that need nested frames, a relaxed policy, or nested archives are rejected with `package_invalid` and the reason codes of the contract.
9. **Policy for failures:** a package that fails the strict CSP is rejected (`needs_relaxed_policy`). We never relax `connect-src`, never add `allow-same-origin`, and the only permitted relaxation is the single `'unsafe-eval'` decision of S3.

## Results per question
Each is **pass** with evidence, or **open**; an open question blocks only the tasks it names. Dates are evidence dates.

| Q | Result | Evidence | Blocks while open |
|---|---|---|---|
| S1 origin layout | **pass on loopback, open for real environments** | Spike F2, probe on `app.localhost` / `content.localhost` (2026-11-12). The cross-site and cookie-free assertion in staging/CI is infrastructure work | T9a (host), infra task |
| S2 sandbox + 17 probes + opaque-origin probes a–d + `<meta http-equiv=refresh>` | **partial**: probes pass (spike, 2026-11-12); refresh probe, re-run with `connect-src 'self'` and real packages **open** | Spike results table | T9a, T9b, T10 |
| S3 `'unsafe-eval'` count | **open** | strict CSP blocks `eval` (spike); the count needs the real corpus | T9b |
| S4 transport | **pass** (prototype) | Spike F3/F4: forged messages ignored, out-of-order commits, key/size/rate limits (2026-11-12). Real SCORM/H5P payloads untested | none |
| S5 single-SCO scope | **open** | needs the corpus and the multi-SCO rejection reason | T10 |
| S6 crafted archives | **open** | crafted zips are built by BE3 under `tests/Lms.IntegrationTests/Packages/Corpus/` (Task 8a reuses them) | T8a, T8b |
| S7 antivirus | **open, owner call** | ClamAV as a pluggable scan step is the recommendation | T8b |
| S8 h5p-standalone | **open** | vendored version and licence of every shipped asset not yet recorded | T11 |
| S9 tokens | **pass in part**: forged/expired/malformed refused (spike F5); session token, revocation marker and > 10 min re-launch **open** | prototype to be re-run with the session-token design | T9a |
| S10 Design A vs B | **open**, Design A assumed | needs the frameset/nested-frame count and the prelude run on the 3 real SCORM files (multi-page nav, BOM, `<meta charset>`/UTF-16, missing `<head>`, own `window.API`, caught parent-walk `SecurityError`) | T9b, T10 |

## Consequences
- T9a, T9b, T10, T11 and T17 are gated on this ADR carrying QA3's approving review and `qa:approved`. The open rows above name which of them stay blocked until their evidence lands.
- A change to the hosting design after sign-off needs a new QA review of the changed part.
- The corpus is described in `docs/spikes/corpus/README.md`.
