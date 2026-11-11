# M4 Teaching & Learning — frontend task proposal

> **Status: DRAFT for Team Lead review.** Author: FE1, 2026-11-12. This is the frontend half only. It is meant to be merged into (or replaced by) the M4 plan the Lead and BE4 write, so the task numbers here (`F1`…`F17`) are local; the Lead renumbers them. Nothing here is a decision: every open question at the end has a recommended default.
>
> **Sources read:** `docs/PRD.md` (FR-CNT-01..04, FR-ASG-01/02, FR-QZ-01..03, FR-LIB-01, FR-FOR-01, FR-VC-01, FR-STD-01..03, FR-LP-05, X-01, X-05, X-07, X-10, D44, §3.4), `docs/DEV-PLAN.md` §2 M4 (items 1–11), the merged contracts in `Lms.Shared` (`LessonPlanDtos`, `Permissions.Files/LessonPlans`, `Routes.Classes.cs`), the M3 plan format, and `docs/DESIGN.md` + `.claude/skills/lms-ui` from #334 (not yet on `main`; this proposal assumes they merge first).
>
> **Contracts do not exist yet** for any M4 module (`Lms.Shared` has no module, asset, assignment, quiz, library, forum, live-session or package DTOs, and no `Permissions` for them). Per the project rules `Lms.Shared` changes only through Lead contract PRs, so **F0 below is the Lead's contract task** and everything else is written against fakes first, the way M3 was.

## 0. Scope and shape

M4 gives **teachers** a way to build lessons from modules and **students** a way to consume them. The frontend is three surfaces:

1. **Authoring** (teacher / supervisor): add, edit, order, hide, schedule and target modules under the lessons of the class lesson plan that M3 T35 (#243/#339) already renders read-only.
2. **Consuming** (student): open the plan, open a module, complete it, see progress.
3. **Module-specific tools:** assignments (submit / grade), quizzes (build / attempt / grade), library, forum, live session, SCORM / H5P / LTI players.

What the frontend deliberately does **not** do in M4: gradebook screens (M5; the "grade flows to the gradebook" part is BE), timetable and attendance import of Zoom participants (M5), module authoring for lessons of *other* teachers' class courses (relationship rule §3.3 applies server-side; the UI just hides what the server forbids).

### Design and conventions (apply to every task)
- Follow `docs/DESIGN.md`: `PageHeader`, `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, `DetailItem`; one primary action per page; 360 px labelled-card tables (`DataLabel` on every `MudTd`); logical CSS only; Material Rounded icons; direction icons mirrored in RTL. Pages that M3 wrote before #334 (my class courses, lesson plan) get a small restyle task (F1) so the whole teaching flow looks the same.
- Per project rules: ar + en resource entries for every key (parity test), bUnit tests first, `Page_renders_rtl_in_arabic` per page, screenshots in `docs/screenshots/pr-<name>/` at 1440 px and 360 px in ar and en (light and dark once #334 lands), 0 warnings, files under ~300 lines (split dialogs and tabs).
- API clients follow the M3 pattern: `I<Module>Api` + HTTP implementation using `Routes.*` / `ProblemCodes.*` from `Lms.Shared` + in-memory fake registered only when `Lms:UseFakeApi=true`.
- Every page gates itself with `PermState.Has(...)` **and** relies on the server (`RequirePermission`); no UI-only security.
- Dates: `yyyy-MM-dd` invariant per DESIGN.md (the M3 plan view currently uses the culture short date and an optional Hijri suffix; F1 aligns it).
- Resource key prefixes (one per module, to keep the resx diff readable and merges mechanical): `Module_*`, `Asset_*`, `Content_*`, `Assignment_*`, `Quiz_*`, `Library_*`, `Forum_*`, `Live_*`, `Package_*` (SCORM/H5P), `Lti_*`.

## 1. Proposed permissions (for the Lead's contract PR F0)

None exist yet. Proposed constants, mirroring the existing `Module.Action` style and the PRD §3.4 matrix (teacher edits own class courses; supervisor edits courses it supervises; student consumes):

| Group | Constants | Used by |
|---|---|---|
| `Modules` | `View`, `Manage` | plan tree module nodes, authoring, completion mark (`View`) |
| `Assets` (or reuse `Files.*`) | `Upload`, `View` | module file picker, preview, streaming |
| `Assignments` (module) | `View`, `Manage`, `Submit`, `Grade` | note: `Assignments` already means *role assignments* in `Permissions`; use **`CourseAssignments`** or `Homework` to avoid the clash |
| `Quizzes` | `View`, `Manage`, `Attempt`, `Grade` | builder, attempt, manual grading |
| `Library` | `View`, `Manage` | library pages, "add to lesson" |
| `Forums` | `View`, `Post`, `Moderate` | forum, announcements |
| `LiveSessions` | `View`, `Manage` | schedule, join |
| `Packages` | `Upload`, `View` | SCORM / H5P upload and players |
| `LtiTools` | `View`, `Manage` (admin registration) , `Launch` | LTI registration page, module launch |

Open question Q1 asks whether these are the right granularity (the more permissions, the more nav/page-gate code and tests).

## 2. Task list

Each task = one issue = one PR. Order follows dependencies; "BE" names the backend work the UI waits for (or fakes). Estimated size S ≤ 1 day, M ≈ 2 days, L ≈ 3+ days for one FE agent.

### F0 — Contract (Lead lane): DTOs, routes, permissions, problem codes for M4
Not FE work, listed so the dependency is explicit. Needs: module DTOs (type enum, metadata, visibility incl. scheduled from–to, target = ClassCourse or group, completion rule view/submit/score), asset DTOs (upload init/complete, preview/stream URL), assignment / submission / grade DTOs, quiz / question / attempt DTOs, library, forum, live session, package, LTI DTOs; `Routes.*`; the `ClassLessonPlanDto` extension carrying modules under lessons (hidden for students, per FR-CNT-02 visibility) and progress per lesson/course. **Unblocks every F-task below.**

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
Per-type editor and viewer: File/Video/Audio use `AssetUploader` + `AssetPreview`; Link validates URL scheme (http/https only) and opens with `rel="noopener noreferrer"`; **Page** needs a rich-text editor (see Q2) with **server-sanitised HTML only rendered via a sanitising component** (no raw `MarkupString` of unsanitised input). Student viewer page `/class-courses/{id}/modules/{moduleId}` with next/previous module navigation and a "Mark complete" button when the rule is manual.
**Tests:** `Link_rejects_javascript_scheme`, `Page_content_is_rendered_sanitised`, `Video_uses_signed_url`, `Manual_completion_marks_and_updates_progress`, rtl.
**Keys:** `Content_*`.

### F5 — Student content view and completion (M)
**PRD:** FR-CNT-04, item 3. **Depends:** F4.
Student "my course" landing for a class course: lessons with progress, "continue where you left off", completion states (not started / in progress / done), automatic completion hooks (view-rule: on open; submit/score rules: shown as read-only state driven by the server). Teacher progress overview per student is **out of scope** (M5 reports) unless Q3 says otherwise.
**Tests:** `Continue_opens_first_incomplete_module`, `Auto_view_completion_on_open`, `Submit_rule_module_shows_waiting_state`.
**Keys:** `Progress_*`.

### F6 — Assignments: teacher side — create, publish, list submissions, grade (L)
**PRD:** FR-ASG-01, FR-ASG-02. **Depends:** F0, F2, F3, BE assignments.
`AssignmentDialog`/page: instructions (rich text, same component as F4), attachments, due date-time, max grade, late policy (allow / allow with penalty % / block), resubmission rule, target class or group, publish now or schedule. Submissions list (`ServerTable`: student, submitted at, late badge, status, grade), grading page: view submission (text + files with preview), grade + feedback + **annotated file upload** (a second upload slot, not an in-browser annotator; see Q4).
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

### F11 — Digital library (M)
**PRD:** FR-LIB-01, item 6. **Depends:** F0, F2, BE library.
`/library` page (`ServerTable` or card grid with cover): search, filter by type / level / subject / tags, access scoping is server-side; resource dialog (title, type, level, subject, tags, cover upload, file or link, "download allowed" toggle) for `Library.Manage`; in-browser reader (reuse `AssetPreview`; download button only when allowed); **"Add to lesson"** from the library page and from F3's module menu (picker dialog).
**Tests:** `Download_button_hidden_when_disabled`, `Filters_compose`, `Add_to_lesson_creates_module_reference`, rtl.
**Keys:** `Library_*`.

### F12 — Forum and announcements (M)
**PRD:** FR-FOR-01, item 7. **Depends:** F0, F2, BE forums.
Per class course: tabs on the class-course page (Plan · Forum · Announcements). Threads list (pinned first, locked badge, reply count), thread view with replies and attachments, new thread / reply composer (plain text + attachments; rich text only if Q2 settles on a shared editor), moderation menu for `Forums.Moderate` (delete, lock, pin) with confirm. Announcements: list, create (pin, notify toggle), student read view.
**Tests:** `Locked_thread_hides_reply_composer`, `Moderator_sees_delete_lock_pin`, `Student_cannot_see_moderation`, `Pinned_threads_first`, `Announcement_notify_toggle_sent`.
**Keys:** `Forum_*`.

### F13 — Live sessions (virtual classroom) (M)
**PRD:** FR-VC-01, item 8. **Depends:** F0, BE provider abstraction + Zoom.
Schedule dialog from a class course (title, start, duration, single or recurring with end date / count, provider default Zoom), session list (upcoming / past) with **Join** button enabled from N minutes before start (server provides the join URL; opens in a new tab with `noopener`), reminders configuration is BE, recording link shown after the session, teacher can attach/edit the recording link. Provider not configured → explanatory empty state (links to Settings for admins).
**Tests:** `Recurring_requires_end`, `Join_enabled_only_in_window`, `Recording_link_visible_after_end`, `Provider_missing_shows_admin_hint`.
**Keys:** `Live_*`.

### F14 — SCORM and H5P: upload and players (L, spike first)
**PRD:** FR-STD-01, FR-STD-02, items 9–10. **Depends:** F2, BE package services.
Upload with manifest validation errors surfaced as a list; player component hosting the package in a **sandboxed iframe** served from a separate origin/path by the API (the FE never runs package scripts in the app origin); SCORM runtime API shim over `postMessage` to report completion/score through the BE; H5P embed with xAPI statements forwarded the same way. **Spike (S) first** to settle Q6 (SCORM runtime location, H5P library choice) before the two L implementations.
**Tests:** `Manifest_errors_listed`, `Player_iframe_is_sandboxed_without_same_origin`, `Completion_message_updates_progress` (bUnit with a stubbed JS interop).
**Keys:** `Package_*`.

### F15 — LTI 1.3: tool registration and launch (M)
**PRD:** FR-STD-03, item 11. **Depends:** F0, BE LTI.
Admin page `/lti-tools` (`LtiTools.Manage`): register / edit / deactivate tool (client id, deployment id, JWKS URL, OIDC login URL, launch URL, scopes), show the platform values the tool needs to be given (issuer, JWKS, redirect URIs) with copy buttons. Module viewer launches via a server-provided form POST target in an iframe or new tab (per tool setting), with a loading/failed state; grade passback is BE.
**Tests:** `Required_urls_must_be_https`, `Platform_values_shown_with_copy`, `Launch_posts_to_server_target`.
**Keys:** `Lti_*`.

### F16 — Navigation, dashboards and notifications for M4 (S)
Nav entries (Library, LTI tools), Home cards for student (due assignments, open quizzes, upcoming live sessions) and teacher (to grade, upcoming sessions), notification type labels in ar/en for M4 events (new assignment, due soon, graded, quiz opens, new forum reply, announcement) so the existing bell/notification page renders them.
**Tests:** `Home_cards_depend_on_permissions`, `Notification_types_have_both_languages` (a parity test over the new event keys).

### F17 — M4 FE hardening: accessibility, 360 px, RTL sweep, E2E hooks (M)
One pass over all M4 pages: axe-style checks in bUnit where the repo already has them (`A11yTests`), focus management in dialogs and the quiz timer (announce remaining time at intervals, not every second), `data-testid`s agreed with QA for the Playwright flow in the M4 DoD ("teacher builds lessons with every module type; student completes content, submits an assignment, takes a quiz"), screenshots refreshed in light + dark.

## 3. Suggested order and parallelism

```
F0 (Lead) ─┬─ F1 ─ F3 ─┬─ F4 ─ F5
           │           ├─ F6 ─ F7
           │           ├─ F8 ─ F9 ─ F10
           ├─ F2 ──────┤
           │           ├─ F11, F12, F13
           │           └─ F14 (spike → build), F15
           └─ F16, F17 last
```

- Critical path: **F0 → F2 → F3 → F4 → F5**, which delivers the DoD's "student completes content" slice. Assignments (F6/F7) and quizzes (F8–F10) can run in parallel with a second FE agent after F3.
- Two FE agents: FE1 = F1, F3, F4, F5, F11, F12; FE2 = F2, F6, F7, F8–F10, F13; the heavy shared pieces (F2 asset components, the rich-text component, the module dialog shell) land first so the other tasks reuse them.
- Each module's FE can start against a fake as soon as F0 merges; the BE tasks only gate the end-to-end checks.

## 4. Alignment with BE4 (to settle in this PR's review)

1. **Plan DTO:** modules nested under `ClassLessonDto` (one read for the whole tree) versus a separate `GET …/lessons/{key}/modules`. The proposal assumes nested for the tree, plus a dedicated `GET /api/modules/{id}` for the viewer.
2. **Visibility filtering** (hidden, scheduled-in-future, target group the student is not in) happens in the BE for students, as it does for hidden lessons today.
3. **Asset flow:** direct-to-MinIO presigned upload (init → PUT → complete) vs streamed through the API; the FE needs the limits (`allowedTypes`, `maxBytes`) from a settings/limits endpoint so it can validate up front.
4. **Server time:** every timed feature (assignment due, quiz timer, live-join window, scheduled visibility) needs `serverNow` or remaining seconds in the response; the FE must not trust the client clock.
5. **Problem codes** for each validation the FE otherwise duplicates (module visibility range, late-policy conflict, quiz closed, attempt limit reached, manifest invalid) so `Error_{code}` resources exist in both languages.
6. **Completion API:** one endpoint to mark manual completion and one event path for automatic completion (open / submit / score); progress read model per lesson and per course.

## 5. Open questions (recommended default in bold)

- **Q1 Permissions granularity** (section 1): **the table as proposed, with `CourseAssignments` instead of a second `Assignments`.**
- **Q2 Rich-text editor** for Page modules, assignment instructions and forum posts: no editor in MudBlazor. Options: (a) **TinyMCE or Quill via JS interop in one shared `RichTextEditor` component with server-side sanitising**, (b) Markdown with preview (simpler, safer, teachers dislike it), (c) a basic bold/italic/list/link toolbar built in-house. Needs the Lead's/product owner's call because it is a new third-party dependency.
- **Q3 Teacher progress view:** per-student progress for a class course in M4 or M5 reports? **M5.**
- **Q4 Annotated file feedback:** upload a marked-up file (**as proposed**) vs an in-browser PDF annotator (large; not in v1).
- **Q5 Regrade** when an author edits a question after attempts exist: **BE rejects edits to correct answers after the first attempt and the UI explains it**, rather than regrade tooling.
- **Q6 SCORM/H5P hosting:** serve packages from a **separate sandboxed origin or path with a restrictive CSP**; H5P player library (`h5p-standalone`) vs custom. Needs a short spike and a security look from QA before F14 is sized.
- **Q7 Video:** native `<video>` with signed URL and HTTP range (**v1**) vs HLS. Large school videos may need HLS later; not planned.
- **Q8 Offline / flaky networks for quiz attempts:** autosave with retry and a visible state (**as proposed**); no full offline mode.

## 6. Risks

- **Rich text and package players are the two XSS surfaces.** Both get a dedicated test (`…_is_sanitised`, `…_is_sandboxed`) and a QA security review before merge.
- **Timer correctness** (quiz, due dates) depends on server time; a client-clock bug would be a grading dispute. F9 and F7 carry explicit tests.
- **File size of the plan page code:** the module authoring and viewer logic must live in separate components (`ModuleNode`, `ModuleDialog`, per-type editors/viewers) to respect the ~300-line rule.
- **#334 not on `main` yet:** the shared `Components/Ui/` components do not exist for the current branch; F1+ should start after #334 merges, or use the legacy pattern and be restyled (cost: one extra pass).
