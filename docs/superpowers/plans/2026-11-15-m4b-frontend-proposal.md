# M4b frontend proposal: Zoom live sessions, SCORM, H5P

> **Status: DRAFT proposal for the Team Lead (no code).** Author: FE2. Frontend half of M4b, paired with BE2's backend proposal (`2026-11-15-m4b-backend-proposal.md`, draft PR #427) and written in the M4a frontend proposal format (`2026-11-14-m4-frontend-proposal.md`). Task numbers (`G1`...`G7`) are local; the Lead renumbers them. Owner decisions applied: **D49** (one platform Zoom app: no credentials or per-client provider screen), **D50** (no LTI), **D51** (SCORM/H5P run in a sandboxed iframe on a separate origin with a strict CSP, after the security spike).
>
> **Sources read:** BE2's backend proposal (sections 3 to 9), PRD FR-VC-01, FR-STD-01, FR-STD-02, FR-CNT-01, X-01, X-05, X-10, D49 to D51, the M4a frontend proposal (F13, F14, F15, F16), and the merged / open M4a client code this builds on (see section 1).

## 0. Scope and shape

M4b adds two module kinds that already exist in the contract catalog (`ModuleKinds.LiveSession`, `Scorm`, `H5p`) and that the M4a module viewer currently answers with "this kind of module opens in its own page":

1. **Live sessions** (Zoom): a teacher schedules a single or recurring session from a lesson; students see it as a module, join inside a window, get a reminder; the teacher attaches or receives the recording link; participants are visible to staff.
2. **SCORM / H5P packages:** a teacher uploads a package, sees it validated, attaches it to a lesson; a student launches it in a **sandboxed iframe on a separate origin** and the app records completion and score through a `postMessage` bridge.

What the frontend deliberately does **not** do: any Zoom credential or provider screen (D49); embed Zoom in the page (the student joins in a new tab on a platform link); attendance screens (M5 owns attendance, M4b stops at the participants list); a teacher view of every student's package attempts (M5 reports); a package editor or H5P authoring; multi-SCO / sequencing navigation (BE rejects it in v1, Q-S5).

### Design and conventions (apply to every task, same as M4a)
- `docs/DESIGN.md` and the lms-ui skill: `PageHeader`, `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, one primary action per page, labelled-card tables at 360 px, logical CSS, Rounded icons, mirrored direction icons, ISO dates (`yyyy-MM-dd HH:mm`, left to right).
- ar and en resource entries for every key (parity test), bUnit tests first, a `Page_renders_rtl_in_arabic` per page, screenshots in `docs/screenshots/pr-<name>/` at 1440 and 360 px in ar and en (light and dark), 0 warnings, files under about 300 lines.
- API clients follow the M3/M4a pattern: `I<Module>Api` + HTTP implementation using `Routes.*` and `ProblemCodes.*` from `Lms.Shared` + an in-memory fake registered only when `Lms:UseFakeApi=true`. The fakes mimic the server rules the UI depends on (as `FakeModulesApi` and `FakeQuizAttemptsApi` do).
- Server time: every timed comparison uses `serverNowUtc` from the response; the browser clock only measures elapsed time since the page loaded (the M4a countdown pattern).
- Resource key prefixes: `Live_*`, `Package_*` (SCORM and H5P), `Error_<code>` for each new problem code.

## 1. What M4b builds on (verified on the open M4a PRs and main, 2026-11-12)

| Seam | Where | M4b use |
|---|---|---|
| Module authoring | `AddModuleMenu`, `ModuleDialog`, `ModuleMetadataForm`, `ModuleOrderControls` (#389) | New menu entries "Live session", "SCORM package", "H5P package" gated by permission and feature flag; the metadata form (titles, visibility, target, completion rule) is reused with `CompletionRules.ForActivity` for the new kinds. |
| Module viewer | `ModuleViewerPage` + `ModuleBody` (#394) | `ModuleBody` gets two new branches: `LiveSessionPanel` and `PackagePlayer`. The "Mark complete / Done" and previous / next logic stay in the page. |
| Assets and uploads | `AssetUploader`, `IAssetsApi` (#387); `ApiHttp.UploadAsync` sends owner fields before the file | Package upload reuses the multipart transport and the progress / cancel / retry behaviour, but posts to `/api/class-courses/{id}/packages` and expects `202` + a status to poll. |
| Course home and Home | `MyCoursePage`, `ContinueLearningCards`, `HomeActivityCards` (#390, #392, #396, #439) | "Upcoming live sessions" row on the course home and a Home card. |
| Notifications | `NotificationText`, `NotificationLabelParityTests` | `Live.Reminder` labels in ar and en; the parity test fails until they exist. |
| Rich text and sanitising | `RichTextEditor`, `SanitizedHtml` | Not used by M4b (titles are plain text, recording links are http/https URLs checked with `ModuleLinks.IsAllowed`). |
| Dialog a11y | `CloseOnEscapeKey` + `js/dialog-focus.js` (#418) | Applies to the new dialogs. |

## 2. Proposed permissions and flags (finalised by the Lead's wave 3 contract PR, BE2 section 5)

| Group | Constants | Frontend use |
|---|---|---|
| `LiveSessions` | `View`, `Manage` | `View`: sessions list, join panel. `Manage`: schedule, edit, cancel, recording link, participants. |
| `Packages` | `Upload`, `View` | `Upload`: upload, attach, delete. `View`: launch a package (students) and read status. |
| Existing | `Modules.Manage` | Creating the module under a lesson still needs it (the dialogs call the module APIs). `Files.Download` is **not** needed for packages. |

Relationship rules are server-side (Teacher: own class courses; Student: own enrollments). The UI hides entries the caller lacks the permission for and never relies on that.

Feature flags `VirtualClasses` and `ScormContent` gate writes and join/launch on the server; the answer is `403 feature_disabled` (Q4). The UI cannot read flags without `Settings.View`, so it does **not** pre-hide by flag: it shows the normal entry and maps `feature_disabled` to a plain message ("This feature is not enabled for your school"). If the Lead wants the entries hidden when the flag is off, the contract needs a capabilities read for non-admins (Q-G1).

## 3. Task list

Each task = one issue = one PR. Sizes: S up to 1 day, M about 2 days, L 3 days or more for one FE agent. "BE" names the backend task the UI waits for (or fakes).

### G1: Live session client, fake and the module kind plumbing (S)
**PRD:** FR-VC-01. **Depends:** wave 3 contract (BE2 Task 1), BE Task 3 (or fake).
`ILiveSessionsApi` (list, create single / recurring, get, update with `scope`, delete, join, recording, participants, provider-status) with HTTP implementation and `FakeLiveSessionsApi` (windowed join with the server clock, overlap / series rules, `SyncFailed`, `provider_unavailable`). `ModuleKindIcons` and `Module_Kind_*` labels for the three kinds so plan trees, the course home and the viewer show them. `ModuleBody` stops saying "unsupported" for these kinds (it shows the panels from G3 / G5, or a neutral placeholder until they land).
**Tests:** `Fake_enforces_the_join_window_with_the_server_clock`, `Fake_recurring_creates_one_session_per_occurrence_max_52`, `Fake_series_edit_scope_this_vs_following`, `Module_kinds_have_labels_and_icons_in_both_languages`.
**Keys:** `Live_*`, `Module_Kind_*`.

### G2: Live session authoring: schedule, list, edit, cancel, recording (L)
**PRD:** FR-VC-01. **Depends:** G1, M4a F3 (authoring shell, #389).
- Route `/class-courses/{Id}/live-sessions` (tab next to Plan, Forum, Announcements in `ClassCourseTabs`), `PageHeader` with one primary action "Schedule a session" (`LiveSessions.Manage`).
- `LiveSessionDialog`: title ar / en, lesson picker (lessons of the plan), start date-time and duration, **single or recurring** (weekly, interval, until date or count, max 52, with a live preview of the generated dates), module settings through the shared `ModuleMetadataForm` (visibility, target class or group, completion rule from `CompletionRules.ForActivity`).
- `LiveSessionsList` (`ServerTable`, upcoming and past): date-time (ISO, left to right), title, status chip (Scheduled, Live, Ended, Cancelled, **SyncFailed** with the explanation "Zoom could not be reached; it will retry"), recording indicator. Row actions: edit, cancel, recording link, participants (icon buttons with `aria-label`). **Series edit** opens `SeriesScopeDialog` (This session / This and following) before saving or deleting.
- `RecordingLinkDialog`: http / https only (`ModuleLinks.IsAllowed`), shows who attached it (Provider / Manual).
- `ProviderStatusBanner`: from `GET provider-status`; `configured = false` shows an info alert "Live sessions are not set up yet. Ask your platform administrator." without any link to Settings (D49); `healthy = false` shows a warning that creating sessions may fail.
- Error mapping: `provider_unavailable` (503, `Retry-After` shown in plain words), `feature_disabled`, `module_visibility_range_invalid` and `module_target_invalid` on the right fields, closed class / year messages (existing codes).
**Tests:** `Teacher_schedules_single_session_under_own_lesson`, `Recurring_requires_end_date_or_count_and_previews_dates`, `Recurring_is_limited_to_52_occurrences`, `Series_edit_asks_for_the_scope`, `SyncFailed_session_shows_the_explanation`, `Recording_link_rejects_javascript_scheme`, `Provider_not_configured_shows_the_platform_admin_hint_without_a_settings_link`, `Provider_unavailable_shows_retry_after`, `Student_sees_no_authoring_controls`, `Page_renders_rtl_in_arabic`.
**Keys:** `Live_*` (schedule, recurrence, status, errors).

### G3: Live session student side: join, countdown, recording, reminders (M)
**PRD:** FR-VC-01. **Depends:** G1, BE Task 5 (join, window, recording).
- `LiveSessionPanel` in the module viewer: title, start (ISO), duration, a **countdown from `serverNowUtc`** ("starts in 2 h 10 min", localized units from resources as in the assignment page), status chip.
- **Join** button enabled only inside the window (`Live.JoinWindowMinutes` before start until end, computed from the server clock offset); clicking calls `POST .../join` and opens the returned URL in a **new tab with `rel="noopener noreferrer"`**. The Zoom URL is never rendered in the page markup before the click and never shown in a list. `409 live_session_not_open` (window closed between render and click) refreshes the panel; a hidden module (`404`) shows the standard not-found state.
- After the session: recording link (when present) as an external link with `noopener`.
- Course home (`MyCoursePage`) and Home: "Upcoming live sessions" row/card with the next sessions across the caller's class courses (server order, at most five), each linking to its module.
- Staff-only `LiveParticipantsDialog` (`GET participants`, paged): name, joined, left, duration, match state chip (Matched / Guest).
- Notification labels `Notif_Live_Reminder` / `NotifEvent_Live_Reminder` in ar and en (parity test).
**Tests:** `Join_is_disabled_before_the_window_and_enabled_inside_it_by_server_time`, `Join_opens_a_new_tab_with_noopener_and_the_url_is_not_in_the_markup_before_the_click`, `Join_in_a_closed_window_refreshes_the_panel`, `Invisible_session_module_is_not_found`, `Recording_link_visible_after_the_session`, `Participants_list_shows_guests_distinctly`, `Home_card_lists_upcoming_sessions_only_with_LiveSessions_View`, `Notification_types_have_both_languages`, `Page_renders_rtl_in_arabic`.
**Keys:** `Live_*`.

### G4: Package upload, validation status and attach to a lesson (L)
**PRD:** FR-STD-01, FR-STD-02. **Depends:** G1-style `IPackagesApi` + fake, BE Task 6 (upload, validation job, states).
- `AddModuleMenu` entries "SCORM package" and "H5P package" (needs `Packages.Upload` and `Modules.Manage`).
- `PackageUploadDialog`: pick a file (`.zip` for SCORM, `.h5p` for H5P; extension and size checked before sending against `Files.MaxUploadMb` from `GET /api/assets/limits`), upload with the shared progress / cancel / retry transport, then **poll the status** (`GET /api/packages/{id}`: Uploaded, Validating, Ready, Rejected) with a visible progress state and a stop on navigation. Polling stops after a cap and offers "Check again" (no unbounded timers).
- `Rejected` shows the **reject code as a localized, plain-words reason** (`Error_package_invalid` and the specific codes the contract defines: too large, too many entries, unsafe path, unsupported SCORM sequencing / multi-SCO...), never the raw server text, and a "Choose another file" action.
- `Ready` shows what was found (title from the manifest, SCORM version or H5P library) and the attach form: lesson, module titles, visibility, target, completion rule (`Submit` or `Score`, `CompletionRules.ForActivity`), then `POST /api/packages/{id}/module`.
- Delete (`DELETE /api/packages/{id}`) with the usual confirm.
- `ScormContent` off: `feature_disabled` is mapped to the plain message; the upload never starts client-side work before the server accepts.
**Tests:** `Rejects_wrong_extension_and_oversize_before_upload`, `Shows_progress_and_then_validating_then_ready`, `Polling_stops_when_the_dialog_closes`, `Rejected_package_shows_a_localized_reason_per_code`, `Ready_package_can_be_attached_with_a_completion_rule`, `Attach_is_disabled_until_ready`, `Feature_disabled_shows_the_plain_message`, `Student_sees_no_upload_entries`, `Page_renders_rtl_in_arabic`.
**Keys:** `Package_*`, `Error_package_*`.

### G5: Sandboxed package player and the message bridge (L, after the security spike)
**PRD:** FR-STD-01, FR-STD-02, X-05, D51. **Depends:** the security spike (BE2 Task 2 ADR), BE Tasks 6 to 8, G4. **Not sized or started before the spike lands.**
- `PackagePlayer` component in the module viewer (`ModuleKinds.Scorm` / `H5p`): on "Start" (or "Resume") the page calls `POST /api/packages/{id}/launch` and renders an `<iframe>` whose `src` is the returned `launchUrl` on the **content origin**, with `sandbox="allow-scripts allow-forms"` **and never `allow-same-origin`, top navigation or popups**, a `title`, `referrerpolicy="no-referrer"`, `allow=""` (no features), `loading="lazy"`. The app never injects markup or scripts from a package into its own DOM.
- **Message bridge** (`wwwroot/js/package-bridge.js`, the only JS that touches package messages): listens for `message` events and accepts one only when `event.origin` equals the origin of the launch URL **and** `event.source` is the iframe's `contentWindow` **and** the payload matches the contract (type, attempt id, a size-capped data object). Everything else is dropped silently. Accepted `commit` / `finish` messages are forwarded to .NET, which throttles (`Packages.CommitMinIntervalSeconds`, coalescing the latest data), caps size, and calls `POST /api/package-attempts/{id}/commit` / `finish`. The attempt id comes from the launch response, never from the message.
- State and feedback: "Saving...", "Saved", "Could not save (retry)" like the quiz autosave; on `finish` the module state is reloaded (completion / score shown by the server's answer, never computed in the browser); a refused launch (`409 package_not_ready`, `404`) shows the standard states.
- Layout: the frame fills the content column, minimum 70 vh, a **Fullscreen** button (Fullscreen API on the iframe container, labelled), and at 360 px the frame is full width with a short "best on a larger screen" hint. A visible **Exit** control stays outside the frame; keyboard focus is never trapped (documented limit: the content inside the frame is third-party and the platform cannot guarantee its accessibility, Q-G5).
- Resume: SCORM keeps its suspend data on the server; the page offers "Resume" when the server reports an attempt in progress and "Start again" only where the contract allows a new attempt.
- H5P uses the same component and bridge with `xAPI` payloads; the result summary shows the score returned by the server.
**Tests (bUnit with stubbed JS interop):** `Player_iframe_is_sandboxed_without_same_origin_top_navigation_or_popups`, `Player_iframe_has_a_title_and_no_feature_permissions`, `Messages_from_another_origin_are_ignored`, `Messages_from_another_window_are_ignored`, `Malformed_or_oversize_messages_are_ignored`, `Commits_are_throttled_and_coalesced`, `Attempt_id_comes_from_the_launch_not_from_the_message`, `Finish_reloads_the_module_state`, `Save_failure_shows_state_and_retries`, `Hidden_module_launch_is_not_found`, `Resume_offered_when_an_attempt_is_in_progress`, `Fullscreen_button_has_a_label_and_exit_stays_outside_the_frame`, `Page_renders_rtl_in_arabic`.
**Keys:** `Package_*`.

### G6: Live session and package surfaces on the course home, Home and navigation (S)
**PRD:** FR-VC-01, X-01. **Depends:** G2, G3, G5.
Course home shows the next live sessions and package modules with completion states (already generic via `ModuleNodeDto`); Home "upcoming live sessions" card (student) and "sessions today" (teacher, `LiveSessions.Manage`) as `SectionCard`s; `Nav_` entry not needed (reached from the class course tabs). Notification labels for any further wave 3 events.
**Tests:** `Home_cards_depend_on_permissions` (extended), `Course_home_lists_upcoming_sessions_in_server_order`, `Notification_types_have_both_languages` (extended).

### G7: M4b FE hardening: accessibility, 360 px, RTL sweep, E2E hooks (M)
One pass over every M4b page, extending `M4aAccessibilityTests` (named controls, field labels, iframe `title`, timer / status `aria-live`), the dialog focus checks, `data-testid`s agreed with QA for the two E2E demos in BE2's plan (live session with the fake provider; SCORM package completing a module), 360 px and light / dark screenshots in ar and en. The player gets a dedicated security review checklist: sandbox attribute set, origin and source checks, no `allow-same-origin`, no package content in the app DOM, CSP of the app page.

## 4. Suggested order and parallelism

```
wave 3 contract (Lead) ── G1 ─┬─ G2 ─┐
                              └─ G3 ─┼─ G6 ── G7 (live part)
spike (BE2 T2 + QA) ── G4 ── G5 ─────┘            G7 (package part)
```

- **Live sessions** (G1 to G3) can start against the fake as soon as the wave 3 contract merges; they do not depend on the spike.
- **Packages** (G4, G5): G4 can start once the contract exists (upload, status, attach need no player). **G5 waits for the spike ADR** (origin layout, sandbox flags, CSP, token design).
- Two FE agents: FE-A takes G1 to G3 and G6; FE-B takes G4, G5 and G7. Both reuse the M4a components listed in section 1.

## 5. Open questions

**For the Lead / BE2:**
- **Q-G1 Capabilities read.** Without `Settings.View` the UI cannot know whether `VirtualClasses` / `ScormContent` are on. Hide the entries when off (needs a small capabilities read for non-admins, for example on `GET provider-status` or the permissions endpoint) or keep showing them and map `feature_disabled` to a message (this proposal's default)?
- **Q-G2 SyncFailed recovery.** Is there a "retry now" action for a `SyncFailed` session, or only the background sweep? (The UI shows the status either way; a button needs an endpoint.)
- **Q-G3 Time zones.** Sessions store UTC; the UI shows the viewer's local time with the zone name once, because teachers and students may be in different zones. Confirm: recurring series are generated in the teacher's local wall-clock time (daylight saving) or in fixed UTC offsets?
- **Q-G4 Class course tab placement.** "Live sessions" as a fourth tab (Plan, Live sessions, Forum, Announcements) or only through lesson modules plus the Home / course-home rows?

**For the spike (affects G5):**
- **Q-G5 Content-host origin and app CSP.** The app page's own CSP (today `frame-ancestors 'none'`, no `frame-src`) must allow the content origin in `frame-src`; the middleware needs the content host name in configuration. The launch URL is absolute, so the client needs no setting, but the host's per-site policy must be agreed. Also: what does the player do in dev and CI where a second host name may not exist?
- **Q-G6 Message contract.** Exact `postMessage` payload (types `commit`, `finish`, `ready`?), who injects the SCORM `API` / `API_1484_11` shim (served by the content host inside the frame vs a parent-frame shim), and whether the frame may ask the parent to resize (`height` messages) or only the fixed layout above is used. This proposal assumes the content host serves the shim and the app only forwards.
- **Q-G7 Tab close and offline.** `sendBeacon` from inside a sandboxed frame is not available to the app; is a last-chance commit on `visibilitychange` from the content shim acceptable, or does the server accept that the last interval may be lost?
- **Q-G8 Accessibility of third-party content.** Accept that package content may not meet WCAG and say so in the player (a short note and a download-free alternative is not possible), or require a teacher acknowledgement at upload?

**For the owner:**
- **Z-G1 Zoom client in the page.** Join always opens a Zoom link in a new tab (assumed). Embedding the Zoom Web SDK in the page is not proposed: it would need a second Zoom app type, SDK keys in the client, and a much larger CSP and privacy surface.
- **Z-G2 Reminders and Home.** Is the in-app reminder (bell) enough for v1, or are email / SMS reminders expected (M6 channels)? The UI only labels the in-app event.

## 6. Risks

- **The package player is the largest XSS / isolation surface of the frontend.** The sandbox flags, the origin and source checks and the "no package content in the app DOM" rule each get a named test and a QA review before merge; a regression here can reach other clients.
- **Timer and window correctness:** the join window and countdown depend on server time; they reuse the tested M4a server-offset pattern and carry explicit tests.
- **Spike dependency:** G5 is the only task blocked by the spike; G1 to G4 and G6 are not, so the schedule can absorb a late spike.
- **Polling and message throttling:** upload status polling and package commits are the two sources of request storms; both have caps, backoff and tests.
- **Third-party content accessibility:** cannot be guaranteed by the platform; documented in the player and in the hardening checklist.
- **File size:** the player (iframe + bridge + state) and the schedule dialog (recurrence preview) must be split into small components to respect the 300-line rule.
