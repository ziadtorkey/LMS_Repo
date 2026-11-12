# M4a Teaching & Learning Implementation Plan

> **Status: DRAFT for owner review (2026-11-12).** Merges the frontend proposal (PR #341, FE1) and the backend proposal (PR #343, BE4) into one task list for **M4a** (D51). M4b (Zoom, SCORM, H5P) is listed at the end ("Later: M4b") and gets its own plan. Owner decisions applied: D48 (rich text), D49 (one platform Zoom app), D50 (no LTI in v1), D51 (split and Lead technical defaults). Lead decisions applied (PR #341/#343 comments): Quill 2.x self-hosted, `HtmlSanitizer` on the server, completion route `/completion` (GET/PUT). Open questions are at the end; each has a recommended default the plan already assumes. Team: Lead, BE1–BE4, FE1, FE2 (+ FE3 proposed), QA1–QA3, UI/UX Designer.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. In this project each task = one GitHub issue and one PR; the lane (Lead contract / BE1–BE4 / FE1–FE3 / QA1–QA3) is marked on each task.

**Goal:** A teacher builds lessons with modules under the lessons of the class lesson plan of their own ClassCourses (File, Video, Audio, Page, Link, Assignment, Quiz, Library resource), each with visibility (published / hidden / scheduled), target (whole ClassCourse or a group) and a completion rule; a student opens content, completes it (manual or automatic) and sees progress per lesson and course; submits an assignment (file and/or text, late policy, resubmission) that the teacher grades with feedback and an annotated file; takes a timed, autosaved quiz that is auto-graded (essays graded by hand) and auto-submitted at close; staff manage a digital library whose resources become lesson modules; each ClassCourse has a forum with moderation and course announcements. Every score is written to a grade-event outbox that M5 reads. All bilingual (ar RTL / en), isolated per client, subtree and relationship.

**Architecture:** Same modular monolith (`Features/<Module>/` + `<Module>Module.cs`; entities in `Lms.Domain/<Module>/`; persistence and services in `Lms.Infrastructure/<Module>/`; contracts in `Lms.Shared` through Lead PRs). M4a adds one kernel the other features plug into: the **module kernel** (Task 3): `LessonModule` (common metadata) + `ModuleContent` (payload of the simple kinds) + `IModuleAccess` (the one visibility / target / relationship resolver every student-facing read uses). Feature kinds (assignment, quiz, library resource) own their tables and point the module at them through `LessonModule.RefId`. Files go through the **asset service** (Task 4) on top of the M0 `IFileStorage`. Completion (Task 6) is one table plus read-time rollups. Scores go to the **grade-event outbox** (Task 7, D51), the only seam M5 consumes.

**Tech Stack:** unchanged from M3 plus two approved packages: **`HtmlSanitizer`** (server, allow-list sanitising of rich text, D48) and **Quill 2.x** (BSD-3-Clause, vendored under `src/Lms.Client/wwwroot/lib/quill/`, never from a CDN). Hangfire for due-soon reminders and quiz auto-submit; MinIO signed URLs with HTTP range for audio/video (no transcoding, no HLS, D51).

**Spec:** `docs/PRD.md` — §3.3 (relationship rules), §3.4 row "Content & activities", §5 X-01, X-02, X-05, X-07, X-09, X-10, §6.6 FR-LP-05, §6.8 FR-CNT-01..04, FR-ASG-01/02, FR-QZ-01..03, FR-LIB-01, FR-FOR-01, decisions D6, D13, D42, D44, D48–D51. Master plan: `docs/DEV-PLAN.md` §2 "M4 Teaching & Learning" items 1–7 (items 8–10 are M4b; item 11, LTI, is out of v1 per D50). Proposals: `docs/superpowers/plans/2026-11-14-m4-frontend-proposal.md` (PR #341), `docs/superpowers/plans/2026-11-14-m4-backend-proposal.md` (PR #343). Previous plan: `2026-11-13-m3-academic-structure.md`.

## What M4a builds on (checked on `origin/main` @ `39c1172`, 2026-11-12)
- **Merged:** academic kernel #265 (`ClassCourse`, `ClassCourseTeacher`, `CourseEnrollment`, `StudentGroup`/`GroupMember` with the D6 one-group rule #306), relationship scopes #298 (`IRelationshipScope`, `RelationshipAccess.CheckClassCourseAsync`), enrollment engine #284, classes #277, files (M0 `IFileStorage`, `/api/files`, setting `Files.MaxUploadMb`), notifications (`INotificationPublisher`), jobs (`IBackgroundJobs.Enqueue` / `EnqueueAsSystem`), `PlacementGuard.EnsureWritableAsync` (409 `class_closed` / `year_closed`).
- **Still open, needed first:** class lesson plans BE #325 (#225, `ClassLessonPlan` / `ClassSection` / `ClassLesson` with stable lesson keys and `GET /api/class-courses/{id}/lesson-plan`), plan view FE #339 (#243, `LessonPlanTree`), blueprints BE #323, design pilot #334 (`docs/DESIGN.md`, `Components/Ui/*`, `.claude/skills/lms-ui`). Tasks 3 and 15 wait for #325 / #339; FE tasks after Task 15 assume #334 is merged.
- **Contract wave 1 is drafted:** PR "feat(contracts): M4a contracts, wave 1" on `lead/m4a-contracts` (Task 1).

## Global Constraints
- **Every M0–M3 Global Constraint still applies** (by reference: `X-Lms-Context`, Guid v7, utf8mb4, default culture `ar`, explicit permissions per built-in role, `UnitAccess.CheckUnitAsync` with 404 for invisible units, ProblemDetails `code` from `ProblemCodes`, `// ISOLATION-BYPASS: <reason>` + allow-list entry, no new `DbSet` properties, two `Program.cs` lines per module, one migration per PR with a working `Down()`, `AuditableEntity` + `[Audited]` + an isolation test per business entity, names 1–200 chars both languages, paging max 200, probe rows per module in `tests/Lms.IntegrationTests/Security/ProbeRows/` in the same PR as the route, FE API interface + fake per module, ar/en resource parity, RTL screenshots; M3: relationship scopes, Closed year / Closed class read-only, live keys for "among live rows" uniqueness, side effects after commit).
- **Isolation shapes (all `IOrgUnitScoped`, unit fields copied from the class of the ClassCourse, re-pathed by the M1 repather):** `LessonModule`, `ModuleContent`, `Asset`, `ModuleCompletion`, `GradeEvent`, `Assignment`, `AssignmentAttachment`, `Submission`, `SubmissionFile`, `SubmissionFeedback`, `Quiz`, `QuizQuestion`, `QuizOption`, `QuizAttempt`, `QuizAnswer`, `ForumThread`, `ForumPost`, `CourseAnnouncement`. `LibraryResource` / `LibraryTag` are unit-scoped at the **owning unit** and read through the subtree filter downward (inherited resources are visible below, read-only; no bypass). The `Repather_covers_*` coverage test must stay green.
- **Relationship rules (D42, contract `RelationshipRules`):** the built-in Teacher reaches only own ClassCourses for `Modules.*` and the wave 2 activity permissions; the built-in Student only own active enrollments; Guardian has no M4a access (grades arrive in M5). Every endpoint that touches a restricted permission calls `RelationshipAccess.CheckClassCourseAsync` (404 on a miss, lists filter). Custom roles are unrestricted inside their unit scope.
- **One visibility resolver:** every student-facing read (plan tree modules, module detail, asset URL, assignment, quiz, forum announcement) goes through `IModuleAccess` (Task 3). A student never receives a hidden module, a scheduled module outside its window, or a module targeted at a group they are not in (D51); a miss is 404. Staff receive everything with `IsVisibleNow`. The FE never filters for security.
- **Server time:** every timed response carries `ServerNowUtc` next to the dates it compares (schedule windows, due dates, attempt `EndsAtUtc` / `RemainingSeconds`). Deadlines, late states, attempt limits and auto-submit are judged on the server clock only.
- **Rich text (D48):** Page bodies, assignment instructions, forum posts and announcements are HTML sanitised **on write** with one shared allow-list (`IRichTextSanitizer`, Task 3): p, br, strong, em, u, s, h2–h4, ol, ul, li, blockquote, a[href http/https/mailto, forced `rel="noopener noreferrer"` `target="_blank"`], span/p with `dir` and `class="ql-align-*|ql-direction-rtl"`; no img, iframe, style, script, event attributes, `javascript:` / `data:` URLs. Max 200 000 characters per body. The client renders only server-sanitised HTML through `SanitizedHtml` (Task 14).
- **Assets:** uploads go through `POST /api/assets` (Task 4): extension and declared content type must be in `AssetTypes.All` (contract; no SVG/HTML), size ≤ the effective `Files.MaxUploadMb` at the ClassCourse's unit (400 `asset_type_not_allowed` / `asset_too_large`). A signed URL (10 min) is issued only after `Files.Download` **and** a check that the caller can see a module (or assignment, submission, forum post, library resource) that references the asset, or manages its ClassCourse. Video/audio served with HTTP range.
- **Closed class / year:** all writes (modules, assets, submissions, attempts, posts) go through `PlacementGuard.EnsureWritableAsync`; reads stay available.
- **Locked plan:** teacher modules are the only writes under a class lesson; class sections/lessons stay locked (M3); the blueprint push job never touches `LessonModule`. A module under a lesson that a later push hides stays attached and is hidden from students with its lesson.
- **Background jobs:** due-soon reminders (`AssignmentDueSoonJob`, recurring), quiz auto-submit at close (`QuizCloseJob`, scheduled per quiz + a recurring safety sweep) run as system jobs (`EnqueueAsSystem` / `AddOrUpdateRecurring`), loop over clients, never with an empty context, idempotent.
- **Audit:** `[Audited]` on `LessonModule`, `ModuleContent`, `Asset`, `Assignment` (incl. late-policy edits), `Quiz`, `QuizQuestion`, `LibraryResource`, `ForumThread` moderation, `CourseAnnouncement`, `SubmissionFeedback`, `GradeEvent`; submissions, answers, completions and posts are not audited row by row (volume), teacher grading and moderation actions are.
- **Performance (X-09):** indexes `(ClassCourseId, StudentId)` on completions, submissions and attempts; `(ClassLessonId, Order)` on modules; all lists server-paged; the plan tree with 40 lessons × 10 modules for one student answers in < 300 ms on the Testcontainers MySQL (test in Task 6).
- **Migrations:** `AddLessonModules` (Task 3), `AddAssets` (Task 4), `AddModuleCompletions` (Task 6), `AddGradeEvents` (Task 7), `AddAssignments` (Task 8), `AddQuizzes` (Task 10), `AddLibrary` (Task 12), `AddForums` (Task 13). Whoever merges second regenerates on top of `main`.
- **FE:** API interfaces `IModulesApi`, `IAssetsApi`, `IProgressApi`, `ICourseAssignmentsApi`, `IQuizzesApi`, `ILibraryApi`, `IForumsApi`, each with HTTP implementation + fake (`Lms:UseFakeApi=true`). Follow `docs/DESIGN.md` (`PageHeader`, `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, `DetailItem`, 360 px labelled cards, logical CSS, ISO dates `yyyy-MM-dd`). Resource key prefixes per module: `Module_*`, `Asset_*`, `Content_*`, `Progress_*`, `Assignment_*`, `Quiz_*`, `Library_*`, `Forum_*`, `RichText_*`. Files under ~300 lines (split dialogs, tabs, per-kind editors/viewers).

## Review Focus
1. **Visibility bypass:** a student reading a hidden, out-of-window or other-group module (tree, `GET /api/modules/{id}`, asset URL, assignment, quiz) gets 404, never the payload; a teacher of another ClassCourse gets 404 on modules, assets, submissions, attempts and posts; a School-2 admin cannot see School-1 content. (Tests in Tasks 3, 4, 5, 8, 10, 13; probes in Task 29.)
2. **XSS through rich text or files:** sanitiser strips script, event handlers, `javascript:`/`data:` URLs, iframes, styles; `SanitizedHtml` never renders client-produced HTML; SVG/HTML uploads refused; Link modules accept http/https only. (Tests in Tasks 3, 4, 14, 18; review in Task 30.)
3. **Deadline and attempt integrity:** due/late, resubmission, attempt limits, time limits and auto-submit judged by the server clock; concurrent submit/start requests cannot create two submissions or exceed the attempt limit (row lock like the enrollment capacity lock); answers after `EndsAtUtc` are refused. (Tests in Tasks 8, 9, 11.)
4. **Grades and correct answers leak:** a student never receives correct options or other students' answers before the review policy allows; essay grades and feedback only after release; grade events written exactly once per scored thing (idempotent). (Tests in Tasks 7, 9, 11.)
5. **Asset access by guessing ids:** an asset id from another ClassCourse, another student's submission file or a library resource with download disabled never yields a signed URL (the protected reader streams instead). (Tests in Tasks 4, 9, 12; probes in Task 29.)

---

### Task 1: Contract — wave 1: Modules permissions, module / asset / completion / progress DTOs, routes, codes — lane: Lead contract

**PRD:** §3.4 "Content & activities", FR-LP-05, FR-CNT-01..04, X-05, D48, D50, D51. **Depends on:** M3 contracts #259. **Status:** draft PR on `lead/m4a-contracts` ("feat(contracts): M4a contracts, wave 1").

**Files:**
- Modify: `src/Lms.Shared/Authorization/Permissions.cs`, `BuiltInRoles.cs`, `RelationshipRules.cs`, `src/Lms.Shared/ProblemCodes.cs`, `src/Lms.Shared/Auditing/AuditCatalog.cs`, `src/Lms.Shared/Classes/LessonPlanDtos.cs`, `src/Lms.Infrastructure/Access/BuiltInRoleDefaults.cs` (version 4), `src/Lms.Client/Resources/SharedResource.{ar,en}.resx`
- Create: `src/Lms.Shared/Content/ModuleCatalogs.cs`, `ModuleDtos.cs`, `AssetDtos.cs`, `ProgressDtos.cs`, `src/Lms.Shared/Routes.Content.cs`
- Test: `tests/Lms.UnitTests/Contracts/ContentContractTests.cs` (new), `BuiltInRoleMatrixTests.cs`, `AuthorizationContractTests.cs`, `AcademicCatalogContractTests.cs`, `AccessContractTests.cs`, `tests/Lms.UnitTests/Access/BuiltInRoleDefaultsTests.cs`, `tests/Lms.IntegrationTests/Access/BuiltInRoleReconcilerTests.cs`

**Interfaces:**
- Permissions: `Modules.{View, Manage}`. Matrix: OrgAdmin + Manager `View, Manage` (§3.4 says View; Manage is needed so the D16 escalation guard still lets them assign the Teacher role — owner Q1), Supervisor `View`, Teacher `View, Manage`, Student `View`; Counselor and Guardian none. `RelationshipRules`: Teacher/`OwnClassCourses` + `Modules.View, Modules.Manage`; Student/`OwnEnrollments` + `Modules.View`. Assets reuse `Files.Upload` / `Files.Download` (no `Assets` group).
- Catalogs: `ModuleKinds.All = [File, Video, Audio, Page, Link, Assignment, Quiz, LibraryResource, LiveSession, Scorm, H5p]` (no LTI, D50), `ModuleKinds.Content = [File, Video, Audio, Page, Link]`; `ModuleVisibilities {Published, Hidden, Scheduled}`; `ModuleTargets {ClassCourse, Group}`; `CompletionRules {None, Manual, View, Submit, Score}`, `CompletionRules.ForContent = [None, Manual, View]`; `CompletionStates {NotStarted, InProgress, Completed}`; `AssetKinds {Document, Image, Audio, Video}` + `AssetKinds.AcceptedBy(moduleKind)`; `AssetTypes.All` (pdf, docx, xlsx, pptx, txt, png, jpg, jpeg, gif, webp, mp3, m4a, ogg, wav, mp4, webm) + `ForFileName`; `AssetUploadForm {file, classCourseId}`.
- DTOs: `ModuleNodeDto`, `ModuleContentDto`, `ModuleDetailDto` (incl. `PreviousModuleId`, `NextModuleId`, `CanEdit`, `ServerNowUtc`), `ModuleContentInput`, `CreateModuleRequest`, `UpdateModuleRequest`, `ReorderModulesRequest`, `MoveModuleRequest`; `AssetDto`, `AssetTypeDto`, `AssetLimitsDto`, `AssetUrlDto`; `ProgressDto`, `ModuleCompletionDto`, `LessonProgressDto`, `CourseProgressDto` (incl. `NextModuleId`); `ClassLessonDto` + optional `Modules`, `Progress`; `ClassLessonPlanDto` + optional `Progress`, `ServerNowUtc`.
- Routes: `/api/class-courses/{id}/lessons/{lessonKey}/modules` (GET list, POST create), `…/modules/order` (PUT), `/api/modules/{id}` (GET, PUT, DELETE), `/api/modules/{id}/move` (POST), `/api/modules/{id}/completion` (GET, PUT), `/api/class-courses/{id}/progress[?studentId=]` (GET), `/api/assets` (POST multipart), `/api/assets/limits?classCourseId=` (GET), `/api/assets/{id}` (GET), `/api/assets/{id}/url` (GET).
- ProblemCodes: `module_visibility_range_invalid`, `module_target_invalid`, `asset_type_not_allowed`, `asset_too_large`, `completion_not_manual` (each with `Error_{code}` ar/en). AuditCatalog: `LessonModule`, `ModuleContent`, `Asset`.

- [x] **Step 1: Write the failing tests** (`ContentContractTests`; matrix, catalog, defaults-version 4, relationship and reconciler tests extended). **Step 2–4** FAIL → implement → PASS; 0 warnings. **Step 5: Commit** `feat(contracts): M4a wave 1 - module permissions, content DTOs, routes and codes`.

### Task 2: Contract — wave 2: assignments, quizzes, library, forums — lane: Lead contract

**PRD:** FR-ASG-01/02, FR-QZ-01..03, FR-LIB-01, FR-FOR-01, D13, D48, D51. **Depends on:** Task 1 merged. Opened when Task 3's PR is in review (so `RefId` semantics are proven), merged before Tasks 8, 10, 12, 13 and FE Tasks 20–26 start.

**Files:** Modify the Task 1 set plus `src/Lms.Shared/Notifications/NotificationEvents.cs`, `src/Lms.Shared/Configuration/SettingKeys.cs`, `SettingCatalog.cs`; Create `src/Lms.Shared/Assignments/CourseAssignmentDtos.cs`, `src/Lms.Shared/Quizzes/QuizDtos.cs`, `QuizCatalogs.cs`, `src/Lms.Shared/Library/LibraryDtos.cs`, `src/Lms.Shared/Forums/ForumDtos.cs`, `Routes.Activities.cs`; tests `ActivityContractTests.cs`.

**Interfaces (to finalise in the PR):**
- Permissions (defaults version 5): `CourseAssignments.{View, Manage, Submit, Grade}` (not `Assignments.*`, which means role assignments), `Quizzes.{View, Manage, Attempt, Grade}`, `Library.{View, Manage}`, `Forums.{View, Post, Moderate}`. Matrix: Teacher `View, Manage, Grade` / `Moderate` (own); Student `View, Submit` / `View, Attempt` / `Forums.View, Post`; OrgAdmin + Manager the staff side (View, Manage, Grade, Moderate — same D16 reason as Task 1); Supervisor View; Library: all staff View, OrgAdmin/Manager/Supervisor/Teacher Manage, Student View. `CourseAssignments.Submit` and `Quizzes.Attempt` join `Permissions.SelfService` (they act only on the caller's own submission/attempt) so the escalation guard still lets admins assign the Student role. RelationshipRules: Teacher/`OwnClassCourses` and Student/`OwnEnrollments` extended to every new class-course permission (not `Library.*`).
- Catalogs: `LatePolicies {Allow, Penalty, Block}`, `ResubmissionRules {None, UntilDue, UntilGraded}`, `SubmissionStatuses {Draft, Submitted, Late, Graded, Returned}`, `QuestionTypes {SingleChoice, MultipleChoice, TrueFalse, Essay}`, `GradingMethods {Highest, Last, Average}`, `ReviewPolicies {None, ScoreOnly, Answers, AnswersAndCorrect}`, `AttemptStatuses {InProgress, Submitted, AutoSubmitted, Graded}`, `LibraryResourceTypes {Book, Document, Video, Audio, Link, Other}`.
- DTOs: assignment (instructions html ar/en, attachments = asset ids, `DueAtUtc`, `MaxGrade`, late policy + `PenaltyPercent`, resubmission rule, target, publish now / `PublishAtUtc`), submission (text html, file asset ids, `IsLate`, status), grading (`Grade`, feedback html, annotated file asset id); quiz (settings, questions with options, pool `DrawCount`), attempt (`EndsAtUtc`, `RemainingSeconds`, `ServerNowUtc`, answers, autosave request with a client sequence number), result per review policy; library resource (title ar/en, type, level type ids, subject, tags, cover asset, file asset or URL, `DownloadAllowed`), reader URL; forum thread, post, moderation, announcement (`Pinned`, `Notify`).
- Settings: `Assignments.LatePenaltyDefaultPercent` (Int 0–100, default 10), `Quizzes.AutoSubmitGraceSeconds` (Int 0–300, default 30).
- Events: `Assignments.Published`, `Assignments.DueSoon`, `Assignments.Graded`, `Quizzes.Opened`, `Forums.Announcement` (students of the ClassCourse/group, named).
- ProblemCodes: `late_policy_invalid`, `submission_closed`, `resubmission_not_allowed`, `quiz_closed`, `quiz_not_open`, `attempt_limit_reached`, `attempt_expired`, `quiz_has_attempts`, `pool_draw_invalid`, `thread_locked`, `download_disabled`. AuditCatalog: `Assignment`, `SubmissionFeedback`, `Quiz`, `QuizQuestion`, `LibraryResource`, `ForumThread`, `CourseAnnouncement`, `GradeEvent`.
- Asset ownership widens: `AssetUploadForm.OrgUnitId` for library assets (owner unit instead of a ClassCourse).

- [ ] **Step 1–5** as Task 1. **Commit** `feat(contracts): M4a wave 2 - assignments, quizzes, library and forums`.

### Task 3: Module kernel — entities, visibility resolver, sanitiser, authoring API, plan tree nesting — lane: BE1

**PRD:** FR-LP-05, FR-CNT-01 (Page, Link), FR-CNT-02, D48, D51. **Depends on:** Task 1; M3 #325 (class lesson plans) merged. DEV-PLAN M4 item 2.

**Files:**
- Create: `src/Lms.Domain/Content/LessonModule.cs`, `ModuleContent.cs`, `IModuleAccess.cs`, `IRichTextSanitizer.cs`; `src/Lms.Infrastructure/Content/ModuleAccess.cs`, `RichTextSanitizer.cs` (HtmlSanitizer, allow-list above), `ModuleWriter.cs`, configurations; migration `AddLessonModules`; `src/Lms.Api/Features/Content/ContentModule.cs`, `ModuleEndpoints.cs`, `ModuleValidators.cs`
- Modify: the M3 lesson-plan read (`LessonPlanEndpoints` / its query) to nest `Modules` per lesson and fill `ServerNowUtc`
- Test: `tests/Lms.UnitTests/Content/RichTextSanitizerTests.cs`, `ModuleRulesTests.cs`; `tests/Lms.IntegrationTests/Content/ModuleAuthoringTests.cs`, `ModuleVisibilityTests.cs`, `ContentIsolationTests.cs`; `Security/ProbeRows/ContentProbeRows.cs`

**Interfaces:**
- `LessonModule` (Id, `ClassCourseId`, `ClassLessonId`, `LessonKey`, Kind, TitleAr/En, Order, Visibility, VisibleFromUtc, VisibleToUtc, Target, GroupId, CompletionRule, CompletionMinScore, `RefId`, unit fields, soft delete, `[Audited]`); `ModuleContent` (ModuleId, AssetId, BodyHtmlAr/En, Url, `[Audited]`).
- `IModuleAccess.VisibleToStudent(IQueryable<LessonModule>, Guid studentProfileId, DateTime nowUtc)` and `CanSeeAsync(moduleId)`: Published, or Scheduled with now in [from, to); lesson not hidden; Target ClassCourse or the student's group (one `GroupMember` lookup, D6); the student actively enrolled (relationship). Staff: `RelationshipAccess.CheckClassCourseAsync`.
- Endpoints: `GET …/lessons/{lessonKey}/modules`, `POST …` (Modules.Manage; kinds in `ModuleKinds.Content` only, others 400 `validation_failed`), `PUT …/order`, `GET/PUT/DELETE /api/modules/{id}`, `POST /api/modules/{id}/move`. In this task Page (sanitised bodies) and Link (absolute http/https, max 2 000) are fully supported; File/Video/Audio are accepted once Task 5 lands (until then 400 `validation_failed`). `GET /api/modules/{id}` returns `ModuleDetailDto` with previous/next in plan order among modules the caller can see.
- Plan tree: `ClassLessonDto.Modules` ordered; for students only `VisibleToStudent` modules with `CompletionState` (null until Task 6), staff all with `IsVisibleNow`.

- [ ] **Step 1: Write the failing tests** `Sanitizer_strips_script_event_handlers_javascript_and_data_urls`; `Sanitizer_keeps_lists_headings_links_and_rtl_direction`; `Sanitizer_forces_noopener_on_links`; `Teacher_adds_page_and_link_modules_under_own_lesson`; `Teacher_of_other_class_course_gets_404`; `Scheduled_requires_range_from_before_to` (`module_visibility_range_invalid`); `Group_target_must_be_group_of_this_class_course` (`module_target_invalid`); `Reorder_requires_exact_live_set`; `Move_to_lesson_of_other_plan_is_404`; `Student_tree_hides_hidden_out_of_window_and_other_group_modules`; `Student_get_of_invisible_module_is_404`; `Closed_class_rejects_module_writes`; `Blueprint_push_keeps_teacher_modules`; isolation tests for both tables. **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(content): lesson module kernel, visibility resolver and authoring`.

### Task 4: Asset service — upload, limits, metadata, signed URL with range — lane: BE1

**PRD:** FR-CNT-03, X-05. **Depends on:** Task 3 (for the module reference check; can start in parallel on the upload part). DEV-PLAN M4 item 1.

**Files:** Create `src/Lms.Domain/Content/Asset.cs`, `IAssetAccess.cs`; `src/Lms.Infrastructure/Content/AssetService.cs`, `AssetAccess.cs`, `MediaProbe.cs` (duration when readable; optional); migration `AddAssets`; `src/Lms.Api/Features/Content/AssetEndpoints.cs`. Test `tests/Lms.IntegrationTests/Content/AssetTests.cs`, `AssetAccessTests.cs`; probe rows.

**Interfaces:** `POST /api/assets` multipart (`file`, `classCourseId`; `Files.Upload` + Modules.Manage on that ClassCourse, or the caller is a student enrolled in it for later submission files) → `AssetDto`; `GET /api/assets/limits?classCourseId=` → `AssetLimitsDto` (effective `Files.MaxUploadMb`, `AssetTypes.All`); `GET /api/assets/{id}` → `AssetDto`; `GET /api/assets/{id}/url` → `AssetUrlDto` (10 min, range-capable, `Content-Disposition` inline for previewable types). `IAssetAccess.CanReadAsync(assetId)` = manager of the ClassCourse, or a visible module / own submission referencing it (extended by Tasks 8, 12, 13). Streams through `IFileStorage.UploadAsync(Stream)`; no presigned direct upload in v1.

- [ ] **Step 1: Tests** `Rejects_svg_html_and_unknown_extensions`; `Rejects_content_type_mismatch`; `Rejects_file_over_effective_limit` (`asset_too_large`); `Limits_follow_unit_setting`; `Signed_url_requires_reference_the_caller_can_see`; `Student_cannot_get_url_of_asset_in_hidden_module`; `Other_client_asset_id_is_404`; `Video_url_supports_range_requests`; isolation test. **Step 2–5** → commit `feat(content): asset service with limits and signed URLs`.

### Task 5: Asset-backed module kinds — File, Video, Audio — lane: BE1

**PRD:** FR-CNT-01 (File, Video, Audio), FR-CNT-03. **Depends on:** Tasks 3, 4.

**Files:** Modify `ModuleValidators.cs`, `ModuleWriter.cs`, `ModuleEndpoints.cs` (detail includes `AssetDto`), `AssetAccess.cs` (module reference). Test `tests/Lms.IntegrationTests/Content/AssetModuleTests.cs`.

**Interfaces:** File/Video/Audio require `Content.AssetId` of an asset of the same ClassCourse whose kind `AssetKinds.AcceptedBy(kind)` accepts (400 `asset_type_not_allowed`); deleting a module keeps the asset (soft delete; storage cleanup is M7). `ModuleDetailDto.Content.Asset` filled; the viewer then calls `/api/assets/{id}/url`.

- [ ] **Step 1: Tests** `Video_module_requires_video_asset`; `File_module_accepts_pdf_and_image`; `Asset_of_other_class_course_rejected`; `Student_detail_includes_asset_and_url_is_issued`. **Step 2–5** → commit `feat(content): file, video and audio modules`.

### Task 6: Completion and progress — lane: BE1

**PRD:** FR-CNT-04. **Depends on:** Task 3 (Task 5 for asset kinds in tests). DEV-PLAN M4 item 3.

**Files:** Create `src/Lms.Domain/Content/ModuleCompletion.cs`, `ICompletionRecorder.cs`; `src/Lms.Infrastructure/Content/CompletionRecorder.cs`, `ProgressQuery.cs`; migration `AddModuleCompletions`; `src/Lms.Api/Features/Content/CompletionEndpoints.cs`, `ProgressEndpoints.cs`. Test `tests/Lms.IntegrationTests/Content/CompletionTests.cs`, `ProgressPerformanceTests.cs`.

**Interfaces:** `ModuleCompletion` (ModuleId, StudentProfileId, ClassCourseId, State, CompletedAtUtc, Score; unique `(ModuleId, StudentProfileId)`). `ICompletionRecorder.RecordAsync(moduleId, studentId, CompletionSignal {Viewed, Marked, Submitted, Scored(score)})` — idempotent, the single writer, used by `GET /api/modules/{id}` (Viewed → View rule completes; any rule → InProgress), `PUT /api/modules/{id}/completion` (Manual only, else 409 `completion_not_manual`; staff 403), and later by assignments (Submitted) and quizzes (Scored ≥ `CompletionMinScore`). `GET /api/modules/{id}/completion` → caller's `ModuleCompletionDto`. `GET /api/class-courses/{id}/progress[?studentId=]` → `CourseProgressDto` (student: own, `studentId` ignored; staff with relationship: required, enrolled student else 404); rollups on read over modules visible to that student whose rule is not `None`; `NextModuleId`. Plan tree fills `CompletionState`, lesson and course `Progress` for students.

- [ ] **Step 1: Tests** `Manual_mark_is_idempotent`; `Mark_on_view_rule_is_409`; `Opening_view_rule_module_completes_it`; `Hidden_or_none_rule_modules_do_not_count`; `Progress_rolls_up_per_lesson_and_course`; `Teacher_reads_enrolled_student_progress_other_teacher_404`; `Next_module_is_first_incomplete_in_plan_order`; `Tree_with_400_modules_answers_under_300ms`. **Step 2–5** → commit `feat(content): module completion and progress`.

### Task 7: Grade-event outbox (M5 seam) — lane: BE1

**PRD:** FR-ASG-02 / FR-QZ-03 "grade flows to the gradebook" (consumed in M5), D51. **Depends on:** Task 1. Small; may run before Task 6 if BE1 is waiting on #325.

**Files:** Create `src/Lms.Domain/Grading/GradeEvent.cs`, `IGradeEvents.cs`; `src/Lms.Infrastructure/Grading/GradeEventWriter.cs`; migration `AddGradeEvents`; `docs/adr/00xx-grade-event-outbox.md`. Test `tests/Lms.IntegrationTests/Grading/GradeEventTests.cs`.

**Interfaces:** `IGradeEvents.RecordAsync(GradeEventInput(SourceKind {Assignment, Quiz}, SourceId, ClassCourseId, StudentProfileId, decimal Points, decimal MaxPoints, DateTime ScoredAtUtc, int Revision))` — append-only, unique `(SourceKind, SourceId, StudentProfileId, Revision)`, written in the same transaction as the grade; no read API in M4a (M5 reads the table). `[Audited]`.

- [ ] **Step 1: Tests** `Same_revision_twice_writes_once`; `Regrade_writes_next_revision`; `Event_carries_unit_fields_and_is_isolated`. **Step 2–5** → commit `feat(grading): grade event outbox for the M5 gradebook`.

### Task 8: Assignments — model, authoring, publish/schedule, student submission, late policy — lane: BE2

**PRD:** FR-ASG-01, FR-ASG-02 (submission part). **Depends on:** Tasks 2, 3, 4. DEV-PLAN M4 item 4.

**Files:** Create `src/Lms.Domain/Assignments/*` (`Assignment`, `AssignmentAttachment`, `Submission`, `SubmissionFile`), `src/Lms.Infrastructure/Assignments/*` (`AssignmentWriter`, `SubmissionService`, `LatePolicyEvaluator`), migration `AddAssignments`, `src/Lms.Api/Features/Assignments/*`. Test `tests/Lms.UnitTests/Assignments/LatePolicyEvaluatorTests.cs`, `tests/Lms.IntegrationTests/Assignments/AssignmentTests.cs`, `SubmissionTests.cs`; probe rows.

**Interfaces:** creating an assignment creates its `LessonModule` (Kind `Assignment`, `RefId`, rule `Submit` or `Score`) in one transaction; instructions sanitised; publish now or `PublishAtUtc` (module Scheduled from that time); student `POST …/submit` (text and/or files, assets owned by the student's ClassCourse), resubmission per rule, late computed server-side (`Allow` → `IsLate`, `Penalty` → `IsLate` + penalty applied at grading, `Block` → 409 `submission_closed`), one live submission per student (row lock; concurrent submit test), `Submitted` completion signal; `Assignments.Published` notification after commit.

- [ ] **Step 1: Tests** `Penalty_policy_requires_percent` (`late_policy_invalid`); `Block_policy_refuses_after_due`; `Late_flag_uses_server_clock`; `Resubmission_rule_enforced` (`resubmission_not_allowed`); `Concurrent_submits_create_one_submission`; `Group_targeted_assignment_hidden_from_other_group`; `Submit_completes_submit_rule_module`; isolation. **Step 2–5** → commit `feat(assignments): assignments and student submissions`.

### Task 9: Assignments — grading, feedback, annotated file, notifications, due-soon job — lane: BE2

**PRD:** FR-ASG-02 (grading part). **Depends on:** Tasks 7, 8.

**Files:** Create `SubmissionFeedback`, `GradingService`, `AssignmentDueSoonJob`, grading endpoints; tests `GradingTests.cs`, `DueSoonJobTests.cs`.

**Interfaces:** teacher lists submissions (paged, late badge, status), grades (0..MaxGrade, penalty applied for `Penalty`), feedback (sanitised), annotated file (asset), returns; `GradeEvent` revision per (re)grade; `Score` completion signal; `Assignments.Graded` to the student; `AssignmentDueSoonJob` (recurring hourly, system context, per client) notifies students without a submission 24 h before due, once.

- [ ] **Step 1: Tests** `Grade_above_max_is_400`; `Penalty_applied_on_late_submission`; `Grading_writes_grade_event_once_per_revision`; `Student_sees_feedback_only_after_return`; `Due_soon_notifies_once_and_skips_submitted`; `Other_teacher_cannot_grade`. **Step 2–5** → commit `feat(assignments): grading, feedback and due-soon reminders`.

### Task 10: Quiz model and builder API — lane: BE3

**PRD:** FR-QZ-01, FR-QZ-02 (settings). **Depends on:** Tasks 2, 3. DEV-PLAN M4 item 5.

**Files:** Create `src/Lms.Domain/Quizzes/*` (`Quiz`, `QuizQuestion`, `QuizOption`), `src/Lms.Infrastructure/Quizzes/QuizWriter.cs`, migration `AddQuizzes` (includes attempt tables for Task 11), `src/Lms.Api/Features/Quizzes/QuizBuilderEndpoints.cs`. Tests `QuizBuilderTests.cs`.

**Interfaces:** quiz creates its `LessonModule` (Kind `Quiz`, rule `Score` default); questions MCQ single/multi, true/false, essay with points; pool: `DrawCount` ≤ question count (`pool_draw_invalid`); settings time limit, attempts, grading method, shuffle questions/options, open/close, review policy; editing a correct option or points after the first attempt → 409 `quiz_has_attempts` (no regrade tooling, Q5); preview-as-student read without creating an attempt.

- [ ] **Step 1: Tests** `Single_choice_needs_exactly_one_correct`; `Multi_choice_needs_two_options_and_one_correct`; `Draw_count_cannot_exceed_pool`; `Close_before_open_rejected`; `Edit_after_attempt_is_409`; `Student_cannot_read_builder`. **Step 2–5** → commit `feat(quizzes): quiz model and builder`.

### Task 11: Quiz attempts, autosave, auto-grading, manual essay grading, auto-submit — lane: BE3

**PRD:** FR-QZ-02 (auto-submit at close), FR-QZ-03. **Depends on:** Tasks 7, 10.

**Files:** Create `QuizAttempt`, `QuizAnswer`, `AttemptService`, `AutoGrader`, `QuizCloseJob`, attempt + grading endpoints. Tests `AttemptTests.cs`, `AutoSubmitTests.cs`, `EssayGradingTests.cs`.

**Interfaces:** start (open window, attempts left; row lock; snapshot of drawn and shuffled questions; `EndsAtUtc` = min(start + limit, close)); autosave per answer with a client sequence (last write wins by sequence; after `EndsAtUtc` + grace → 409 `attempt_expired`); submit (idempotent); auto-grade objective questions; essays pending until graded; final score by grading method → `GradeEvent` + `Score` completion signal; `QuizCloseJob` auto-submits unfinished attempts at close (scheduled + recurring sweep, idempotent); results per review policy (never correct answers before allowed).

- [ ] **Step 1: Tests** `Attempt_limit_enforced_under_concurrency`; `Timer_is_server_side_and_late_save_is_409`; `Autosave_keeps_highest_sequence`; `Objective_questions_auto_graded`; `Essay_pending_until_teacher_grades`; `Unfinished_attempts_auto_submitted_at_close`; `Highest_last_average_methods`; `Review_policy_hides_correct_answers`; `Grade_event_written_once_per_final_score`. **Step 2–5** → commit `feat(quizzes): attempts, grading and auto-submit`.

### Task 12: Digital library — lane: BE4

**PRD:** FR-LIB-01, D13. **Depends on:** Tasks 2, 4 (asset owner = unit), 3 (library module kind).

**Files:** Create `src/Lms.Domain/Library/*` (`LibraryResource`, `LibraryTag`), `src/Lms.Infrastructure/Library/*`, migration `AddLibrary`, `src/Lms.Api/Features/Library/*`. Tests `LibraryTests.cs`, `LibraryReaderTests.cs`.

**Interfaces:** CRUD for `Library.Manage` at the owning unit; search/filter by type, level type, subject, tags (paged); visible at the owning unit and below (subtree filter) and only for matching levels for students; reader endpoint streams through the API when `DownloadAllowed = false` (no signed URL, `Content-Disposition: inline`, 403 `download_disabled` for the download route); "add to lesson" creates a `LibraryResource` module referencing it (rule Manual/View).

- [ ] **Step 1: Tests** `Resource_visible_below_owner_not_in_sibling_school`; `Student_sees_only_matching_levels`; `Download_disabled_gives_no_signed_url`; `Add_to_lesson_creates_module`; `Filters_compose`; isolation. **Step 2–5** → commit `feat(library): digital library with protected reader`.

### Task 13: Forums and course announcements — lane: BE4

**PRD:** FR-FOR-01. **Depends on:** Tasks 2, 4. DEV-PLAN M4 item 7.

**Files:** Create `src/Lms.Domain/Forums/*`, `src/Lms.Infrastructure/Forums/*`, migration `AddForums`, `src/Lms.Api/Features/Forums/*`. Tests `ForumTests.cs`, `AnnouncementTests.cs`.

**Interfaces:** one forum per ClassCourse (implicit); threads, replies, attachments (assets); posts sanitised; moderation (`Forums.Moderate`): delete (soft), lock (409 `thread_locked` on reply), pin; announcements per ClassCourse (pin, `Notify` → `Forums.Announcement` to enrolled students after commit); students post only in own enrolled ClassCourses.

- [ ] **Step 1: Tests** `Locked_thread_refuses_replies`; `Pinned_first_then_latest_activity`; `Student_cannot_moderate`; `Other_class_course_student_404`; `Announcement_notifies_enrolled_students_once`; `Post_html_is_sanitised`; isolation. **Step 2–5** → commit `feat(forums): course forums and announcements`.

---

### Task 14: Shared rich-text editor (Quill 2.x) and `SanitizedHtml` — lane: FE1

**PRD:** D48. **Depends on:** nothing (Quill approved). Start immediately; Tasks 18, 20, 26 use it.

**Files:** Create `src/Lms.Client/Components/Editors/RichTextEditor.razor` (+ `.razor.cs`), `SanitizedHtml.razor`, `wwwroot/js/richtext.js`, `wwwroot/lib/quill/` (vendored 2.x JS/CSS + `LICENSE`), `wwwroot/css/rich.css`. Test `tests/Lms.Client.Tests/Components/RichTextEditorTests.cs`, `SanitizedHtmlTests.cs`.

**Interfaces:** toolbar bold, italic, underline, headings, ordered/bullet lists, link, blockquote, alignment, per-block direction toggle (RTL/LTR), clear formatting; no image/video embeds (Q3 default); parameters `Value`/`ValueChanged` (HTML), `Label`, `Required`, `MaxLength` (text length), `Disabled`, `HelperText`; initial direction from the UI culture; toolbar `aria-label`s from resources, wraps at 360 px; `SanitizedHtml` renders server-sanitised HTML inside `<div class="lms-rich">` and forces `rel="noopener noreferrer"`.

- [ ] **Step 1: bUnit tests (stubbed JS)** `Initial_html_is_passed_to_editor`; `Change_event_updates_bound_value`; `Required_blocks_form_when_empty` (`<p><br></p>` is empty); `Max_length_counts_text_not_markup`; `Disabled_editor_is_read_only`; `Direction_follows_ui_language`; `Component_renders_rtl_in_arabic`; `No_cdn_reference_in_index_html`. **Steps 2–5** → commit `feat(client): shared rich-text editor`.

### Task 15: Plan view v2 — modules in the tree, progress, DESIGN.md restyle — lane: FE1

**PRD:** FR-LP-05, FR-CNT-04. **Depends on:** Task 1; #339, #334 merged (BE Tasks 3, 6 or fakes).

**Files:** Modify `src/Lms.Client/Pages/Classes/LessonPlanPage.razor`, `LessonPlanTree.razor`; Create `Components/Content/ModuleNode.razor`, `ProgressBar.razor`, `IModulesApi.cs` (+ HTTP + fake), `IProgressApi.cs` (+ HTTP + fake). Test `LessonPlanPageV2Tests.cs`.

**Interfaces:** module nodes under lessons (kind icon, title, visibility/target chips for staff, completion state for students), lesson and course progress bars, ISO dates, `PageHeader`/`SectionCard`; hidden-lesson greying kept.

- [ ] **Step 1: bUnit tests** `Student_sees_only_modules_the_server_sends`; `Teacher_sees_hidden_and_scheduled_chips`; `Progress_bar_reflects_completed_over_total`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): lesson plan with modules and progress`.

### Task 16: Asset components — uploader, preview, signed streaming — lane: FE2

**PRD:** FR-CNT-03, X-05. **Depends on:** Task 1 (BE Task 4 or fake).

**Files:** Create `Components/Assets/AssetUploader.razor`, `AssetPreview.razor`, `IAssetsApi.cs` (+ HTTP + fake; extends the transport of `IFileUploadApi`). Test `AssetUploaderTests.cs`, `AssetPreviewTests.cs`.

**Interfaces:** limits from `/api/assets/limits` shown up front; type/size validated before upload (`AssetTypes.ForFileName`); progress, cancel, retry; preview: image, PDF (native viewer), audio/video elements with signed URL refreshed once on expiry (judged by `ServerNowUtc`); no-preview fallback with download only with `Files.Download`.

- [ ] **Step 1: bUnit tests** `Rejects_disallowed_type_and_oversize_before_upload`; `Shows_progress_and_completes`; `Expired_signed_url_is_refreshed_once`; `Download_hidden_without_permission`; rtl. **Steps 2–5** → commit `feat(client): asset uploader and preview`.

### Task 17: Module authoring shell — add, edit, order, visibility, targeting — lane: FE1

**PRD:** FR-CNT-01/02, FR-LP-05, X-10. **Depends on:** Task 15 (BE Task 3 or fake).

**Files:** Create `Components/Content/AddModuleMenu.razor`, `ModuleDialog.razor`, `ModuleMetadataForm.razor`, `ModuleOrderControls.razor`, `MoveModuleDialog.razor`. Test `ModuleAuthoringTests.cs`.

**Interfaces:** "Add module" menu lists the kinds the caller can create now (content kinds; Assignment/Quiz/Library entries appear when their tasks land); common metadata (titles ar/en, visibility with date-time pickers, target ClassCourse or a group of this ClassCourse, completion rule from `CompletionRules.ForContent` for content kinds); per-kind body slot; drag-and-drop ordering plus move up/down buttons (keyboard); hide/show; delete with confirm; move to another lesson; locked blueprint nodes keep no edit controls; `module_visibility_range_invalid` / `module_target_invalid` shown on the right field.

- [ ] **Step 1: bUnit tests** `Teacher_adds_page_module_under_own_lesson`; `Scheduled_requires_from_before_to`; `Group_target_lists_only_groups_of_this_class_course`; `Reorder_by_keyboard_buttons`; `Locked_blueprint_nodes_have_no_edit_controls`; `Student_sees_no_authoring_controls`; rtl. **Steps 2–5** → commit `feat(client): module authoring`.

### Task 18: Simple module bodies and the module viewer — lane: FE1

**PRD:** FR-CNT-01 (File, Video, Audio, Page, Link), FR-CNT-03. **Depends on:** Tasks 14, 16, 17 (BE Tasks 3, 5 or fakes).

**Files:** Create `Components/Content/Kinds/{File,Video,Audio,Page,Link}Editor.razor` and `…Viewer.razor`, `Pages/Content/ModuleViewerPage.razor` (`/class-courses/{Id:guid}/modules/{ModuleId:guid}`). Test `ModuleKindsTests.cs`, `ModuleViewerPageTests.cs`.

**Interfaces:** File/Video/Audio use `AssetUploader` + `AssetPreview`; Link accepts http/https only and opens with `rel="noopener noreferrer"`; Page uses `RichTextEditor` and the viewer renders `SanitizedHtml` of the server's body for the UI language (falls back to the other language); previous/next navigation from `ModuleDetailDto`; "Mark complete" when the rule is Manual.

- [ ] **Step 1: bUnit tests** `Link_rejects_javascript_scheme`; `Page_renders_server_html_via_SanitizedHtml_only`; `Video_uses_signed_url`; `Manual_completion_marks_and_updates_state`; `Previous_next_navigation`; rtl. **Steps 2–5** → commit `feat(client): content module editors and viewer`.

### Task 19: Student course home, continue, completion states — lane: FE1

**PRD:** FR-CNT-04. **Depends on:** Task 18 (BE Task 6 or fake). DEV-PLAN M4 item 3.

**Files:** Create `Pages/Content/MyCoursePage.razor` (`/class-courses/{Id:guid}/learn`), `Components/Content/ContinueCard.razor`. Test `MyCoursePageTests.cs`.

**Interfaces:** lessons with progress, "continue where you left off" (`NextModuleId`), not started / in progress / done, read-only waiting state for Submit/Score rules; teacher per-student progress view is M5 (Q4 in #341, agreed).

- [ ] **Step 1: bUnit tests** `Continue_opens_next_module`; `View_rule_module_shows_done_after_open`; `Submit_rule_module_shows_waiting_state`; rtl. **Steps 2–5** → commit `feat(client): student course home and progress`.

### Task 20: Assignments — teacher: create, publish, submissions, grading — lane: FE2

**PRD:** FR-ASG-01, FR-ASG-02. **Depends on:** Tasks 2, 14, 16, 17 (BE Tasks 8, 9 or fakes). DEV-PLAN M4 item 4.

**Files:** Create `Pages/Assignments/AssignmentDialog.razor`, `SubmissionsPage.razor`, `GradeSubmissionPage.razor`, `ICourseAssignmentsApi.cs` (+ HTTP + fake). Test `AssignmentTeacherTests.cs`.

**Interfaces:** instructions (`RichTextEditor`), attachments, due date-time, max grade, late policy (penalty % required for Penalty), resubmission rule, target, publish now / schedule; submissions `ServerTable` (late badge, status, grade); grading page with submission preview, grade, feedback, annotated file upload slot.

- [ ] **Step 1: bUnit tests** `Late_policy_penalty_requires_percentage`; `Grade_above_max_rejected`; `Annotated_file_attached_to_feedback`; `Submissions_show_late_badge`; rtl. **Steps 2–5** → commit `feat(client): assignment authoring and grading`.

### Task 21: Assignments — student: view, submit, resubmit, feedback — lane: FE2

**PRD:** FR-ASG-02. **Depends on:** Task 20.

**Files:** Create `Pages/Assignments/AssignmentPage.razor`, `SubmitPanel.razor`. Test `AssignmentStudentTests.cs`.

**Interfaces:** instructions, attachments, due countdown using the server offset, late state in plain words, submit text and/or files, resubmission per rule, grade + feedback + annotated file after return; blocked-late explains why.

- [ ] **Step 1: bUnit tests** `Blocked_late_policy_disables_submit`; `Resubmit_only_when_rule_allows`; `Submit_requires_text_or_file`; `Overdue_uses_server_time`; `Graded_shows_feedback`; rtl. **Steps 2–5** → commit `feat(client): student assignment submission`.

### Task 22: Quiz builder — lane: FE3 (FE2 if FE3 is not started)

**PRD:** FR-QZ-01, FR-QZ-02. **Depends on:** Tasks 2, 17 (BE Task 10 or fake). DEV-PLAN M4 item 5.

**Files:** Create `Pages/Quizzes/QuizBuilderPage.razor`, `QuestionEditor.razor`, `QuizSettingsTab.razor`, `QuizPreview.razor`, `IQuizzesApi.cs` (+ HTTP + fake). Test `QuizBuilderTests.cs`.

**Interfaces:** question types, options with correct flags, points, pool draw N of M, settings tab, preview-as-student, reorder (drag + buttons), explicit Save with unsaved-changes guard, `quiz_has_attempts` explained.

- [ ] **Step 1: bUnit tests** `Mcq_needs_one_correct_option`; `Multi_requires_two_or_more_options`; `Pool_draw_count_cannot_exceed_pool_size`; `Close_after_open_validated`; `Leaving_with_unsaved_changes_asks`; rtl. **Steps 2–5** → commit `feat(client): quiz builder`.

### Task 23: Quiz attempt — lane: FE3 (FE2)

**PRD:** FR-QZ-03. **Depends on:** Task 22 (BE Task 11 or fake).

**Files:** Create `Pages/Quizzes/QuizAttemptPage.razor`, `QuestionNavigator.razor`, `AttemptTimer.razor`, `AutosaveQueue.cs`, `QuizResultPage.razor`. Test `QuizAttemptTests.cs`.

**Interfaces:** start confirmation (attempts left, time limit), navigator, server-driven timer (synced on start and each save; announced at intervals for screen readers), debounced queued autosave with saved/saving/failed state and retry, auto-submit at zero or when the server reports closed, submit confirmation, result per review policy, resume an unfinished attempt.

- [ ] **Step 1: bUnit tests** `Answers_autosave_debounced`; `Failed_autosave_shows_state_and_retries`; `Timer_uses_server_remaining_time`; `Auto_submits_at_zero`; `Review_policy_hides_answers`; `Resume_unfinished_attempt`; rtl. **Steps 2–5** → commit `feat(client): quiz attempt with timer and autosave`.

### Task 24: Quiz grading and attempt review — lane: FE3 (FE2)

**PRD:** FR-QZ-03. **Depends on:** Task 23.

**Files:** Create `Pages/Quizzes/QuizAttemptsPage.razor`, `EssayGradingPage.razor`. Test `QuizGradingTests.cs`.

**Interfaces:** attempts list per quiz, essay grading queue (per question or per student), points capped at question points, comment, finalize.

- [ ] **Step 1: bUnit tests** `Essay_points_capped_at_question_points`; `Finalize_disabled_until_all_essays_graded`; rtl. **Steps 2–5** → commit `feat(client): quiz essay grading`.

### Task 25: Digital library pages — lane: FE1

**PRD:** FR-LIB-01. **Depends on:** Tasks 2, 16 (BE Task 12 or fake). DEV-PLAN M4 item 6.

**Files:** Create `Pages/Library/LibraryPage.razor` (`/library`), `LibraryResourceDialog.razor`, `LibraryReader.razor`, `AddToLessonDialog.razor`, `ILibraryApi.cs` (+ HTTP + fake); Modify `NavCatalog.cs` (`Nav_Library`, `Library.View`), `AddModuleMenu.razor` (library picker). Test `LibraryPageTests.cs`.

**Interfaces:** search + filters (type, level, subject, tags), card grid with cover, manage dialog, reader (download button only when allowed), add to lesson from the library and from the module menu.

- [ ] **Step 1: bUnit tests** `Download_button_hidden_when_disabled`; `Filters_compose`; `Add_to_lesson_creates_module_reference`; rtl. **Steps 2–5** → commit `feat(client): digital library`.

### Task 26: Forum and announcements — lane: FE1

**PRD:** FR-FOR-01. **Depends on:** Tasks 2, 14, 16 (BE Task 13 or fake). DEV-PLAN M4 item 7.

**Files:** Create `Pages/Forums/ForumTab.razor`, `ThreadPage.razor`, `PostComposer.razor`, `AnnouncementsTab.razor`, `IForumsApi.cs` (+ HTTP + fake); Modify the class-course page tabs (Plan · Forum · Announcements). Test `ForumTests.cs`.

**Interfaces:** threads (pinned first, locked badge, reply count), thread with replies and attachments, composer with `RichTextEditor`, moderation menu (delete, lock, pin) for `Forums.Moderate`, announcements list/create (pin, notify), student read view.

- [ ] **Step 1: bUnit tests** `Locked_thread_hides_reply_composer`; `Moderator_sees_delete_lock_pin`; `Student_cannot_see_moderation`; `Pinned_threads_first`; `Announcement_notify_toggle_sent`; rtl. **Steps 2–5** → commit `feat(client): course forum and announcements`.

### Task 27: Navigation, home cards and M4a notification labels — lane: FE2

**PRD:** FR-NOT (labels for M4a events), X-01. **Depends on:** Tasks 2, 19, 21, 23.

**Files:** Modify `NavCatalog.cs`, `Pages/Home.razor`, notification label resources. Test `HomeCardsTests.cs`, `NotificationLabelParityTests.cs`.

**Interfaces:** student cards (continue learning, due assignments, open quizzes), teacher cards (to grade); `Notif_*` / `NotifEvent_*` ar/en for the five wave 2 events.

- [ ] **Step 1: bUnit tests** `Home_cards_depend_on_permissions`; `Notification_types_have_both_languages`. **Steps 2–5** → commit `feat(client): M4a navigation and home cards`.

### Task 28: M4a FE hardening — accessibility, 360 px, RTL sweep, E2E hooks — lane: FE1 + UI/UX Designer review

**PRD:** X-01, X-10. **Depends on:** Tasks 15–27.

**Files:** all M4a pages; `docs/screenshots/pr<N>/`. Test extends `A11yTests`.

**Interfaces:** focus management in dialogs and the quiz timer, `data-testid`s agreed with QA for Tasks 31–33, 360 px labelled cards, light + dark screenshots in ar and en; designer review per DESIGN.md.

- [ ] **Steps 1–5** → commit `fix(client): M4a accessibility and responsive sweep`.

---

### Task 29: M4a isolation and permission probe suite — lane: QA (QA2)

**PRD:** §4.2.3, FR-SEC-05, §3.3 step 2. **Depends on:** BE Tasks 3–13 (grows as they merge; first PR after Task 3).

**Files:** Create `tests/Lms.IntegrationTests/Security/M4aCallers.cs` (teacher A with ClassCourse X, teacher B with Y, student in X group 1, student in X group 2, student of another class, School-2 admin, client-B admin, guardian), `ProbeRows/M4a*.cs` (one per module), `M4aEndpointMatrixTests.cs`, `M4aIsolationTests.cs`, `M4aVisibilityProbeTests.cs`.

**Interfaces:** every M4a route × caller (teacher A on Y's modules/assets/submissions → 404; group-2 student on a group-1 module, its asset URL and its assignment → 404; student on builder/grading routes → 403; guardian on any module → 403; School-2 admin on School-1 library resource below School 1 → 404); `Every_M4a_api_route_is_in_the_probe_matrix`; an isolation test per M4a table.

- [ ] **Steps 1–5** as M3 Task 45. **Commit** `test(security): M4a endpoint matrix, visibility and isolation probes`.

### Task 30: Security review — rich text and assets — lane: QA (QA3)

**PRD:** D48, X-05, PRD §9.2. **Depends on:** Tasks 3, 4, 14, 18.

**Files:** Create `tests/Lms.IntegrationTests/Security/RichTextXssTests.cs` (OWASP XSS cheat-sheet vectors through Page, instructions, posts), `AssetSecurityTests.cs` (polyglot / mismatched content types, path-like names, guessed ids, expired URLs), `docs/security/m4a-review.md` checklist.

- [ ] **Steps 1–5** → commit `test(security): M4a rich text and asset review`. Findings filed as `bug` + `sev:*` against the owning lane.

### Task 31: E2E exit demo 1 — teacher builds lessons with content modules; student completes content — lane: QA (QA1)

**PRD:** FR-LP-05, FR-CNT-01..04, DEV-PLAN §1 M4 exit (content part). **Depends on:** Tasks 3–6, 14–19; M3 E2E fixtures (#285).

**Files:** Create `tests/Lms.E2ETests/M4/ContentTests.cs`, page objects `LessonPlanPage`, `ModuleDialog`, `ModuleViewerPage`.

**Scenarios (`[Theory]` `"ar"` / `"en"`; Arabic asserts `dir="rtl"`):** `Teacher_adds_file_video_audio_page_and_link_modules_and_reorders_them`; `Scheduled_and_group_modules_are_hidden_from_other_students`; `Student_opens_page_marks_file_complete_and_progress_updates`; `Continue_opens_next_incomplete_module`.

- [ ] **Steps 1–5** as M3 Task 46 (one E2E stack at a time, `e2e.lock`). **Commit** `test(e2e): M4a content modules and completion`.

### Task 32: E2E exit demo 2 — student submits an assignment and takes a quiz — lane: QA (QA2)

**PRD:** FR-ASG-01/02, FR-QZ-01..03, DEV-PLAN §1 M4 exit. **Depends on:** Tasks 8–11, 20–24.

**Files:** Create `tests/Lms.E2ETests/M4/AssignmentTests.cs`, `QuizTests.cs`.

**Scenarios (ar + en):** `Teacher_publishes_assignment_student_submits_late_with_penalty_teacher_grades_with_annotated_file`; `Blocked_late_assignment_refuses_submission`; `Student_takes_timed_quiz_answers_autosave_after_reload_and_scores`; `Unfinished_attempt_is_auto_submitted_at_close` (dev job trigger); `Teacher_grades_essay_and_student_sees_result_per_review_policy`.

- [ ] **Steps 1–5** as Task 31. **Commit** `test(e2e): M4a assignments and quizzes`.

### Task 33: E2E exit demo 3 — library and forum — lane: QA (QA3)

**PRD:** FR-LIB-01, FR-FOR-01. **Depends on:** Tasks 12, 13, 25, 26.

**Files:** Create `tests/Lms.E2ETests/M4/LibraryAndForumTests.cs`.

**Scenarios (ar + en):** `Supervisor_adds_library_book_with_download_disabled_teacher_adds_it_to_lesson_student_reads_without_download`; `Student_posts_thread_teacher_pins_and_locks_it`; `Teacher_posts_pinned_announcement_and_students_are_notified`.

- [ ] **Steps 1–5** as Task 31. **Commit** `test(e2e): M4a library and forum`.

---

## Execution notes for the Team Lead
- **Issues:** `docs/superpowers/plans/m4a-issues.json` holds one entry per task (33: 2 Lead, 11 BE, 15 FE, 5 QA) as `{title, lane, labels, body}`, generated from this file in the M3 issue format. Before creating them: create the labels `area:content`, `area:grading`, `area:assignments`, `area:quizzes`, `area:library`, `area:forums`; put every issue on milestone "M4 Teaching & Learning" and the "LMS v1" project; then replace "plan Task N" in the Depends-on lines with the issue numbers. Task 1's issue is closed by the wave 1 contract PR.
- **Contract PRs first:** Task 1 (`lead/m4a-contracts`, draft) merges before any M4a code. Task 2 opens while Task 3 is in review and merges before Tasks 8, 10, 12, 13, 20–27 start. Each wave bumps `BuiltInRoleDefaults` once (4, then 5).
- **M3 prerequisites:** #325 (lesson plans BE), #339 (plan view FE), #334 (design pilot) must merge first. BE1 can start Task 7 and the upload half of Task 4 while #325 is in review; FE1 starts Task 14 at once.
- **Critical path:** Task 1 → Task 3 (BE1) → Task 5 → Task 6 → Task 31; in parallel Task 2 → Tasks 8/10 → 9/11 → Task 32. Review Task 3 first; it fixes the `RefId` pattern every feature kind copies.
- **BE lanes (split by folder; two agents never edit the same files):**
  - **BE1 — content kernel (`Domain/Content`, `Infrastructure/Content`, `Features/Content`, `Domain/Grading`, `Infrastructure/Grading`, the lesson-plan read query lines for nesting):** 3 → 4 → 5 → 6 → 7 (7 may go first while waiting for #325).
  - **BE2 — assignments (`Domain/Assignments`, `Infrastructure/Assignments`, `Features/Assignments`):** 8 → 9.
  - **BE3 — quizzes (`Domain/Quizzes`, `Infrastructure/Quizzes`, `Features/Quizzes`):** 10 → 11.
  - **BE4 — library and forums (`Domain/Library`, `Domain/Forums`, their Infrastructure and Features folders):** 12 → 13. BE4 also helps QA2 with probe rows for its own routes.
  - Feature kinds create their module through `IModuleWriter.CreateForRefAsync(kind, refId, …)` (Task 3) and extend `IAssetAccess` through a registered `IAssetReferenceSource` per feature — never by editing `Infrastructure/Content` files.
- **Shared BE files (serialize merges):** `Program.cs` (two lines per module), `ProbeRows/` (each PR its own file), `IsolationBypassGuardTests.cs` allow-list (no M4a bypass expected), migrations (regenerate on top of `main`), `Lms.Shared` (Lead only).
- **FE lanes:** **FE1 — content (`Components/Editors`, `Components/Content`, `Pages/Classes/LessonPlan*`, `Pages/Content`, `Pages/Library`, `Pages/Forums`):** 14 → 15 → 17 → 18 → 19 → 25 → 26 → 28. **FE2 — assets and assignments (`Components/Assets`, `Pages/Assignments`, `NavCatalog.cs`, `Home.razor`):** 16 → 20 → 21 → 27 (+ 22–24 if no FE3). **FE3 — quizzes (`Pages/Quizzes`):** 22 → 23 → 24. **FE is the bottleneck (15 FE tasks vs 11 BE tasks):** start FE3 when Task 2 merges (Lead may add agents). Shared FE files: both resx (append in your own key prefix), `ClientServices.cs` (one line per API), `NavCatalog.cs` (FE2 owns; FE1 appends `Nav_Library` in Task 25), `AddModuleMenu.razor` (FE1 owns; others ask FE1 for their entry).
- **QA:** QA1 reviews BE1 and FE1 PRs and owns Task 31; QA2 reviews BE2, BE3 and FE3 PRs and owns Tasks 29, 32; QA3 reviews BE4 and FE2 PRs, owns Tasks 30, 33 and gives the security review on every PR touching the sanitiser, assets or the reader. Every review checks the Review Focus items, explicit permissions and a relationship check on every restricted endpoint, `IModuleAccess` on every student read, resource parity, RTL screenshots, and the one-E2E-stack lock.
- **Splits from DEV-PLAN §2 M4:** item 1 → Tasks 4 + 16; item 2 → 3 + 5 + 15 + 17 + 18; item 3 → 6 + 19; item 4 → 8 + 9 + 20 + 21; item 5 → 10 + 11 + 22–24; item 6 → 12 + 25; item 7 → 13 + 26. New: Task 7 (grade outbox, D51), Task 14 (editor, D48), Tasks 27–30. Items 8–10 → M4b; item 11 (LTI) dropped by D50.
- **Exit demo:** Tasks 31–33 green in CI in ar and en and Task 29 green on `main` = M4a done; then update `docs/STATUS.md`, record the owner's answers below as D52+ in `docs/PRD.md` §8 and start the M4b plan.

## Later: M4b (separate plan after M4a)
- **Contract wave 3 (Lead):** `LiveSessions.{View, Manage}`, `Packages.{Upload, View}`, live session and package DTOs, routes, codes (`package_invalid`, `provider_unavailable`), `Live.Reminder` event.
- **Spike first (QA + BE):** SCORM/H5P hosting on a sandboxed origin with a strict CSP (D51): host-name vs path layout, zip hardening (size, entry count, nesting, path traversal), tracking-commit rate limits; H5P player choice.
- **BE:** virtual classroom (`IMeetingProvider`, Zoom server-to-server OAuth with **one platform app**, D49; credentials only in server configuration; single/recurring sessions, reminders, join link, participant import feeding M5 attendance, recording link); SCORM 1.2/2004 (upload + manifest validation, sandboxed launch, runtime API, completion/score → `ICompletionRecorder` + `IGradeEvents`); H5P (upload + `h5p.json` validation, embedded player, xAPI → score); M4b probes and security checklist.
- **FE:** live sessions (schedule dialog, join window, recording link; "ask your platform administrator" when Zoom is not configured), SCORM/H5P upload and sandboxed players (`postMessage` shim), M4b parts of navigation and hardening.
- **QA:** M4b probes, package security review (zip-slip, decompression bombs, cross-tenant paths), E2E for a live session and a SCORM package.
- **Open for M4b:** Zoom plan capacity (one account's licences, concurrent meetings and API rate limits shared by all clients) must be confirmed before the Zoom task is sized.

## Open questions for the product owner
Each has a recommended default that this draft already assumes.

1. **Admins and managers editing content (amends §3.4 "Content & activities"):** the matrix gives Org Admin and Manager only "View", but the D16 escalation guard lets a user assign a role only if they hold all of its permissions, so with View only they could no longer assign the Teacher role. **Recommended:** Org Admins and Managers also hold `Modules.Manage` (and in wave 2 the staff-side assignment, quiz and forum permissions), so they can fix content in their units; they are not limited to own ClassCourses. Alternative: change the escalation guard to ignore activity permissions (wider change, touches M1 security code).
2. **Asset size limits:** one limit for all files (the existing `Files.MaxUploadMb`, default 200 MB, adjustable per unit up to 2 048 MB) or separate limits per kind (BE4 proposed 25 MB documents, 100 MB audio, 500 MB video). **Recommended:** one limit in M4a; per-kind limits later only if clients ask.
3. **Images inside rich text (D48 follow-up):** the editor ships without image embeds (text, lists, links, headings, alignment, direction). **Recommended:** no images in M4a; teachers add images as File modules. Later: upload through the asset service and insert a signed reference, never base64.
4. **Digital library in M4a:** D51 names content, assignments, quizzes and forums; the library is not named but the "Library resource" module kind depends on it and DEV-PLAN puts it in M4. **Recommended:** M4a (both proposals assume it).

## PRD / DEV-PLAN inconsistencies found
- §3.4 "Content & activities" (Org Admin/Manager View) vs the D16 escalation guard — Q1.
- FR-CNT-02 lists completion rules view / submit / score, while FR-CNT-04 mentions manual marking — the contract adds `Manual` and `None` (not counted).
- FR-CNT-01 lists "LTI tool" — removed by D50; DEV-PLAN M4 item 11 likewise.
- The PRD §7 model has no lesson id in the DTOs: class lessons are addressed by the blueprint `Key`, so module routes are `/api/class-courses/{id}/lessons/{lessonKey}/modules` (the proposals sketched `/api/class-lessons/{id}`).
- FR-ASG-02 / FR-QZ-03 "grade flows to the gradebook" needs the M5 gradebook — M4a writes the grade-event outbox only (D51).
