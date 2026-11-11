# M4 Teaching & Learning — backend task proposal (DRAFT for Team Lead review)

Status: **proposal**, written by Backend developer 4. Nothing here is decided; the Team Lead turns it into the approved M4 plan, the contract PR (`src/Lms.Shared`) and the issues. Sources: PRD §6.6 (FR-LP-05), §6.8 (FR-CNT, FR-ASG, FR-QZ, FR-LIB, FR-FOR, FR-VC, FR-STD), D44 (FR-LP-05 deferred to M4), DEV-PLAN M4 items 1–11, PRD §3.4 matrix row "Content & activities".

## 1. What M4 has to deliver (exit demo)

A teacher builds lessons with every module type; a student completes content, submits an assignment and takes a quiz. The M5 gradebook is not here, but **M4 must emit the grade events** (assignment graded, quiz attempt scored, SCORM/H5P/LTI score) so M5 can consume them without reworking M4 (see §6).

## 2. Shared foundations (build first; everything else sits on them)

| Piece | Why | Notes |
|---|---|---|
| **Lesson module core** (`LessonModule`, FR-LP-05, FR-CNT-01/02) | Every content type hangs off it; class lesson plan (M3 #225) already has `ClassLesson` with the locked/teacher-content split ready | One table `LessonModules` (id, `ClassLessonId`, `ClassCourseId`, type, title ar/en, order, visibility `Published/Hidden/Scheduled(from,to)`, target `Course/Group(id)`, completion rule `None/View/Submit/Score(min)`, `RefId` + `RefKind` to the typed payload). Tenant-owned, unit-scoped (OrgUnitId/Path of the class), soft delete, `[Audited]`. |
| **Visibility + target resolver** | Students must see only published, in-window modules for their enrollment and group | One `IModuleAccess` service reused by every student-facing read (content, assignments, quizzes, forum announcements). Mirrors `RelationshipAccess` style. |
| **Asset service** (FR-CNT-03, X-05) | Files exist (`IFileStorage`, MinIO, `Files.Upload/Download`) but there is no domain asset: type/size limits per client, preview, streaming | Add `Asset` (file id, kind, size, duration, owner class course) + settings `Files.AllowedTypes`/`MaxSizeMb` (per client, inherited) + `GET /api/assets/{id}/url` (signed, permission checked against the owning module). Video: signed URL with range requests (no transcoding in v1). |
| **Progress / completion** (FR-CNT-04) | Students mark or auto-complete; teacher sees progress | `ModuleCompletion` (module, student profile, state, completedAt, score?) + rollups per lesson/course computed in queries (no stored counters in v1). |
| **Grade event outbox** | M5 consumes scores | `IGradeEvents` interface in Domain with a no-op/outbox implementation in M4 (a `GradeEvent` row per scored thing: source kind/id, student, class course, points, max, at). M5 reads it. Keeps M4 and M5 decoupled. |

## 3. Proposed backend tasks (one issue / PR each, dependency order)

Naming follows the M3 map (T-numbers continue from M4's start; the Lead assigns lanes). "Perm" = permission constants the contract PR must add (listed once in §5).

| # | Task | Entities (new) | Endpoints (sketch) | Depends on |
|---|---|---|---|---|
| B1 | **Module core + class plan authoring** (FR-LP-05, FR-CNT-02) | `LessonModule` | `GET /api/class-lessons/{id}/modules`, `POST/PUT/DELETE /api/class-lessons/{id}/modules/{id}`, `PUT /api/class-lessons/{id}/modules/order`; the M3 plan-tree read (#225) is extended to nest modules (students: visible ones only) | M3 #225 merged |
| B2 | **Asset service** (FR-CNT-03) | `Asset` | `POST /api/assets` (multipart, limits), `GET /api/assets/{id}/url`, `GET /api/assets/{id}/preview-meta`; settings for allowed types/size | B1 |
| B3 | **Simple module types**: File, Audio, Video, Page (rich text, sanitised), Link (FR-CNT-01) | payload tables `PageContent`, `LinkContent`; File/Audio/Video use `Asset` | module create/update accepts the typed payload; `GET /api/modules/{id}` for students | B1, B2 |
| B4 | **Completion & progress** (FR-CNT-04) | `ModuleCompletion` | `POST /api/modules/{id}/complete`, `GET /api/class-courses/{id}/progress` (student own / teacher all), auto-completion hooks (view, submit, score) | B3 |
| B5 | **Assignments** (FR-ASG-01/02) | `Assignment`, `AssignmentAttachment`, `Submission`, `SubmissionFile`, `SubmissionFeedback` | teacher CRUD + publish/schedule, student `POST …/submit` (file and/or text, resubmission rule, late policy allow/penalty/block computed server-side), teacher grade + feedback + annotated file, notifications new/due-soon/graded (Hangfire due-soon job) | B3, notifications (M1) |
| B6 | **Quiz model + builder** (FR-QZ-01/02) | `Quiz`, `QuizQuestion` (type MCQ-single/multi, TF, essay), `QuizOption`, `QuizPool` | builder CRUD, settings (time limit, attempts, grading method, shuffle, open/close, review policy) | B3 |
| B7 | **Quiz attempts + grading** (FR-QZ-03) | `QuizAttempt`, `QuizAnswer` | start (attempt limits, server clock timer, shuffled snapshot), autosave, submit, auto-grade objective, manual essay grading, auto-submit unfinished at close (Hangfire), score → grade event | B6 |
| B8 | **Digital library** (FR-LIB-01, D13) | `LibraryResource`, `LibraryTag` | resources CRUD (Supervisor/Admin/Teacher), search/filter, access by unit + level, reader-url endpoint with download-disabled flag (the protected viewer streams, never gives a download URL when disabled), "add to lesson" creates a Library module | B2, B3 |
| B9 | **Forums + announcements** (FR-FOR-01) | `ForumThread`, `ForumPost`, `CourseAnnouncement` | threads/replies/attachments, moderation (delete/lock/pin), course announcements (pin, notify) | B2 |
| B10 | **Virtual classroom** (FR-VC-01) | `LiveSession`, `LiveSessionSeries`, `MeetingProviderAccount` | `IMeetingProvider` abstraction, Zoom server-to-server OAuth impl, create single/recurring, reminders (Hangfire), join link endpoint, participant import → attendance (M5 interface), recording link attach | B3 |
| B11 | **SCORM 1.2/2004** (FR-STD-01) | `ScormPackage`, `ScormSco`, `ScormTracking` | upload + manifest validation (zip hardening: size, entry count, path traversal), launch-url hosting under a sandboxed origin path, runtime API endpoint for tracking commits, completion/score → events | B2, B4 |
| B12 | **H5P** (FR-STD-02) | `H5pContent` | upload + validation (h5p.json), serve embedded player assets, xAPI statement endpoint → score event | B2, B4 |
| B13 | **LTI 1.3** (FR-STD-03) | `LtiTool` (Admin registration), `LtiLaunchLink`, `LtiLineItem` | tool registration (client-level, Org Admin), OIDC launch + JWT signing/verification, deep-link optional, Assignment & Grade Services (line items, score passback) → grade events | B3 |
| B14 | **M4 isolation probes + docs** | – | probe rows per new endpoint (`ProbeRows/`), every new table gets an isolation test; hand-over of the grade-event contract to M5 | all |

Suggested lanes if four backend agents are available: BE-A B1 → B2 → B3 → B4; BE-B B5; BE-C B6 → B7; BE-D B8 → B9, then B10 / B11 / B12 / B13 as capacity frees. B1–B3 are the critical path (everything else waits for them), so they should be single-owner and land first; B5/B6/B8/B9/B10 can start as soon as B3's module payload shape is merged (they plug in a new `RefKind`).

## 4. Cross-cutting rules the proposal relies on

1. **Isolation:** every new table carries `OrganizationId`; class-bound tables (modules, assignments, quizzes, submissions, forum, live sessions) also `OrgUnitId` + `OrgUnitPath` copied from the class (same pattern as `ClassLessonPlan`). Library resources are unit-scoped at the owning unit with inheritance downwards (reads via the subtree filter, no bypass). Student-owned rows (submissions, attempts, completions) are visible to the student only for themselves.
2. **Relationship rules (D42):** the built-in Teacher reaches only own ClassCourses (`RelationshipAccess.CheckClassCourseAsync`); Student only own enrollments; Guardian none in M4 (grades are M5). Content permissions on custom roles are unrestricted inside the unit scope, as for M3.
3. **Closed class / year:** writes use `PlacementGuard.EnsureWritableAsync` (409 `class_closed` / `year_closed`); students cannot submit into a closed year.
4. **Locked plan:** teacher modules are the only writes under a class lesson; blueprint-pushed lessons stay locked; the push job (M3 #225) already keeps teacher modules (it never touches `LessonModule`).
5. **Files:** all uploads go through B2 (type/size limits per client, MinIO bucket per client, signed URLs after a permission check on the owning module).
6. **Background work:** due-soon reminders, quiz auto-submit, live-session reminders, SCORM/H5P package validation run as Hangfire system jobs through `IBackgroundJobs.EnqueueAsSystem`.
7. **Audit:** `[Audited]` on modules, assignments (including late-policy edits), quizzes and settings, grade events; submissions/answers are not audited row by row (volume) — only teacher grading actions are.
8. **Bilingual:** titles/instructions stored as `…Ar/…En` where authored by staff; page content is a single rich-text field per language pair.

## 5. Permissions to add (for the Lead's contract PR; existing `Assignments.*` is the role-assignment permission, so the activity module needs its own names)

| Group | Constants | Default grant (matrix row "Content & activities") |
|---|---|---|
| `Modules` | `View`, `Manage` (lesson modules, completion marks use `View`) | Org Admin/Manager/Supervisor View; Teacher Manage (own class courses); Student View |
| `CourseAssignments` (not `Assignments`, which already means role assignment) | `View`, `Manage`, `Submit`, `Grade` | Teacher Manage + Grade (own); Student View + Submit |
| `Quizzes` | `View`, `Manage`, `Attempt`, `Grade` | Teacher Manage + Grade (own); Student View + Attempt |
| `Library` | `View`, `Manage` | all staff View; Supervisor/Org Admin/Teacher Manage |
| `Forums` | `View`, `Post`, `Moderate` | Student/Teacher Post; Teacher Moderate (own); Org Admin Moderate |
| `LiveSessions` | `View`, `Manage` | Teacher Manage (own); Student View |
| `Packages` | `Upload`, `View` (SCORM/H5P) | Teacher Upload; Student View |
| `LtiTools` | `View`, `Manage` (tool registration), `Launch` | Org Admin Manage; Teacher/Student Launch |
| Assets | **no new group**: reuse `Files.Upload` / `Files.Download`; the signed URL is additionally checked against the owning module's `Modules.View` (and the student's visibility) | – |

This is the same set as FE1's proposal (#341 §1), adopting its names (`Modules`, `CourseAssignments`, `Packages`, `LtiTools`); the one difference is that assets reuse `Files.*`.

Also needed: settings (`Files.AllowedTypes`, `Files.MaxSizeMb`, `Assignments.LatePolicyDefault`, `Quizzes.AutoSubmitGraceSeconds`), audit entity types, notification events (`Assignment.Published/DueSoon/Graded`, `Quiz.Opened`, `Forum.Announcement`, `Live.Reminder`), problem codes (`submission_closed`, `attempt_limit_reached`, `quiz_closed`, `package_invalid`, `asset_type_not_allowed`, …) and route constants. The contract PR (T-contract) should land before B1.

## 6. Open questions for the Team Lead / product owner

1. **Grade carry-over seam:** D44 defers grade carry-over to M5. Is the proposed `GradeEvent` outbox the agreed seam, or should M5 own the grade-item model and M4 write grade entries directly? (The outbox keeps M4 shippable on its own.)
2. **Rich text:** Page modules need HTML sanitising. Is a server-side allow-list sanitiser (HtmlSanitizer) acceptable as a new package, and is a client-side editor (FE choice) in scope for M4?
3. **Video:** signed-URL streaming of the uploaded file only (no transcoding/HLS) for v1?
4. **SCORM/H5P hosting:** serve package content from the API under a sandboxed path with strict CSP, or from MinIO with a separate origin? The first is simpler to isolate per tenant; the second scales better.
5. **LTI 1.3 scope:** v1 = tool launch + score passback only (no deep linking, no names/roles provisioning)? Needs a per-client key pair; where do private keys live (the existing key-protection runbook)?
6. **Zoom credentials:** one set per client (Org Admin enters them) or one platform app? The proposal assumes per client in `MeetingProviderAccount`, secrets protected like the IdP client secrets.
7. **Quiz question pools:** "pool per quiz" read as: a quiz may hold more questions than it shows and draws N at random per attempt. Confirm.
8. **Targeting a group** (FR-CNT-02): confirm that group-targeted modules are visible only to members of that group (and to teachers), and that the "one group per class course" rule (D6) makes the check a single lookup.
9. **Size:** M4 is the largest milestone (11 DEV-PLAN items, 14 backend tasks here). Suggest the Lead considers splitting it into M4a (B1–B9) and M4b (B10–B13) so the exit demo does not wait for Zoom, SCORM, H5P and LTI.

## 7. Risks

- **SCORM/H5P/LTI are security-heavy** (untrusted zips and third-party JS, JWT validation): each needs a QA security review, not only an isolation probe; zip-slip, decompression bombs and cross-tenant path access are the named threats.
- **Probe-row pollution:** more modules means more shared-world probe rows; the class probe rows already hit an order-dependence (fixed in #335). New rows must not create years/classes at levels that shadow others.
- **Quiz timing** relies on the server clock (never the client's) and on idempotent submit; attempts need row-level locking like the enrollment capacity lock.
- **Volume:** submissions, answers, tracking commits grow fastest; indexes by (ClassCourseId, StudentId) and server-side paging from the start (X-09).

## 8. Alignment with the frontend proposal (PR #341, section 4)

| # | FE point | Backend position |
|---|---|---|
| 1 | Plan DTO: nested vs separate | **Nested.** `ClassLessonDto` gets `Modules` (id, kind, title, order, visibility, completion state for the caller); one read for the tree. Plus `GET /api/modules/{id}` for the viewer (typed payload, asset url, `serverNow`). |
| 2 | Visibility filtering | **Backend.** For the Student role the tree drops hidden modules, modules outside their `from/to` window and modules targeted at a group they are not in; teachers see everything with a flag. Implemented once in `IModuleAccess` (B1) and reused by every student read. |
| 3 | Asset flow | **v1: streamed through the API** (`POST /api/assets` multipart; the existing `IFileStorage.UploadAsync(Stream)` enforces type and size). Limits come from `GET /api/assets/limits` (`allowedTypes`, `maxBytes`) so the FE validates up front. Direct-to-MinIO presigned upload (init → PUT → complete) is a later optimisation for very large videos; the FE should hide the transport behind one `IAssetUploader`. |
| 4 | Server time | Every timed response carries `serverNow` (UTC) next to the dates it compares (assignment due, quiz attempt `endsAt` and `remainingSeconds`, live join window, scheduled visibility). Deadlines are always judged server-side. |
| 5 | Problem codes | Added to the contract PR: `module_visibility_range_invalid`, `late_policy_invalid`, `submission_closed`, `resubmission_not_allowed`, `quiz_closed`, `attempt_limit_reached`, `quiz_has_attempts` (edit of a correct answer after the first attempt, Q5), `package_invalid`, `asset_type_not_allowed`, `asset_too_large`, `module_target_invalid`. Each gets an `Error_{code}` resource pair on both sides. |
| 6 | Completion API | `POST /api/modules/{id}/completion` (manual mark, idempotent) and automatic completion from events (opened for View rules, submission, score at or above the minimum); read models `GET /api/class-courses/{id}/progress` (the caller's own) and `progress` fields on each lesson and module node. |
| Q1 | Granularity | Agreed (section 5 table). |
| Q2 | Rich text | Backend sanitises on write with an allow-list sanitiser (new package `HtmlSanitizer`; the Lead approves the dependency). One shared `RichTextEditor` on the FE as proposed. |
| Q3 | Teacher progress view | M5 as proposed; the M4 progress endpoint already supports a teacher reading a student's progress by id, so no rework later. |
| Q5 | Edit after attempts | Backend rejects (`quiz_has_attempts`), no regrade tooling. |
| Q6 | SCORM/H5P hosting | Agreed in principle; section 6 question 4 stays open until the spike and a QA security look. |
