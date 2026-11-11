# M4 Teaching & Learning — frontend task proposal

> **Status: READY for Team Lead review (updated 2026-11-12 for the owner's decisions, PRD D48–D51, and aligned with BE4's backend proposal #343).** Author: FE1. This is the frontend half only. It is meant to be merged into (or replaced by) the M4 plan the Lead and BE4 write, so the task numbers here (`F0`…`F16`) are local; the Lead renumbers them.
>
> **What changed since the first draft:** M4 is split into **M4a** (content, assignments, quizzes, forums; D51) and **M4b** (Zoom, SCORM, H5P); **LTI is out of scope for v1** (D50, the old F15 is removed); **rich text is a visual editor** in one shared component (D48, editor chosen under F4a); **Zoom is one platform app** (D49), so there is no per-client Zoom setup UI; section 4 records BE4's answers; section 5 lists only what is still open.
>
> **Sources read:** `docs/PRD.md` (FR-CNT-01..04, FR-ASG-01/02, FR-QZ-01..03, FR-LIB-01, FR-FOR-01, FR-VC-01, FR-STD-01..03, FR-LP-05, X-01, X-05, X-07, X-10, D44, §3.4), `docs/DEV-PLAN.md` §2 M4 (items 1–11), the merged contracts in `Lms.Shared` (`LessonPlanDtos`, `Permissions.Files/LessonPlans`, `Routes.Classes.cs`), the M3 plan format, and `docs/DESIGN.md` + `.claude/skills/lms-ui` from #334 (not yet on `main`; this proposal assumes they merge first).
>
> **Contracts do not exist yet** for any M4 module (`Lms.Shared` has no module, asset, assignment, quiz, library, forum, live-session or package DTOs, and no `Permissions` for them). Per the project rules `Lms.Shared` changes only through Lead contract PRs, so **F0 below is the Lead's contract task** and everything else is written against fakes first, the way M3 was.

## 0. Scope and shape

M4 gives **teachers** a way to build lessons from modules and **students** a way to consume them. The frontend is three surfaces:

1. **Authoring** (teacher / supervisor): add, edit, order, hide, schedule and target modules under the lessons of the class lesson plan that M3 T35 (#243/#339) already renders read-only.
2. **Consuming** (student): open the plan, open a module, complete it, see progress.
3. **Module-specific tools:** assignments (submit / grade), quizzes (build / attempt / grade), library, forum, live session, SCORM / H5P players.

What the frontend deliberately does **not** do in M4: gradebook screens (M5; the "grade flows to the gradebook" part is BE), timetable and attendance import of Zoom participants (M5), module authoring for lessons of *other* teachers' class courses (relationship rule §3.3 applies server-side; the UI just hides what the server forbids).

### Phases (D51)
- **M4a:** F0, F4a, F1–F10, F11 (library), F12 (forums), F15 and F16 (M4a part). Delivers the DoD slice: teacher builds lessons with content modules; student completes content, submits an assignment, takes a quiz.
- **M4b:** F13 (live sessions, Zoom), F14 (SCORM + H5P, after the QA security spike), and the M4b parts of F15 and F16.
- The digital library (FR-LIB-01) is not named in D51; this proposal keeps it in M4a because the "library resource" module type depends on it. **Lead to confirm.**

### Design and conventions (apply to every task)
- Follow `docs/DESIGN.md`: `PageHeader`, `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, `DetailItem`; one primary action per page; 360 px labelled-card tables (`DataLabel` on every `MudTd`); logical CSS only; Material Rounded icons; direction icons mirrored in RTL. Pages that M3 wrote before #334 (my class courses, lesson plan) get a small restyle task (F1) so the whole teaching flow looks the same.
- Per project rules: ar + en resource entries for every key (parity test), bUnit tests first, `Page_renders_rtl_in_arabic` per page, screenshots in `docs/screenshots/pr-<name>/` at 1440 px and 360 px in ar and en (light and dark once #334 lands), 0 warnings, files under ~300 lines (split dialogs and tabs).
- API clients follow the M3 pattern: `I<Module>Api` + HTTP implementation using `Routes.*` / `ProblemCodes.*` from `Lms.Shared` + in-memory fake registered only when `Lms:UseFakeApi=true`.
- Every page gates itself with `PermState.Has(...)` **and** relies on the server (`RequirePermission`); no UI-only security.
- Dates: `yyyy-MM-dd` invariant per DESIGN.md (the M3 plan view currently uses the culture short date and an optional Hijri suffix; F1 aligns it).
- Resource key prefixes (one per module, to keep the resx diff readable and merges mechanical): `Module_*`, `Asset_*`, `Content_*`, `Assignment_*`, `Quiz_*`, `Library_*`, `Forum_*`, `Live_*`, `Package_*` (SCORM/H5P).

## 1. Proposed permissions (for the Lead's contract PR F0)

None exist yet. Proposed constants, mirroring the existing `Module.Action` style and the PRD §3.4 matrix (teacher edits own class courses; supervisor edits courses it supervises; student consumes):

| Group | Constants | Used by |
|---|---|---|
| `Modules` | `View`, `Manage` | plan tree module nodes, authoring, completion mark (`View`) |
| Assets | **no new group: reuse `Files.Upload` / `Files.Download`** (BE4 #343 §5); the signed URL is also checked server-side against the owning module's `Modules.View` | module file picker, preview, streaming |
| `Assignments` (module) | `View`, `Manage`, `Submit`, `Grade` | note: `Assignments` already means *role assignments* in `Permissions`; use **`CourseAssignments`** or `Homework` to avoid the clash |
| `Quizzes` | `View`, `Manage`, `Attempt`, `Grade` | builder, attempt, manual grading |
| `Library` | `View`, `Manage` | library pages, "add to lesson" |
| `Forums` | `View`, `Post`, `Moderate` | forum, announcements |
| `LiveSessions` | `View`, `Manage` | schedule, join |
| `Packages` | `Upload`, `View` | SCORM / H5P upload and players |

BE4 adopted these names (#343 §5); the only difference is the assets row. The contract PR (F0) is the single place where they are finalised.

## 2. Task list

Each task = one issue = one PR. Order follows dependencies; "BE" names the backend work the UI waits for (or fakes). Estimated size S ≤ 1 day, M ≈ 2 days, L ≈ 3+ days for one FE agent.

### F0 — Contract (Lead lane): DTOs, routes, permissions, problem codes for M4
Not FE work, listed so the dependency is explicit. Needs: module DTOs (type enum, metadata, visibility incl. scheduled from–to, target = ClassCourse or group, completion rule view/submit/score), asset DTOs (upload init/complete, preview/stream URL), assignment / submission / grade DTOs, quiz / question / attempt DTOs, library, forum, live session, package, LTI DTOs; `Routes.*`; the `ClassLessonDto` extension carrying `Modules` under lessons (hidden for students, per FR-CNT-02 visibility) and progress per lesson/course; `serverNow` on every timed response; the problem codes BE4 lists in #343 §8 (`module_visibility_range_invalid`, `late_policy_invalid`, `submission_closed`, `resubmission_not_allowed`, `quiz_closed`, `attempt_limit_reached`, `quiz_has_attempts`, `package_invalid`, `asset_type_not_allowed`, `asset_too_large`, `module_target_invalid`). No LTI DTOs. **The M4a contract goes first; the M4b contract (live session, packages) is a second Lead PR.** **Unblocks every F-task below.**

### F1 — Plan view v2: modules in the tree, progress, DESIGN.md restyle (M)
**PRD:** FR-LP-05, FR-CNT-04, DEV-PLAN item 3. **Depends:** F0, #339, #334.
Extend `LessonPlanTree` with module nodes under each lesson (type icon, title, visibility/target chips, completion state for students), per-lesson and per-course progress bars, replace the "modules arrive in a later release" slot, move the page onto `PageHeader` / `SectionCard`, ISO dates. Keep hidden-lesson greying. Student view hides unpublished/scheduled-in-future modules (server filters; UI never shows what it is not sent).
**Tests:** `Student_sees_only_published_modules`, `Teacher_sees_hidden_and_scheduled_modules_with_chips`, `Progress_bar_reflects_completed_over_total`, `Pages_render_rtl_in_arabic`.
**Keys:** `LessonPlan_*` (extend), `Module_Status_*`, `Progress_*`.

### F2 — Asset service UI: upload, preview, signed streaming (L)
**PRD:** FR-CNT-03, X-05; item 1. **Depends:** F0, BE asset service.
Reusable components in `Components/Assets/`: `AssetUploader` (drag-drop + picker, per-client type/size limits shown up front and validated before upload, progress, cancel, retry; chunked/direct-to-MinIO per BE), `AssetPreview` (image `<img>`, PDF `<iframe>`/native viewer, audio/video native elements with a signed URL, refreshed on expiry, no-preview fallback with a download button when `Files.Download` allows). Extends the existing `IFileUploadApi` rather than replacing it.
**Tests:** `Rejects_disallowed_type_and_oversize_before_upload`, `Shows_progress_and_completes`, `Expired_signed_url_is_refreshed_once`, `Download_hidden_without_permission`.
**Keys:** `Asset_*`.

### F3 — Module authoring shell: add / edit / order / visibility / targeting (L)
**PRD:** FR-CNT-01/02, FR-LP-05. **Depends:** F0, F1, BE modules API.
Teacher-only controls on the plan tree: "Add module" menu (the 12 types of FR-CNT-01, filtered by permission), `ModuleDialog` shell with the common metadata (title ar/en, visibility published / hidden / scheduled from–to with date-time pickers, target whole class course or a group from M3 groups, completion rule), per-type body slot, drag-and-drop ordering within a lesson (reuse the blueprint tree's `MudDropContainer` approach incl. keyboard alternative: move up/down buttons for X-10), hide/show, delete with confirm, "move to another lesson". Locked blueprint sections/lessons stay non-editable (lock icon + tooltip text).
**Tests:** `Teacher_adds_page_module_under_own_lesson`, `Scheduled_requires_from_before_to`, `Group_target_lists_only_groups_of_this_class_course`, `Reorder_by_keyboard_buttons`, `Locked_blueprint_nodes_have_no_edit_controls`, `Student_sees_no_authoring_controls`.
**Keys:** `Module_*`.

### F4 — Simple module bodies: File, Video, Audio, Link, Page (rich text) (L)
**PRD:** FR-CNT-01 (types 1–5), FR-CNT-03. **Depends:** F2, F3.
Per-type editor and viewer: File/Video/Audio use `AssetUploader` + `AssetPreview`; Link validates URL scheme (http/https only) and opens with `rel="noopener noreferrer"`; **Page** uses the shared `RichTextEditor` (F4a) and renders only HTML that the server sanitised on write (D48; no raw `MarkupString` of anything the client produced). Student viewer page `/class-courses/{id}/modules/{moduleId}` with next/previous module navigation and a "Mark complete" button when the rule is manual.
**Tests:** `Link_rejects_javascript_scheme`, `Page_content_is_rendered_sanitised`, `Video_uses_signed_url`, `Manual_completion_marks_and_updates_progress`, rtl.
**Keys:** `Content_*`.

### F4a — Shared `RichTextEditor` component (M): build before F4, F6 and F12
**PRD:** D48. **Depends:** nothing (starts against a fake).
`Components/Editors/RichTextEditor.razor` wrapping **Quill 2.x** through JS interop (`wwwroot/js/richtext.js`; Quill JS/CSS vendored under `wwwroot/lib/quill/`, **no CDN**). Toolbar: bold, italic, underline, headings, ordered and bullet lists, link, blockquote, alignment, **text direction toggle (RTL/LTR per block)**, clear formatting. No image or video embeds in the first iteration (Q2a). Parameters: `Value` / `ValueChanged` (HTML string), `Label`, `Required`, `MaxLength`, `Disabled`, `HelperText`; the initial direction follows the UI language. Companion `SanitizedHtml` display component: renders server-sanitised HTML in `<div class="lms-rich">` with consistent typography per DESIGN.md and links forced to `rel="noopener noreferrer"`. Keyboard-accessible toolbar with an `aria-label` per button from resources; the toolbar wraps at 360 px; dark mode through theme tokens.
**Tests (bUnit, stubbed JS interop):** `Initial_html_is_passed_to_editor`, `Change_event_updates_bound_value`, `Required_blocks_form_when_empty` (an empty editor serialises as `<p><br></p>`, treated as empty), `Max_length_counts_text_not_markup`, `Disabled_editor_is_read_only`, `Direction_follows_ui_language`, rtl.
**Licence note (D48):** **Quill is BSD-3-Clause**: free for commercial use, no key, no usage cap, self-hosted. Chosen over TinyMCE because self-hosted TinyMCE 7 is GPL-2.0-or-later (or a paid commercial licence) and its cloud build needs an API key and reports usage; either would be a legal or operational decision the owner has not been asked to make. Gaps to accept: no tables or image resizing out of the box, and RTL relies on the `direction`/`align` formats (included here). Only `RichTextEditor` touches the library, so it can be re-backed by TinyMCE later without touching pages. **Approved by the Lead: Quill 2.x, self-hosted, in M4a as F4a** (the server-side `HtmlSanitizer` package is approved too).

### F5 — Student content view and completion (M)
**PRD:** FR-CNT-04, item 3. **Depends:** F4.
Student "my course" landing for a class course: lessons with progress, "continue where you left off", completion states (not started / in progress / done), automatic completion hooks (view-rule: on open; submit/score rules: shown as read-only state driven by the server). Teacher progress overview per student is **out of scope** (M5 reports) unless Q3 says otherwise.
**Tests:** `Continue_opens_first_incomplete_module`, `Auto_view_completion_on_open`, `Submit_rule_module_shows_waiting_state`.
**Keys:** `Progress_*`.

### F6 — Assignments: teacher side — create, publish, list submissions, grade (L)
**PRD:** FR-ASG-01, FR-ASG-02. **Depends:** F0, F2, F3, BE assignments.
`AssignmentDialog`/page: instructions (shared `RichTextEditor`, F4a), attachments, due date-time, max grade, late policy (allow / allow with penalty % / block), resubmission rule, target class or group, publish now or schedule. Submissions list (`ServerTable`: student, submitted at, late badge, status, grade), grading page: view submission (text + files with preview), grade + feedback + **annotated file upload** (a second upload slot, not an in-browser annotator; see Q4).
**Tests:** `Late_policy_penalty_requires_percentage`, `Cannot_publish_without_due_date_when_policy_blocks_late`, `Grade_above_max_rejected`, `Annotated_file_attached_to_feedback`, rtl.
**Keys:** `Assignment_*`.

### F7 — Assignments: student side — view, submit, resubmit (M)
**PRD:** FR-ASG-02. **Depends:** F6.
Assignment page for a student: instructions, attachments, due countdown with late state (uses server time offset, not the client clock, for the "overdue" decision), submit text and/or files, resubmission per rule, show grade + feedback + annotated file once graded. Blocked-late state explained in plain words.
**Tests:** `Blocked_late_policy_disables_submit`, `Resubmit_only_when_rule_allows`, `Submit_requires_text_or_file`, `Graded_shows_feedback`.

### F8 — Quiz builder (L)
**PRD:** FR-QZ-01, FR-QZ-02, item 5. **Depends:** F0, F3, BE quizzes.
Builder page: questions of type MCQ single / multi, true-false, essay; options with correct flags; points; question **pool per quiz** (draw N of M) UI; settings tab: time limit, attempts, grading method (highest / last / average), shuffle questions/options, open / close dates, review policy; preview-as-student mode; reorder questions (drag + buttons). Autosave of the draft is **not** assumed: explicit Save with unsaved-changes guard.
**Tests:** `Mcq_needs_at_least_one_correct_option`, `Multi_requires_two_or_more_options`, `Pool_draw_count_cannot_exceed_pool_size`, `Close_after_open_validated`, `Leaving_with_unsaved_changes_asks`, rtl.
**Keys:** `Quiz_*`.

### F9 — Quiz attempt (student) (L)
**PRD:** FR-QZ-03. **Depends:** F8.
Attempt page: start confirmation (attempts left, time limit), question navigator, **server-driven countdown timer** (sync remaining seconds from the server on start and on each autosave; local ticking only for display), **autosave** on answer change (debounced, queued, offline-tolerant retry with visible "saved / saving / failed" state), auto-submit at zero and when the server says the quiz closed, confirmation before submit, result page according to the review policy (score only / answers / nothing), keyboard operable, no copy-protect gimmicks.
**Tests:** `Answers_autosave_debounced`, `Failed_autosave_shows_state_and_retries`, `Timer_uses_server_remaining_time`, `Auto_submits_at_zero`, `Review_policy_hides_answers`, `Resume_unfinished_attempt`.

### F10 — Quiz grading (essays) and attempt review (M)
**PRD:** FR-QZ-03. **Depends:** F9.
Teacher list of attempts per quiz, essay grading queue (per question or per student), points + comment, finalize, regrade after changing a correct option is **BE decision** (Q5). Student review view per policy.
**Tests:** `Essay_points_capped_at_question_points`, `Finalize_disabled_until_all_essays_graded`.

### F11 — Digital library (M), M4a
**PRD:** FR-LIB-01, item 6. **Depends:** F0, F2, BE library.
`/library` page (`ServerTable` or card grid with cover): search, filter by type / level / subject / tags, access scoping is server-side; resource dialog (title, type, level, subject, tags, cover upload, file or link, "download allowed" toggle) for `Library.Manage`; in-browser reader (reuse `AssetPreview`; download button only when allowed); **"Add to lesson"** from the library page and from F3's module menu (picker dialog).
**Tests:** `Download_button_hidden_when_disabled`, `Filters_compose`, `Add_to_lesson_creates_module_reference`, rtl.
**Keys:** `Library_*`.

### F12 — Forum and announcements (M), M4a
**PRD:** FR-FOR-01, item 7. **Depends:** F0, F2, BE forums.
Per class course: tabs on the class-course page (Plan · Forum · Announcements). Threads list (pinned first, locked badge, reply count), thread view with replies and attachments, new thread / reply composer (plain text + attachments; the shared `RichTextEditor` (F4a)), moderation menu for `Forums.Moderate` (delete, lock, pin) with confirm. Announcements: list, create (pin, notify toggle), student read view.
**Tests:** `Locked_thread_hides_reply_composer`, `Moderator_sees_delete_lock_pin`, `Student_cannot_see_moderation`, `Pinned_threads_first`, `Announcement_notify_toggle_sent`.
**Keys:** `Forum_*`.

### F13 — Live sessions (virtual classroom) (M), M4b
**PRD:** FR-VC-01, item 8. **Depends:** F0, BE provider abstraction + Zoom.
Schedule dialog from a class course (title, start, duration, single or recurring with end date / count, provider default Zoom), session list (upcoming / past) with **Join** button enabled from N minutes before start (server provides the join URL; opens in a new tab with `noopener`), reminders configuration is BE, recording link shown after the session, teacher can attach/edit the recording link. Zoom is **one platform-level app** (D49): there is no credentials or per-client provider screen. If the platform app is not configured the page shows an explanatory empty state ("ask your platform administrator"), without a link to client Settings.
**Tests:** `Recurring_requires_end`, `Join_enabled_only_in_window`, `Recording_link_visible_after_end`, `Provider_missing_shows_platform_admin_hint`.
**Keys:** `Live_*`.

### F14 — SCORM and H5P: upload and players (L, spike first), M4b
**PRD:** FR-STD-01, FR-STD-02, items 9–10. **Depends:** F2, BE package services.
Upload with manifest validation errors surfaced as a list; player component hosting the package in a **sandboxed iframe** served from a separate origin/path by the API (the FE never runs package scripts in the app origin); SCORM runtime API shim over `postMessage` to report completion/score through the BE; H5P embed with xAPI statements forwarded the same way. **Spike (S) first, together with QA's security spike (D51: sandboxed origin)**, to settle the SCORM runtime location and the H5P library choice before the two L implementations.
**Tests:** `Manifest_errors_listed`, `Player_iframe_is_sandboxed_without_same_origin`, `Completion_message_updates_progress` (bUnit with a stubbed JS interop).
**Keys:** `Package_*`.

### F15 — Navigation, dashboards and notifications for M4 (S)
Nav entries (Library), Home cards for student (due assignments, open quizzes, upcoming live sessions) and teacher (to grade, upcoming sessions), notification type labels in ar/en for M4 events (`Assignment.Published/DueSoon/Graded`, `Quiz.Opened`, `Forum.Announcement`, and in M4b `Live.Reminder`, as named in #343 §5) so the existing bell/notification page renders them.
**Tests:** `Home_cards_depend_on_permissions`, `Notification_types_have_both_languages` (a parity test over the new event keys).

### F16 — M4 FE hardening: accessibility, 360 px, RTL sweep, E2E hooks (M)
One pass over all M4 pages (M4a part after F12, M4b part after F14): axe-style checks in bUnit where the repo already has them (`A11yTests`), focus management in dialogs and the quiz timer (announce remaining time at intervals, not every second), `data-testid`s agreed with QA for the Playwright flow in the M4 DoD ("teacher builds lessons with every module type; student completes content, submits an assignment, takes a quiz"), screenshots refreshed in light + dark.

## 3. Suggested order and parallelism

```
M4a:  F0 (Lead) ─┬─ F4a (editor, no BE needed) ──────────┐
                 ├─ F2 (assets) ─┐                        │
                 └─ F1 ─ F3 ─────┼─ F4 ─ F5               │
                                 ├─ F6 ─ F7  (uses F4a) ──┤
                                 ├─ F8 ─ F9 ─ F10         │
                                 ├─ F11, F12 (F12 uses F4a)
                                 └─ F15, F16 (M4a part)
M4b:  F0b (Lead) ── F13, F14 (spike, then build), F15/F16 additions
```

- Critical path (M4a): **F0 → F2 → F3 → F4 → F5**, which delivers "student completes content". F4a has no backend dependency and can start as soon as the Lead approves Quill. Assignments (F6/F7) and quizzes (F8–F10) run in parallel with a second FE agent after F3.
- Two FE agents: FE1 = F4a, F1, F3, F4, F5, F11, F12; FE2 = F2, F6, F7, F8–F10; M4b goes to whoever is free.
- Each module's FE can start against a fake as soon as F0 merges; the BE tasks only gate the end-to-end checks.

## 4. Alignment with BE4 (resolved in #343 §8)

| # | Point | Agreed position |
|---|---|---|
| 1 | Plan DTO | `ClassLessonDto` carries nested `Modules` (id, kind, title, order, visibility, the caller's completion state); one read for the tree. `GET /api/modules/{id}` for the viewer (typed payload, asset url, `serverNow`). |
| 2 | Visibility | Backend filters for students (hidden, outside the from/to window, targeted at a group they are not in); teachers get everything with a flag. The FE never filters for security. |
| 3 | Asset flow | v1 streams through the API (`POST /api/assets` multipart); limits from `GET /api/assets/limits` (`allowedTypes`, `maxBytes`) so `AssetUploader` validates up front. Presigned direct upload is a later optimisation behind the same component; the FE hides the transport. |
| 4 | Server time | `serverNow` next to every compared date; `remainingSeconds` on attempts; the FE only displays and counts down locally. |
| 5 | Problem codes | The eleven codes in F0, each with an `Error_{code}` resource pair in ar and en. |
| 6 | Completion | Manual mark is idempotent; automatic completion from events; `GET /api/class-courses/{id}/progress` plus progress on lesson and module nodes. **Decided by the Lead:** the route is `/api/modules/{id}/completion` (GET and PUT). |

## 5. Decisions taken (D48–D51) and what is still open

Decided by the owner or accepted by the Lead:
- **Rich text (D48):** visual editor, one shared component, the server sanitises with an allow-list (`HtmlSanitizer`). Editor: **Quill 2.x**, licence note under F4a.
- **Zoom (D49):** one platform app; no per-client credentials UI.
- **LTI (D50):** out of scope for v1; no F-task, no `LtiTools` permission group, no `Lti_*` keys.
- **Split (D51):** M4a then M4b; sandboxed origin for SCORM/H5P after a QA spike; signed-URL video (no HLS); a quiz pool draws N per attempt; group-targeted modules are visible only to that group and its teachers.
- Agreed with BE4 (#343): permission granularity, teacher progress view in M5, edits to correct answers rejected after the first attempt (`quiz_has_attempts`).
- Defaults kept: annotated feedback is an uploaded marked-up file; quiz attempts autosave with a visible state and retry, no offline mode.

Still open (for the Lead; Quill and the `/completion` route are settled):
- **Q2a Images inside rich text:** not in the first iteration. If wanted later: upload through `AssetUploader` and insert a signed asset reference, never a base64 image.
- **Library phase:** M4a (assumed) or M4b.

## 6. Risks

- **Rich text and package players are the two XSS surfaces.** Both get a dedicated test (`…_is_sanitised`, `…_is_sandboxed`) and a QA security review before merge.
- **Timer correctness** (quiz, due dates) depends on server time; a client-clock bug would be a grading dispute. F9 and F7 carry explicit tests.
- **File size of the plan page code:** the module authoring and viewer logic must live in separate components (`ModuleNode`, `ModuleDialog`, per-type editors/viewers) to respect the ~300-line rule.
- **#334 not on `main` yet:** the shared `Components/Ui/` components do not exist for the current branch; F1+ should start after #334 merges, or use the legacy pattern and be restyled (cost: one extra pass).
