# M4 Teaching & Learning — backend task proposal (DRAFT for Team Lead review)

Status: **proposal, updated for the owner decisions D48-D51** (rich text editor + sanitising, one platform Zoom app, LTI out of v1, M4 split into M4a and M4b). Written by Backend developer 4; the Team Lead turns it into the approved M4 plan, the contract PR (`src/Lms.Shared`) and the issues. Companion of the frontend proposal (PR #341). Sources: PRD §6.6 (FR-LP-05), §6.8 (FR-CNT, FR-ASG, FR-QZ, FR-LIB, FR-FOR, FR-VC, FR-STD-01/02), D44, D48-D51, DEV-PLAN M4, PRD §3.4 matrix row "Content & activities". FR-STD-03 (LTI 1.3) is out of v1 (D50).

## 1. What M4 has to deliver (exit demo)

**M4a** (content modules, assignments, quizzes, forums; plus the digital library): a teacher builds lessons with the common module types, a student completes content, submits an assignment and takes a quiz. **M4b** (Zoom live sessions, SCORM, H5P) follows. The M5 gradebook is not here, but **M4 emits grade events** (assignment graded, quiz attempt scored, SCORM/H5P score) so M5 can consume them without reworking M4 (see section 8; the outbox is the agreed seam, D51).

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

### M4a (content modules, assignments, quizzes, forums, library)

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
| B13 | **M4a isolation probes + docs** | – | probe rows per new M4a endpoint (`ProbeRows/`), an isolation test per new table, hand-over of the grade-event contract to M5 | B1-B9 |

### M4b (Zoom live sessions, SCORM, H5P)

| # | Task | Entities (new) | Endpoints (sketch) | Depends on |
|---|---|---|---|---|
| B10 | **Virtual classroom** (FR-VC-01) | `LiveSession`, `LiveSessionSeries` | `IMeetingProvider` abstraction, Zoom server-to-server OAuth impl using **one platform app** (D49: `ZoomOptions` bound from server configuration / secrets, never stored in the database or the repo; sessions are created under the platform account and tagged with the class course), create single/recurring, reminders (Hangfire), join link endpoint, participant import → attendance (M5 interface), recording link attach | B3 |
| B11 | **SCORM 1.2/2004** (FR-STD-01) | `ScormPackage`, `ScormSco`, `ScormTracking` | upload + manifest validation (zip hardening: size, entry count, path traversal), launch-url hosting under a sandboxed origin path, runtime API endpoint for tracking commits, completion/score → events | B2, B4 |
| B12 | **H5P** (FR-STD-02) | `H5pContent` | upload + validation (h5p.json), serve embedded player assets, xAPI statement endpoint → score event | B2, B4 |
| B14 | **M4b isolation probes + docs** | – | probe rows for the Zoom, SCORM and H5P endpoints, an isolation test per new table, a security review checklist for package upload and the SCORM runtime | B10, B11, B12 |

The former B13 (LTI 1.3) is removed (D50).

Suggested lanes for M4a with four backend agents: BE-A B1 -> B2 -> B3 -> B4; BE-B B5; BE-C B6 -> B7; BE-D B8 -> B9; B13 closes M4a. B1-B3 are the critical path (everything else waits for them), so they should be single-owner and land first; B5/B6/B8/B9 can start as soon as the module payload shape of B3 is merged (they plug in a new `RefKind`). M4b: B10 (Zoom), B11 (SCORM), B12 (H5P) are independent of each other once B3/B4 exist; SCORM and H5P wait for the QA hosting spike (D51); B14 closes M4b.

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
| Assets | **no new group**: reuse `Files.Upload` / `Files.Download`; the signed URL is additionally checked against the owning module's `Modules.View` (and the student's visibility) | – |

This is the same set as FE1's proposal (#341 §1), adopting its names (`Modules`, `CourseAssignments`, `Packages`); the one difference is that assets reuse `Files.*`.

Also needed: settings (`Files.AllowedTypes`, `Files.MaxSizeMb`, `Assignments.LatePolicyDefault`, `Quizzes.AutoSubmitGraceSeconds`), audit entity types, notification events (`Assignment.Published/DueSoon/Graded`, `Quiz.Opened`, `Forum.Announcement`, `Live.Reminder`), problem codes (`submission_closed`, `attempt_limit_reached`, `quiz_closed`, `package_invalid`, `asset_type_not_allowed`, …) and route constants. The contract PR (T-contract) should land before B1.

## 6. Decisions applied and what stays open

Applied from the owner (PRD D48-D51) and the Lead's technical defaults (D51):

1. **Rich text (D48):** one shared visual editor on the frontend; the backend sanitises HTML on write with an allow-list (`HtmlSanitizer`, approved) for Page modules, assignment instructions and forum posts.
2. **Zoom (D49):** one platform-level app; credentials in server configuration (environment / secret store), never in the database or the repo. No per-client Zoom settings or tables.
3. **LTI (D50):** out of v1; no LTI entities, endpoints, permissions or keys.
4. **Split (D51):** M4a then M4b as in section 3.
5. **Technical defaults accepted:** `GradeEvent` outbox as the M5 seam; video is a signed URL to the uploaded file (HTTP range, no HLS); a quiz pool draws N questions per attempt; group-targeted modules are visible only to that group and its teachers (D6 makes it a single membership lookup); SCORM/H5P content is served from a sandboxed origin after a QA spike.

Still open (small):

- **SCORM/H5P spike** (QA + backend): origin layout (separate host name vs sandboxed path with CSP), zip hardening limits (size, entry count, nesting), where tracking commits are rate limited. Needed before B11/B12 are sized.
- **Zoom account capacity:** with one platform app all clients share one Zoom account's licences, concurrent-meeting and API rate limits. The Lead should confirm the Zoom plan before B10; B10 will queue and rate-limit calls and surface `provider_unavailable` instead of failing silently.
- **Asset size limits per client** (defaults for documents, audio, video) to seed `Files.MaxSizeMb`: proposal 25 MB documents, 100 MB audio, 500 MB video.

## 7. Risks

- **SCORM/H5P are security-heavy** (untrusted zips and third-party JS): each needs a QA security review, not only an isolation probe; zip-slip, decompression bombs and cross-tenant path access are the named threats.
- **One platform Zoom app** (D49) shares licences and rate limits across all clients; a noisy client can starve others (see section 6).
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
| Q2 | Rich text | Decided (D48): backend sanitises on write with `HtmlSanitizer` (approved); one shared `RichTextEditor` on the FE. |
| Q3 | Teacher progress view | M5 as proposed; the M4 progress endpoint already supports a teacher reading a student's progress by id, so no rework later. |
| Q5 | Edit after attempts | Backend rejects (`quiz_has_attempts`), no regrade tooling. |
| Q6 | SCORM/H5P hosting | Sandboxed origin after a QA spike (D51); see section 6. |
