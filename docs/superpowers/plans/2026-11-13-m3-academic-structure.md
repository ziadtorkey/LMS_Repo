# M3 Academic Structure Implementation Plan

> **Status: APPROVED (product owner, 2026-11-11) with decisions D37–D46 (open questions answered with the recommended defaults; blueprint authoring for Org Admins and Managers per D46).** Open questions are at the end ("Open questions for the product owner"); every one has a recommended default the plan already assumes. Team: Lead, BE1, BE2, BE3, BE4, FE1, FE2, QA1, QA2, QA3.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. In this project each task = one GitHub issue and one PR; the lane (Lead contract / BE1–BE4 / FE1–FE2 / QA1–QA3) is marked on each task.

**Goal:** Admins and supervisors create courses (CRUD, Excel import), allocate them in the tree, restrict them (religion, gender, level) and assign supervisors who build and publish a versioned blueprint; admins create classes per academic year (single, bulk, copied from last year), pick their courses from the allocations (distribution matrix), assign eligible teachers (primary + assistants with dated history); students are created or imported with generated codes, default passwords (D24), guardians and class placement, and are enrolled automatically in their class's courses (restrictions respected, optional courses manual, exceptions approved); teachers see only their own ClassCourses and the locked class lesson plan copied from the blueprint (push or new-classes-only on publish); teachers form groups (one group per ClassCourse, D6); a student is transferred between classes with enrollments re-mapped; students edit their own non-critical data and request changes to critical data; guardians get a portal with "my children" — all bilingual (ar RTL / en), isolated per client and subtree.

**Architecture:** Same modular monolith as M0–M2 (`Features/<Module>/` + `<Module>Module.cs`; entities in `Lms.Domain/<Module>/`; persistence and services in `Lms.Infrastructure/<Module>/`; contracts in `Lms.Shared` through Lead PRs). M3 adds two kernels the feature tasks build on, like the M2 people kernel: the **academic kernel** (Task 5: one migration `AddAcademicStructure` with courses, allocations, restrictions, supervisors, classes, class students, class courses, teacher eligibility and assignments, enrollments, groups, transfers; the pure restriction evaluator; lifecycle hook interfaces; usage checks; the course catalog ancestor reader) and **relationship scopes** (Task 12: PRD §3.3 step 2 — Teacher → own ClassCourses, Supervisor → supervised courses, Student → own enrollments — evaluated after the M0 permission/scope/policy check). All writes to `CourseEnrollment` go through one service, `IEnrollmentEngine` (Task 24), which the class-course lifecycle (Task 14), student placement (Tasks 21, 23, 26), exceptions (Task 11) and transfers (Task 28) call. Blueprints (Task 16) are versioned per course; a class lesson plan (Task 17) is a locked copy merged by stable keys. The student/guardian tasks deferred from M2 (D23) are carried in with their M2 appendix text plus the deltas listed here.

**Tech Stack:** unchanged from M2: .NET 10, ASP.NET Core minimal APIs + Razor Pages (account pages), EF Core 10 + `MySql.EntityFrameworkCore` (ADR 0001), MySQL 8.4, OpenIddict 7, Hangfire (`IBackgroundJobs`, #144), ClosedXML, MinIO via `IFileStorage`, SignalR notifications, Blazor WebAssembly + MudBlazor 9 (`MudDropContainer` for the blueprint tree), xUnit, Testcontainers, bUnit, Playwright. **No new packages.**

**Spec:** `docs/PRD.md` — §3.3 (relationship rules), §3.4 (rows "Students & guardians", "Classes, groups", "Courses, allocations, restrictions", "Blueprints", "Enrollment, transfers, promotion", "Teacher assignment"), §4.2, §4.3 (policies `CreateCourses`, `CreateClasses`, `CreateStudents`), §4.4 (per-student flags), §5 X-01..X-07, X-09, §6.4 FR-STU-01..06, §6.5 FR-CLS-01..05, FR-GRP-01..03, §6.6 FR-CRS-01..04, FR-LP-01..05, §6.7 FR-ENR-01..05, FR-TCH-01..03, §7, decisions D1–D36 (especially D6, D7, D9, D22–D30, D32, D34). Master plan: `docs/DEV-PLAN.md` §2 "M3 Academic Structure" (items 0–13), DoD §3.4, exit demo §1. Previous plans: `2026-11-10-m0-foundation.md`, `2026-11-11-m1-organization-access.md`, `2026-11-12-m2-people.md` — **its "Appendix — Deferred to M3 (owner Q1)" is the base text of Tasks 2, 18, 19, 21, 22, 23, 25, 37–42 and 47.**

## What M3 builds on (checked on `origin/main` @ `30ad12e`, 2026-11-11)
- **Merged:** people kernel #181 (`StaffProfile`, `StudentProfile` with `FeatureFlagOverridesJson` and the client-wide student-code unique index, `GuardianProfile`, `StudentGuardian.For/Repoint`, `PersonStatusChange`, `PersonNormalizer`, `IPersonDuplicateFinder` incl. `StudentNationalIdExistsAsync`, `CountStudentsByNameAndDobAsync`, `FindGuardianAsync`, `PeopleUsageCheck`); person accounts and status gate #191 (`IPersonAccounts.EnsureUserAsync` — creates users **without** password, `IInvitationOutbox`, `IPersonStatusService`, `IRoleAssignmentSync.SyncBuiltInAsync`, `RecipientScopes.AncestorsAndSelf`); staff expiry job #197 (pattern for system jobs: platform context, loop over clients); jobs kernel #144 (`IBackgroundJobs.Enqueue<T>` captures the caller context, `EnqueueAsSystem`, `AddOrUpdateRecurring`); identity providers #183/#188/#194; staff pages #189 (`Components/People/UnitPicker`, `PhotoUpload`, `PersonStatusChip`, `IStaffApi` + fake); `OrgUnit.IsLevelTransfer/NextTransferUnitId/IsCourseManagementLevel` fields; `LevelType.CanHostClasses`; `IAcademicYearResolver.GetEffectiveAsync/GetCurrentAsync` (D22); `ISettingsResolver.ResolveAsync`; `IOrgTree`; `UnitAccess.CheckUnitAsync/CheckClientRootAsync`; `IOrgUnitUsageCheck`; `IOrgUnitRepather`; `INotificationPublisher` + `NotificationRequest`.
- **Still open on 2026-11-11 (M2/M1), named in Depends-on where they matter:** import framework #159 / PR #202 (draft) — `IImportDefinition`, `ImportChecks`, `XlsxBuilder`; import wizard #169 / PR #195 (draft); staff create #162 / PR #187 (the `StaffCreator` pattern); staff lifecycle/share #163/#164 (PRs #198, #196); staff import #165 / PR #205; role-blocking #160 / PR #184 (`IRoleGrantGuard`, `PolicyEvaluator.FindBlockingUnitForRoleAsync`); external login #168 / PR #193; dev job trigger #199 / PR #201; staff import page #172 / PR #204; M1 academic flags API #48 / PR #148 (`PUT /api/org-units/{id}/academic-flags`); M1 branch copy #50 and templates #52 (BE, open).
- **Not on main and needed by D24:** `User.MustChangePassword` and a forced change-password step — Task 20 adds them.

## Global Constraints
- **Every M0, M1 and M2 Global Constraint still applies** (by reference: path format, `X-Lms-Context`, Guid v7, utf8mb4, default culture `ar`, explicit permissions per built-in role and `PlatformOnly`, `UnitAccess.CheckUnitAsync` / `CheckClientRootAsync` with 404 for invisible units, ProblemDetails `code` from `ProblemCodes`, `// ISOLATION-BYPASS: <reason>` + allow-list entry in `IsolationBypassGuardTests` + explicit `OrganizationId`, no new `DbSet` properties, two `Program.cs` lines per module, one migration per PR with working `Down()`, `AuditableEntity` + `[Audited]` + isolation test per business entity, names 1–200 chars both languages, paging max 200, probe rows per module in `tests/Lms.IntegrationTests/Security/ProbeRows/` in the same PR as the route, FE API interface + fake per module, ar/en resource parity, RTL screenshots in `docs/screenshots/pr<N>/`; M2: people isolation shapes, client-wide duplicate checks answering only `field`, normalization rules, imports X-06/D34, jobs never with an empty context). **The M2 appendix "Revised Global Constraints for M3" (student code D25, student accounts D24) apply unchanged.**
- **Academic isolation shapes (Task 5).** All `IOrgUnitScoped`:
  - `Course` — unit = **owner unit**; `CourseRestriction`, `CourseSupervisor`, `Blueprint`, `BlueprintSection`, `BlueprintLesson` copy the course's owner unit fields.
  - `CourseAllocation` — unit = the allocated unit. `TeacherCourseEligibility` — unit = the unit the eligibility applies to (it covers ClassCourses at or below it).
  - `Class` — unit = the class's unit; `ClassStudent`, `ClassCourse`, `ClassCourseTeacher`, `CourseEnrollment`, `EnrollmentException`, `StudentGroup`, `GroupMember`, `ClassLessonPlan`, `ClassSection`, `ClassLesson` copy the class's unit fields; `StudentTransfer` = the **target** class's unit (plus `FromOrgUnitPath` for the source-side read).
  - Copied unit fields are re-pathed by the M1 repather like every other unit-scoped table (the `Repather_covers_*` coverage test must stay green).
- **Ancestor reads (one reviewed place).** A course owned at an ancestor of the caller's scope, and allocations at ancestors, are needed to answer "which courses are available here". Only `Infrastructure/Courses/CourseCatalog.cs` may read them, with `ISOLATION-BYPASS` limited to the caller's organization **and** to owner/allocation paths that are proper prefixes of a unit path the caller can see (ancestors only, never siblings). Such courses are returned read-only (`IsInherited = true`); writes always go through `CheckUnitAsync` at the owner unit, which answers 404 for ancestors. QA greps the allow-list.
- **Course ownership (Q1 default):** a course's owner unit is the client root or a unit with `IsCourseManagementLevel = true` (400 `invalid_owner_unit` otherwise). A course is **available** at unit U when it is allocated at U or an ancestor of U; only available courses can become ClassCourses of a class at U.
- **Codes:** course code and class code `^[A-Za-z0-9_-]{1,50}$`, stored upper-case. Course code unique among live courses of the client (Q2, 409 `course_code_taken`); class code unique among live classes of the same academic year in the client (409 `class_code_taken`).
- **Live keys (MySQL has no partial unique index).** Invariants that hold "among live/active rows" use a nullable `*Key` column that holds the value only while the row is live/active (NULLs are distinct in MySQL unique indexes). Task 5 adds `ILiveKeyed.ClearLiveKeys()`; `StampingInterceptor` calls it when a row is soft-deleted. Indexes: `Course (OrganizationId, CodeKey)`; `Class (OrganizationId, AcademicYearId, CodeKey)`; `CourseAllocation (OrgUnitId, CourseKey)`; `ClassCourse (ClassId, CourseKey)`; `ClassStudent (OrganizationId, StudentId, ActiveYearKey)` (**one active class per student per academic year**); `CourseEnrollment (ClassCourseId, StudentId, ActiveKey)`; `GroupMember (ClassCourseId, StudentId, LiveKey)` (**D6: at most one group per student per ClassCourse**, enforced by the DB, not only by code); `ClassCourseTeacher (ClassCourseId, OpenPrimaryKey)` (one open primary teacher); `CourseSupervisor (CourseId, UserId, LiveKey)`; `TeacherCourseEligibility (OrgUnitId, UserId, CourseId, LiveKey)`. A unique-index violation maps to the task's 409 code (concurrency tests prove it).
- **Academic year context (X-04, D22):** a class belongs to one academic year that must be in the effective year list of its unit (`IAcademicYearResolver.GetEffectiveAsync`, 400 `year_not_effective`). A **Closed** year (or a Closed class) is read-only: every write on the class or anything hanging off it answers 409 `year_closed` / `class_closed`. List endpoints take `yearId` (default: the current year of the queried unit; the client sends `IYearStore.SelectedYearId`).
- **Relationship scopes (Task 12, PRD §3.3 step 2, Q6 default):** after `RequirePermission` + `CheckUnitAsync` + policy, the active role's relationship rule (contract `RelationshipRules`, Task 1) restricts the resource: built-in **Teacher** → ClassCourses where the caller holds a current `ClassCourseTeacher` row (or classes where the caller is homeroom teacher, for `Classes.View` / `Students.View`); built-in **Supervisor** → courses where the caller is a live `CourseSupervisor`; built-in **Student** → own active enrollments. Custom roles and the other built-in roles are unrestricted inside their unit scope. A miss on a single resource answers **404** (never reveals it); list endpoints filter. Every feature endpoint that touches a restricted permission calls `IRelationshipScope` — QA checks each one (Review Focus 1).
- **One enrollment writer:** only `IEnrollmentEngine` (Task 24) creates, drops or re-maps `CourseEnrollment` and `ClassStudent` rows. Restrictions (Task 5 evaluator) are checked on every path: auto-enrollment, manual enrollment, import, transfer, grouping suggestions. Auto-enrollment never enrolls a student who fails a restriction; it reports the skip.
- **Student placement:** a student has at most one **active** `ClassStudent` per academic year. Placing a student moves the student's home unit to the class's unit (`StudentProfile.MoveHome` + `StudentGuardian.Repoint` + Student-role assignment sync through `IRoleAssignmentSync`). Placement is optional on create and import (Q10 default); unplaced students are listable (`StudentQuery.Unplaced`).
- **Capacity (setting `Classes.CapacityMode`, `Warn` | `Block`, default `Warn`):** adding students beyond `Capacity` → `Block`: 409 `class_full`; `Warn`: 409 `over_capacity` unless the request sets `ConfirmOverCapacity = true`. Imports treat `Warn` as allowed (reported in preview) and `Block` as a row error.
- **Blueprint identity:** every blueprint section and lesson has a stable `Key` (Guid) kept across versions (a draft copied from version N keeps N's keys; new items get new keys). Class sections/lessons carry the same keys; push merges by key. Class plan sections/lessons are never edited by teachers (locked, FR-LP-03); removed items are **hidden**, never deleted (history kept; teacher content arrives in M4 and stays attached).
- **Background jobs:** blueprint push (`BlueprintPushJob`, queue `default`) is enqueued with `IBackgroundJobs.Enqueue` (publisher's context). Bulk operations that run in the request (bulk create classes ≤ 50, copy classes ≤ 500, add ≤ 500 students to a class, bulk teacher assignment ≤ 200 ClassCourses) answer 400 `too_many_items` above the limit. Imports use the M2 framework (`imports` queue).
- **Notifications:** new events (Task 1) go through `INotificationPublisher`; guardians and students are notified only when they have a user (portal/student account). Side effects (emails, notifications, push jobs) only after the transaction commits.
- **Performance (X-09):** list endpoints server-paged with indexes on the filter columns; auto-enrollment is set-based (no per-student × per-course round trips): placing 500 students into a class with 12 ClassCourses completes in < 5 s on the Testcontainers MySQL (test in Task 24).
- **Migrations:** `AddAcademicStructure` (Task 5), `AddStudentCodeCounters` (Task 18), `AddMustChangePassword` (Task 20), `AddStudentChangeRequests` (Task 22), `AddBlueprints` (Task 16, includes the class lesson plan tables), `AddEnrollmentExceptions` (Task 11). Whoever merges second regenerates on top of `main`.
- **FE:** API interfaces `ICoursesApi`, `IBlueprintsApi`, `IClassesApi`, `ITeachingApi`, `IEnrollmentApi`, `IGroupsApi` (FE1/FE2 per task) and the M2-deferred `IStudentsApi`, `IGuardiansApi`, `IChangeRequestsApi`, `IMyProfileApi`, each with HTTP implementation + fake. Codes, emails, mobiles, national ids, default passwords are `dir="ltr"` inputs in both cultures. Year-bound pages read `IYearStore` and show the read-only banner for Closed years.

## Review Focus
1. **Relationship rule bypassed:** a teacher opening a student, class, ClassCourse, enrollment, group or lesson plan outside their own ClassCourses gets 404 (lists never contain them); a supervisor editing the blueprint, allocations or teachers of a course they do not supervise gets 404; a student reading another class's plan gets 404. (Tests in Tasks 12, 15, 16, 17, 21, 24, 27; probes in Task 45.)
2. **Ancestor course data leaks sideways:** `CourseCatalog` returning a course owned in a sibling school or another client, or a School-2 admin allocating / adding to a class a course owned in School 1. (Tests in Tasks 5, 6, 7, 14; probes in Task 45.)
3. **Enrollment invariants broken on a side path:** a student active in two classes of one year, in two groups of one ClassCourse (also under two concurrent requests), auto-enrolled in a restricted course, or enrolled through import/transfer/exception without the same checks; removing a course or a student leaves active enrollments. (Tests in Tasks 5, 11, 14, 24, 26, 27, 28.)
4. **Blueprint push damages class data:** push must merge by key, hide removed lessons, never touch Closed years or Closed classes, be idempotent on re-run, and a NewClassesOnly publish must not change existing plans (except plans that never received a version). (Tests in Tasks 16, 17.)
5. **Student credentials leak (D24):** the default password never appears in logs, audit `Before/After`, `ImportRow.ValuesJson`, previews, error reports or any response after the first — the import column is flagged `ImportColumnDto.Sensitive` and shown only as a mask in the preview, rows API and error report; a must-change user gets no token before changing it; credentials go only to the student's or the primary guardian's email. (Tests in Tasks 20, 21, 23.)
6. **Carried from M2 for students and guardians:** M2 Review Focus 1 (duplicate/guardian lookups reveal only `field` / name + masked mobile) and 3 (student import all-or-nothing including codes, guardians, accounts, placements, enrollments). (Tests in Tasks 19, 21, 23.)

---

### Task 1: Contract — M3 permissions, matrix, relationship rules, policies, settings, problem codes, audit and notification catalogs — lane: Lead contract

**PRD:** §3.3, §3.4, §4.3, §4.4, X-02, X-07, FR-STU-03/04/06, FR-CRS-*, FR-CLS-02, FR-LP-01, FR-ENR-05, D27, D32. **Depends on:** M2 contracts #179.

**Files:**
- Modify: `src/Lms.Shared/Authorization/Permissions.cs`, `BuiltInRoles.cs`, `PolicyCatalog.cs`; Create `src/Lms.Shared/Authorization/RelationshipRules.cs`
- Modify: `src/Lms.Shared/Configuration/SettingKeys.cs`, `SettingCatalog.cs`, `src/Lms.Shared/ProblemCodes.cs`, `src/Lms.Shared/Auditing/AuditCatalog.cs`, `src/Lms.Shared/Notifications/NotificationEvents.cs`
- Modify: `src/Lms.Client/Resources/SharedResource.{ar,en}.resx` (keys the parity tests require: `Error_{code}`, `Notif_{Event}`, `NotifEvent_{Event}`, `Policy_{Key}`, `Setting_{Name}`, `Perm_{Permission}` if the roles page has them), `src/Lms.Infrastructure/Access/BuiltInRoleDefaults.cs` (bump version so the reconciler adds the new permissions)
- Test: `tests/Lms.UnitTests/Contracts/BuiltInRoleMatrixTests.cs`, `ConfigurationContractTests.cs`, `AcademicCatalogContractTests.cs` (new)

**Interfaces:**
- Permissions (append): `Students.{View, Create, Update, Delete, Import, ApproveChanges, SelfEdit, ViewChildren}` (M2 appendix); `Courses.{View, Create, Update, Delete, Import, Allocate, ManageSupervisors}`; `Blueprints.{View, Edit, Publish}`; `Classes.{View, Create, Update, Delete, Close, ManageCourses}`; `TeacherAssignments.{View, Manage}`; `Enrollment.{View, Manage, Import, Transfer, RequestException, ApproveException}`; `Groups.{View, Manage}`; `LessonPlans.View`.
- Matrix additions (explicit lists; Import only for OrgAdmin as in §3.4):
  - OrgAdmin + `Students.{View, Create, Update, Delete, Import, ApproveChanges}`, `Courses.*` (7), `Blueprints.View`, `Classes.*` (6), `TeacherAssignments.*`, `Enrollment.*` (6), `Groups.*`, `LessonPlans.View`.
  - Manager + `Students.{View, Create, Update, Delete, ApproveChanges}`, `Courses.{View, Create, Update, Delete, Allocate, ManageSupervisors}`, `Blueprints.View`, `Classes.*` (6), `TeacherAssignments.*`, `Enrollment.{View, Manage, Transfer, RequestException, ApproveException}`, `Groups.*`, `LessonPlans.View`.
  - Supervisor + `Students.View`, `Courses.{View, Create, Update, Delete, Allocate}`, `Blueprints.*`, `Classes.View`, `TeacherAssignments.*`, `Enrollment.{View, ApproveException}`, `Groups.View`, `LessonPlans.View`.
  - Counselor + `Students.{View, Update, ApproveChanges}`, `Classes.View`, `Enrollment.{View, RequestException}`, `Groups.View`.
  - Teacher + `Students.View`, `Courses.View`, `Blueprints.View`, `Classes.View`, `TeacherAssignments.View`, `Enrollment.View`, `Groups.*`, `LessonPlans.View` (all relationship-scoped, D32).
  - Student + `Students.SelfEdit`, `LessonPlans.View`. Guardian + `Students.ViewChildren`.
- `RelationshipRules` (new): `Relationships { OwnClassCourses, SupervisedCourses, OwnEnrollments }`; `sealed record RelationshipRule(string RoleKey, string Relationship, IReadOnlyList<string> Permissions)`; `All` = Teacher/`OwnClassCourses`: every Teacher permission above except `Courses.View`, `Blueprints.View` (the catalog and blueprints of courses are not secret inside the unit); Supervisor/`SupervisedCourses`: `Courses.{Update, Delete, Allocate}`, `Blueprints.{Edit, Publish}`, `TeacherAssignments.Manage`, `Enrollment.ApproveException`; Student/`OwnEnrollments`: `LessonPlans.View`. `RelationshipRules.For(string roleKey, string permission) : string?`.
- `PolicyCatalog` (append): `CreateStudents → [Students.Create, Students.Import]`, `CreateCourses → [Courses.Create, Courses.Import]`, `CreateClasses → [Classes.Create]`.
- Settings: `SettingValueKinds.StringSet`; `SettingCategories.Academic`. Keys: `StudentsCodeMode` (`"Students.CodeMode"`, `"Auto"`, Enum `[Auto, Manual]`); `StudentsCodePattern` (`"Students.CodePattern"`, `"{Seq:6}"`, String); `StudentsCriticalFields` (`"Students.CriticalFields"`, `StudentFields.DefaultCritical`, StringSet over `StudentFields.Requestable`); `GuardiansDataRequired` (`"Guardians.DataRequired"`, false); `ClassesCapacityMode` (`"Classes.CapacityMode"`, `"Warn"`, Enum `[Warn, Block]`); `BlueprintsSectionLabel` (`"Blueprints.SectionLabel"`, `"Week"`, Enum `[Week, Unit, Chapter]`); `EnrollmentTransferCarriesGrades` (`"Enrollment.TransferCarriesGrades"`, true; read by M5 gradebook).
- `ProblemCodes` (append): M2-appendix student codes (`possible_duplicate, student_code_taken, student_code_required, student_code_not_allowed, student_code_exhausted, invalid_code_pattern, guardian_required, primary_guardian_required, guardian_link_exists, guardian_incomplete, critical_field, change_request_pending, change_request_closed, change_request_stale, invalid_level_type, password_change_required`) and `invalid_owner_unit, course_code_taken, course_in_use, course_not_available, allocation_exists, allocation_in_use, restriction_not_met, exception_pending, exception_closed, blueprint_draft_exists, blueprint_no_draft, blueprint_stale, blueprint_empty, class_code_taken, class_in_use, class_closed, class_full, over_capacity, year_closed, year_not_effective, class_course_exists, class_course_in_use, enrollment_not_optional, already_enrolled, not_in_class, already_placed, class_unit_mismatch, teacher_not_eligible, teacher_not_in_unit, primary_teacher_exists, already_in_group, not_enrolled, transfer_invalid, too_many_items`.
- `AuditCatalog.EntityTypes` + `StudentProfile, GuardianProfile, StudentGuardian, StudentChangeRequest` (if not added in M2) and `Course, CourseAllocation, CourseRestriction, CourseSupervisor, Blueprint, Class, ClassStudent, ClassCourse, ClassCourseTeacher, TeacherCourseEligibility, CourseEnrollment, EnrollmentException, StudentGroup, GroupMember, StudentTransfer`.
- Events (append): `StudentsChangeRequested`, `StudentsChangeRequestDecided` (M2 appendix); `TeachingAssigned = "Teaching.Assigned"` (named; params `ClassNameAr, ClassNameEn, CourseNameAr, CourseNameEn, Role`); `BlueprintsPushed = "Blueprints.Pushed"` (named teachers; `CourseNameAr, CourseNameEn, Version`); `EnrollmentExceptionRequested = "Enrollment.ExceptionRequested"` (named supervisors, else roles `[Manager]` `AncestorsAndSelf`; `StudentNameAr, StudentNameEn, CourseNameAr, CourseNameEn`); `EnrollmentExceptionDecided` (named requester; + `Decision`); `EnrollmentStudentTransferred = "Enrollment.StudentTransferred"` (named guardians + teachers of both classes; `StudentNameAr, StudentNameEn, FromClassAr, FromClassEn, ToClassAr, ToClassEn, EffectiveOn`).

- [ ] **Step 1: Write the failing tests** `Built_in_roles_match_the_M3_matrix_exactly` (literal sets); `No_built_in_role_holds_a_platform_only_permission`; `Every_relationship_rule_names_a_built_in_role_and_permissions_that_role_holds`; `Every_policy_key_blocks_a_permission_or_a_role`; `StringSet_defaults_are_subsets_of_allowed_values`; `Every_new_setting_key_is_in_the_catalog_with_matching_default_json`; `New_notification_events_have_unique_keys_and_valid_recipient_scope`; client parity tests stay green.
- [ ] **Step 2: Run** `dotnet test tests/Lms.UnitTests --filter "BuiltInRoleMatrixTests|ConfigurationContractTests|AcademicCatalogContractTests"` → FAIL.
- [ ] **Step 3: Implement.** **Step 4: Run** `dotnet test LMS.slnx` → PASS; 0 warnings.
- [ ] **Step 5: Commit** `feat(contracts): M3 permissions, relationship rules, academic policies, settings and catalogs`.

### Task 2: Contract — students, guardians, change requests, nationalities (from M2 appendix) — lane: Lead contract

**PRD:** FR-STU-01..06, D24–D27, D30. **Depends on:** Task 1.

**Files / Interfaces:** exactly the M2 plan appendix "Contract parts deferred from Tasks 1–3 → Task 2" (`PeopleCatalogs` additions `Genders, Religions, GuardianRelations, StudentCodeModes, StudentFields, ChangeRequestStatuses`; `Nationalities.cs`; `StudentDtos.cs`, `GuardianDtos.cs`, `ChangeRequestDtos.cs`; routes), with these M3 deltas:
- `CreateStudentRequest` gains `Guid? ClassId` (placement, Task 21) and `string? DefaultPassword` (blank = generated, Q9); new `StudentCreatedDto(StudentDetailDto Student, string IssuedPassword, string CredentialsSentTo)` with `CredentialsDelivery { Student, PrimaryGuardian, None }` — the only DTO that ever carries a password.
- `StudentSummaryDto` gains `Guid? CurrentClassId, string? CurrentClassNameAr, string? CurrentClassNameEn`; `StudentDetailDto` gains `bool MustChangePassword`; `StudentQuery` gains `Guid? ClassId = null, bool Unplaced = false, Guid? AcademicYearId = null`.
- `POST /api/students/{id}/resend-invitation` is replaced by `POST /api/students/{id}/reset-password` body `ResetStudentPasswordRequest(string? DefaultPassword)` → `StudentCreatedDto`.
- `ImportKinds.Students = "students"` (moved here from M2 Task 3).
- **Sensitive import columns (QA2 review of the import wizard):** `ImportColumnDto` gains a trailing `bool Sensitive = false` (`src/Lms.Shared/Imports/ImportDtos.cs`); the student `DefaultPassword` column is the first sensitive column. For a sensitive column the API never returns a value: `ImportRowDto.Values[key]` is `null` when empty and the fixed mask `"••••••"` when filled, in the preview/rows API and in the error report; errors on it are still reported (code + message only). The wizard shows the mask and never echoes the cell.

- [ ] **Step 1: Tests** `Catalog_values_are_unique`; `Import_column_dto_has_sensitive_flag_defaulting_to_false`; `Requestable_fields_exclude_StudentCode_and_Email`; `Default_critical_fields_are_requestable`; `Nationalities_have_two_letter_codes_and_both_names`; `People_routes_are_unique_and_start_with_api`; `Only_StudentCreatedDto_has_a_password_property` (reflection over `Lms.Shared`).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(contracts): student, guardian and change-request DTOs`.

### Task 3: Contract — courses, allocations, restrictions, supervisors, blueprints — lane: Lead contract

**PRD:** FR-CRS-01..04, FR-LP-01/02/04, D7. **Depends on:** Task 1.

**Files:**
- Create: `src/Lms.Shared/Courses/CourseDtos.cs`, `CourseCatalogs.cs`, `src/Lms.Shared/Blueprints/BlueprintDtos.cs`
- Modify: `src/Lms.Shared/Routes.cs`, `src/Lms.Shared/Imports/ImportDtos.cs` (`ImportKinds.Courses = "courses"`)
- Test: `tests/Lms.UnitTests/Contracts/AcademicCatalogContractTests.cs`

**Interfaces:**
- Catalogs: `CourseStatuses { Active, Inactive }`; `CourseRestrictionTypes { Religion, Gender, LevelType }` (Q3); `BlueprintStatuses { Draft, Published, Superseded }`; `BlueprintApplyModes { NewClassesOnly, PushToExisting }`; `CourseSorts { code, -code, name, -name }`.
- Courses: `CourseSummaryDto(Guid Id, string Code, string NameAr, string NameEn, string Language /* ISO 639-1 */, int WeeklyPeriods, Guid OwnerOrgUnitId, string OwnerOrgUnitNameAr, string OwnerOrgUnitNameEn, string Status, bool IsInherited, int AllocationCount, bool HasPublishedBlueprint)`; `CourseDetailDto(CourseSummaryDto Summary, string? DescriptionAr, string? DescriptionEn, IReadOnlyList<CourseRestrictionDto> Restrictions, IReadOnlyList<CourseSupervisorDto> Supervisors, bool CanEdit)`; `SaveCourseRequest(string Code, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn, string Language, int WeeklyPeriods /* 0..40 */, Guid OwnerOrgUnitId, IReadOnlyList<Guid>? AllocateToOrgUnitIds)`; `SetCourseStatusRequest(string Status)`; `CourseQuery(Guid? OrgUnitId, bool IncludeInherited = true, string? Status = null, string? Search = null, bool SupervisedByMe = false, string? Sort = null, int Page = 1, int PageSize = 50)`.
- Allocations: `CourseAllocationDto(Guid Id, Guid CourseId, string CourseCode, string CourseNameAr, string CourseNameEn, Guid OrgUnitId, string OrgUnitNameAr, string OrgUnitNameEn, bool IsInherited, int ClassCourseCount)`; `AllocateCoursesRequest(Guid OrgUnitId, IReadOnlyList<Guid> CourseIds)`; `UnitCoursesDto(IReadOnlyList<CourseAllocationDto> Available /* here + ancestors */, IReadOnlyList<CourseAllocationDto> AllocatedBelow)`.
- Restrictions: `CourseRestrictionInput(string Type, IReadOnlyList<string> Values)` (Religion/Gender values = catalog keys; LevelType values = level type ids); `CourseRestrictionDto(string Type, IReadOnlyList<string> Values)`; `SetCourseRestrictionsRequest(IReadOnlyList<CourseRestrictionInput> Restrictions)` (replace-all; empty = unrestricted).
- Supervisors: `CourseSupervisorDto(Guid UserId, string NameAr, string NameEn, string? Email)`; `SetCourseSupervisorsRequest(IReadOnlyList<Guid> UserIds)`.
- Blueprints: `BlueprintSummaryDto(Guid Id, Guid CourseId, int Version /* 0 = draft */, string Status, int Revision, DateTime? PublishedAt, string? PublishedByNameAr, string? PublishedByNameEn, string? ApplyMode, int SectionCount, int LessonCount)`; `BlueprintLessonDto(Guid Key, int Order, string TitleAr, string TitleEn, string? ObjectivesAr, string? ObjectivesEn, int? PlannedOffsetDays)`; `BlueprintSectionDto(Guid Key, int Order, string TitleAr, string TitleEn, IReadOnlyList<BlueprintLessonDto> Lessons)`; `BlueprintDto(BlueprintSummaryDto Summary, IReadOnlyList<BlueprintSectionDto> Sections)`; `BlueprintLessonInput(Guid? Key, string TitleAr, string TitleEn, string? ObjectivesAr, string? ObjectivesEn, int? PlannedOffsetDays /* 0..400 */)`; `BlueprintSectionInput(Guid? Key, string TitleAr, string TitleEn, IReadOnlyList<BlueprintLessonInput> Lessons)`; `SaveBlueprintDraftRequest(int Revision, IReadOnlyList<BlueprintSectionInput> Sections)` (whole tree; order = list position; null key = new); `PublishBlueprintRequest(string ApplyMode)`; `PublishBlueprintResultDto(BlueprintSummaryDto Blueprint, int ClassesQueued)`; `CopyBlueprintRequest(Guid SourceCourseId, int? SourceVersion /* null = latest published */)`.
- Routes: `Courses = "/api/courses"` + `/{id}`, `/{id}/status`, `/{id}/restrictions`, `/{id}/supervisors`, `/{id}/blueprints`, `/{id}/blueprints/{version:int}`, `/{id}/blueprints/draft`, `/{id}/blueprints/draft/publish`, `/{id}/blueprints/draft/copy-from`; `CourseAllocations = "/api/course-allocations"` + `/{id}`; `UnitCourses(Guid orgUnitId) = "/api/org-units/{id}/courses"`; `MySupervisedCourses = "/api/me/supervised-courses"`.

- [ ] **Step 1: Tests** `Restriction_types_and_apply_modes_are_unique`; `Course_routes_are_unique_and_start_with_api`; `Import_kinds_are_lower_case_route_segments` (Courses added).
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(contracts): course, allocation, restriction, supervisor and blueprint DTOs`.

### Task 4: Contract — classes, class courses, teachers, enrollment, exceptions, groups, transfers, lesson plans — lane: Lead contract

**PRD:** FR-CLS-01..05, FR-TCH-01..03, FR-ENR-01..05, FR-GRP-01..03, FR-LP-03/05, FR-CRS-03 (exceptions). **Depends on:** Task 1.

**Files:**
- Create: `src/Lms.Shared/Classes/ClassDtos.cs`, `src/Lms.Shared/Teaching/TeachingDtos.cs`, `src/Lms.Shared/Enrollment/EnrollmentDtos.cs`, `src/Lms.Shared/Enrollment/GroupDtos.cs`, `src/Lms.Shared/Classes/LessonPlanDtos.cs`
- Modify: `src/Lms.Shared/Routes.cs`, `src/Lms.Shared/Imports/ImportDtos.cs` (`ImportKinds.ClassStudents = "class-students"`)
- Test: `tests/Lms.UnitTests/Contracts/AcademicCatalogContractTests.cs`

**Interfaces:**
- Catalogs: `ClassStatuses { Active, Closed }`; `ClassStudentStatuses { Active, TransferredOut, Removed }` (M7 adds Promoted/Graduated/Withdrawn); `EnrollmentSources { Auto, Manual, Exceptional, Transfer }`; `EnrollmentStatuses { Active, Dropped }`; `TeacherRoles { Primary, Assistant }`; `ExceptionStatuses { Pending, Approved, Rejected, Cancelled }`.
- Classes: `ClassSummaryDto(Guid Id, string Code, string NameAr, string NameEn, Guid OrgUnitId, string OrgUnitNameAr, string OrgUnitNameEn, Guid AcademicYearId, string YearNameAr, string YearNameEn, int? Capacity, int StudentCount, int CourseCount, Guid? HomeroomTeacherUserId, string? HomeroomTeacherNameAr, string? HomeroomTeacherNameEn, string Status, bool IsReadOnly)`; `SaveClassRequest(string Code, string NameAr, string NameEn, Guid OrgUnitId, Guid AcademicYearId, int? Capacity /* 1..500 */, Guid? HomeroomTeacherUserId)`; `BulkCreateClassesRequest(Guid OrgUnitId, Guid AcademicYearId, string NamePatternAr, string NamePatternEn, string CodePattern, int From, int To, int? Capacity)` (`{N}` exactly once in each pattern); `CopyClassesRequest(Guid SourceYearId, Guid TargetYearId, Guid OrgUnitId, bool IncludeCourses, string? ReplaceFrom, string? ReplaceTo)` (text replace on names and codes, e.g. `2026` → `2027`); `CopyClassesResultDto(int Created, IReadOnlyList<string> SkippedCodes)`; `ClassQuery(Guid? OrgUnitId, bool IncludeDescendants = true, Guid? AcademicYearId = null, string? Status = null, string? Search = null, string? Sort = null, int Page = 1, int PageSize = 50)`.
- ClassCourses: `ClassCourseDto(Guid Id, Guid ClassId, string ClassNameAr, string ClassNameEn, Guid CourseId, string CourseCode, string CourseNameAr, string CourseNameEn, bool IsOptional, int EnrolledCount, IReadOnlyList<ClassCourseTeacherDto> CurrentTeachers, int? LessonPlanVersion)`; `AddClassCoursesRequest(IReadOnlyList<Guid> CourseIds, bool IsOptional)`; `UpdateClassCourseRequest(bool IsOptional)`; `BulkAssignCourseRequest(Guid OrgUnitId, Guid AcademicYearId, Guid CourseId, bool IsOptional)` (every live class at the unit and below); `DistributionMatrixDto(IReadOnlyList<ClassSummaryDto> Classes, IReadOnlyList<CourseSummaryDto> Courses, IReadOnlyList<MatrixCellDto> Cells)`; `MatrixCellDto(Guid ClassId, Guid CourseId, Guid ClassCourseId, bool IsOptional)`.
- Teaching: `TeacherEligibilityDto(Guid Id, Guid UserId, string TeacherNameAr, string TeacherNameEn, Guid CourseId, string CourseCode, string CourseNameAr, string CourseNameEn, Guid OrgUnitId, string OrgUnitNameAr, string OrgUnitNameEn)`; `GrantEligibilityRequest(Guid OrgUnitId, IReadOnlyList<Guid> UserIds, IReadOnlyList<Guid> CourseIds)` (cross product, ≤ 200 pairs); `EligibilityQuery(Guid? OrgUnitId, Guid? UserId, Guid? CourseId, int Page = 1, int PageSize = 50)`; `ClassCourseTeacherDto(Guid Id, Guid UserId, string NameAr, string NameEn, string Role, DateOnly From, DateOnly? To, bool IsCurrent)`; `AssignTeacherRequest(Guid UserId, string Role, DateOnly? From)`; `ReplaceTeacherRequest(Guid NewUserId, DateOnly EffectiveOn)`; `EndTeacherRequest(DateOnly LastDay)`; `BulkAssignTeacherRequest(Guid UserId, string Role, IReadOnlyList<Guid> ClassCourseIds)`; `MyClassCourseDto(Guid ClassCourseId, Guid ClassId, string ClassNameAr, string ClassNameEn, string CourseNameAr, string CourseNameEn, string OrgUnitNameAr, string OrgUnitNameEn, string? TeacherRole, int StudentCount)`.
- Enrollment: `ClassStudentDto(Guid Id, Guid StudentId, string StudentCode, string NameAr, string NameEn, string Gender, DateOnly From, DateOnly? To, string Status)`; `AddStudentsToClassRequest(IReadOnlyList<Guid> StudentIds, DateOnly? EffectiveOn, bool ConfirmOverCapacity)`; `RestrictionSkipDto(Guid StudentId, Guid ClassCourseId, IReadOnlyList<string> FailedTypes)`; `AddStudentsResultDto(int Added, int EnrollmentsCreated, IReadOnlyList<RestrictionSkipDto> RestrictionSkips, bool OverCapacity)`; `RemoveFromClassRequest(DateOnly EffectiveOn, string Reason)`; `EnrollmentDto(Guid Id, Guid ClassCourseId, Guid StudentId, string StudentCode, string StudentNameAr, string StudentNameEn, string CourseCode, string CourseNameAr, string CourseNameEn, string ClassNameAr, string ClassNameEn, string Source, string Status, DateOnly From, DateOnly? To, string? DropReason, Guid? GroupId, string? GroupNameAr, string? GroupNameEn)`; `EnrollStudentsRequest(IReadOnlyList<Guid> StudentIds)`; `DropEnrollmentRequest(string Reason, DateOnly? EffectiveOn)`; `EnrollmentQuery(Guid? StudentId, Guid? ClassCourseId, string? Status = "Active", int Page = 1, int PageSize = 50)`.
- Exceptions: `RequestExceptionRequest(Guid StudentId, string Reason)`; `DecideExceptionRequest(string? Comment)`; `EnrollmentExceptionDto(Guid Id, Guid ClassCourseId, Guid StudentId, string StudentCode, string StudentNameAr, string StudentNameEn, string CourseNameAr, string CourseNameEn, string ClassNameAr, string ClassNameEn, IReadOnlyList<string> FailedTypes, string Reason, string Status, string? RequestedByNameAr, string? RequestedByNameEn, DateTime CreatedAt, string? DecidedByNameAr, string? DecidedByNameEn, DateTime? DecidedAt, string? DecisionComment)`; `ExceptionQuery(Guid? OrgUnitId, string? Status = "Pending", int Page = 1, int PageSize = 50)`.
- Groups: `StudentGroupDto(Guid Id, Guid ClassCourseId, string NameAr, string NameEn, string? MatchType, string? MatchValue, int MemberCount)`; `SaveGroupRequest(string NameAr, string NameEn, string? MatchType, string? MatchValue)` (optional restriction type/value used for suggestions); `GroupMembersRequest(IReadOnlyList<Guid> StudentIds)`; `GroupMemberDto(Guid StudentId, string StudentCode, string NameAr, string NameEn)`.
- Transfers: `TransferStudentRequest(Guid StudentId, Guid ToClassId, DateOnly EffectiveOn, string Reason, bool ConfirmOverCapacity)`; `TransferPreviewDto(IReadOnlyList<string> KeptCourseCodes, IReadOnlyList<string> DroppedCourseCodes, IReadOnlyList<string> AddedCourseCodes, IReadOnlyList<string> RestrictedCourseCodes, bool OverCapacity)`; `StudentTransferDto(Guid Id, Guid StudentId, string FromClassNameAr, string FromClassNameEn, string ToClassNameAr, string ToClassNameEn, DateOnly EffectiveOn, string Reason, string? ByNameAr, string? ByNameEn, DateTime CreatedAt)`.
- Lesson plans: `ClassLessonDto(Guid Key, int Order, string TitleAr, string TitleEn, string? ObjectivesAr, string? ObjectivesEn, DateOnly? PlannedDate, bool IsHidden)`; `ClassSectionDto(Guid Key, int Order, string TitleAr, string TitleEn, bool IsHidden, IReadOnlyList<ClassLessonDto> Lessons)`; `ClassLessonPlanDto(Guid ClassCourseId, int? BlueprintVersion, DateTime? SyncedAt, string SectionLabel, IReadOnlyList<ClassSectionDto> Sections)`.
- Routes: `Classes = "/api/classes"` + `/{id}`, `/{id}/close`, `/bulk`, `/copy`, `/matrix` (`?orgUnitId&yearId`), `/{id}/students`, `/{id}/students/{classStudentId}/remove`, `/{id}/courses`; `ClassCourses = "/api/class-courses"` + `/{id}`, `/bulk-assign`, `/{id}/teachers`, `/{id}/teachers/{assignmentId}/replace`, `/{id}/teachers/{assignmentId}/end`, `/{id}/enrollments`, `/{id}/exceptions`, `/{id}/groups`, `/{id}/lesson-plan`; `TeacherEligibility = "/api/teacher-eligibility"` + `/{id}`; `TeacherAssignmentsBulk = "/api/teacher-assignments/bulk"`; `MyClassCourses = "/api/me/class-courses"`; `Enrollments = "/api/enrollments"` + `/{id}/drop`; `EnrollmentExceptions = "/api/enrollment-exceptions"` + `/{id}/approve`, `/{id}/reject`, `/{id}/cancel`; `Groups = "/api/groups"` + `/{id}`, `/{id}/members`, `/{id}/members/remove`, `/{id}/suggestions`; `Transfers = "/api/transfers"` + `/preview`; `StudentTransfers(Guid id) = "/api/students/{id}/transfers"`, `StudentEnrollments(Guid id) = "/api/students/{id}/enrollments"`.

- [ ] **Step 1: Tests** `Academic_catalog_values_are_unique`; `Academic_routes_are_unique_and_start_with_api`; `Import_kinds_are_lower_case_route_segments`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(contracts): class, teaching, enrollment, group, transfer and lesson-plan DTOs`.

### Task 5: Academic kernel — entities, migration, live keys, restriction evaluator, lifecycle hooks, usage checks, course catalog — lane: BE1

**PRD:** §7, §4.2, FR-ORG-04 (classes/courses block unit delete), FR-CRS-02/03 (availability, evaluator), FR-GRP-02/D6 (unique), X-03. **Depends on:** Tasks 1, 3, 4; M2 #181 (people kernel). Kernel for DEV-PLAN items 1–13. **Critical path: review first.**

**Files:**
- Create: `src/Lms.Domain/Courses/Course.cs`, `CourseAllocation.cs`, `CourseRestriction.cs`, `CourseSupervisor.cs`, `CourseRestrictionEvaluator.cs`, `ICourseCatalog.cs`; `src/Lms.Domain/Classes/Class.cs`, `ClassStudent.cs`, `ClassCourse.cs`, `IClassCourseLifecycleHandler.cs`; `src/Lms.Domain/Teaching/TeacherCourseEligibility.cs`, `ClassCourseTeacher.cs`; `src/Lms.Domain/Enrollment/CourseEnrollment.cs`, `StudentGroup.cs`, `GroupMember.cs`, `StudentTransfer.cs`, `IEnrollmentEngine.cs` (interface only; implementation Task 24); `src/Lms.Domain/Common/ILiveKeyed.cs`
- Create: `src/Lms.Infrastructure/Courses/CourseCatalog.cs`, `CoursesUsageCheck.cs`, `src/Lms.Infrastructure/Classes/ClassesUsageCheck.cs`, `AcademicServiceCollectionExtensions.cs`; `Persistence/Configurations/Academic/*Configuration.cs`; migration `AddAcademicStructure`
- Modify: `src/Lms.Infrastructure/Persistence/Interceptors/StampingInterceptor.cs` (call `ILiveKeyed.ClearLiveKeys()` on soft delete), `tests/Lms.UnitTests/Architecture/IsolationBypassGuardTests.cs` (allow-list `CourseCatalog.cs`)
- Test: `tests/Lms.UnitTests/Courses/CourseRestrictionEvaluatorTests.cs`, `tests/Lms.IntegrationTests/Academic/AcademicIsolationTests.cs`, `LiveKeyTests.cs`, `CourseCatalogTests.cs`, `AcademicUsageCheckTests.cs`

**Interfaces:**
- `[Audited] Course : AuditableEntity, IOrgUnitScoped, ILiveKeyed { string Code, string? CodeKey, NameAr, NameEn, DescriptionAr?, DescriptionEn?, string Language, int WeeklyPeriods, CourseStatus Status }` (unit = owner); `Update(...)`, `SetStatus`, `MoveOwner(OrgUnit)` (re-points copied children in the same transaction).
- `[Audited] CourseAllocation { Guid CourseId, Guid? CourseKey }` (unit = allocated unit). `[Audited] CourseRestriction { Guid CourseId, string Type, string ValuesJson }` (one row per type). `[Audited] CourseSupervisor { Guid CourseId, Guid UserId, Guid? LiveKey }`.
- `[Audited] Class : AuditableEntity, IOrgUnitScoped, ILiveKeyed { Guid AcademicYearId, string Code, string? CodeKey, NameAr, NameEn, int? Capacity, Guid? HomeroomTeacherUserId, ClassStatus Status }`. `[Audited] ClassStudent { Guid ClassId, Guid StudentId, Guid AcademicYearId, DateOnly From, DateOnly? To, ClassStudentStatus Status, Guid? ActiveYearKey, string? EndReason }`. `[Audited] ClassCourse { Guid ClassId, Guid CourseId, Guid? CourseKey, bool IsOptional }`.
- `[Audited] TeacherCourseEligibility { Guid UserId, Guid CourseId, Guid? LiveKey }`. `[Audited] ClassCourseTeacher { Guid ClassCourseId, Guid UserId, TeacherRole Role, DateOnly From, DateOnly? To, Guid? OpenPrimaryKey }`.
- `[Audited] CourseEnrollment { Guid ClassCourseId, Guid StudentId, Guid ClassStudentId, EnrollmentSource Source, EnrollmentStatus Status, DateOnly From, DateOnly? To, string? DropReason, Guid? CarriedFromEnrollmentId, Guid? ActiveKey }`. `[Audited] StudentGroup { Guid ClassCourseId, NameAr, NameEn, string? MatchType, string? MatchValue }`. `[Audited] GroupMember { Guid GroupId, Guid ClassCourseId, Guid StudentId, Guid EnrollmentId, Guid? LiveKey }`. `[Audited] StudentTransfer { Guid StudentId, Guid FromClassId, Guid ToClassId, string FromOrgUnitPath, DateOnly EffectiveOn, string Reason }` (append-only).
- Unique indexes per Global Constraints "Live keys"; filter indexes `(OrganizationId, OrgUnitPath, AcademicYearId)` on `Class`, `(StudentId, Status)` on `CourseEnrollment`/`ClassStudent`.
- `CourseRestrictionEvaluator.Failed(IReadOnlyList<(string Type, IReadOnlyList<string> Values)> restrictions, string gender, string religion, Guid classUnitLevelTypeId) : IReadOnlyList<string>` — AND across types, any-of within a type (Q3); pure.
- `ICourseCatalog` (`ISOLATION-BYPASS` per Global Constraints): `FindAsync(Guid courseId) : Task<CourseView?>` (visible = owner in scope or owner an ancestor of the caller's scope; `CourseView(Course Course, bool IsInherited)`); `IsAvailableAtAsync(Guid courseId, OrgUnit unit) : Task<bool>`; `AvailableAtAsync(OrgUnit unit) : Task<IReadOnlyList<(CourseAllocation Allocation, Course Course, bool IsInherited)>>`; `ListVisibleAsync(CourseQuery q)` (paged).
- `IClassCourseLifecycleHandler { Task OnAddedAsync(ClassCourse cc, CancellationToken); Task OnRemovingAsync(ClassCourse cc, CancellationToken) /* throw ClassCourseInUseException to block */; Task OnOptionalChangedAsync(ClassCourse cc, CancellationToken); }` — all registered handlers are called in registration order inside the caller's transaction (Task 14 calls them; Tasks 17 and 24 register handlers).
- `IEnrollmentEngine` (signatures in Task 24).
- `CoursesUsageCheck : IOrgUnitUsageCheck` → `"courses"` (live courses owned or allocated at the unit); `ClassesUsageCheck` → `"classes"`.

- [ ] **Step 1: Write the failing tests** (unit) `Religion_restriction_passes_only_listed_values`; `Two_types_are_ANDed`; `No_restriction_passes_everyone`; (integration) `Every_academic_table_is_isolated_by_client_and_subtree` (client A/B, School1/School2, parent sees both — one case per entity); `Second_active_class_for_same_student_and_year_violates_unique` and `Removed_placement_frees_the_slot`; `Student_cannot_be_in_two_groups_of_one_class_course_even_concurrently` (D6, two parallel saves); `Soft_deleted_course_frees_its_code`; `Catalog_returns_ancestor_owned_course_as_inherited_but_not_sibling_or_other_client` (Review Focus 2); `Availability_follows_allocation_at_unit_or_ancestor`; `Unit_with_classes_or_courses_cannot_be_deleted`; `Repather_covers_academic_tables`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.UnitTests --filter CourseRestrictionEvaluatorTests` and `dotnet test tests/Lms.IntegrationTests --filter "AcademicIsolationTests|LiveKeyTests|CourseCatalogTests|AcademicUsageCheckTests"` → FAIL.
- [ ] **Step 3: Implement.** **Step 4: Run** → PASS; `dotnet ef migrations script` up and down reviewed.
- [ ] **Step 5: Commit** `feat(academic): academic kernel with courses, classes, enrollment entities and course catalog`.

### Task 6: Courses CRUD, status, delete rules and Excel import — lane: BE1

**PRD:** FR-CRS-01, X-06, X-09, §4.3 `CreateCourses`. **Depends on:** Tasks 3, 5, 12; import part: M2 #159 / PR #202. DEV-PLAN item 1.

**Files:**
- Create: `src/Lms.Api/Features/Courses/CoursesModule.cs`, `CourseEndpoints.cs`, `CourseHandlers.cs`, `CourseValidators.cs`, `CourseMapper.cs`, `Import/CourseImportDefinition.cs`
- Modify: `src/Lms.Api/Resources/ImportResource.{ar,en}.resx` (course codes)
- Test: `tests/Lms.IntegrationTests/Courses/CourseEndpointsTests.cs`, `CourseImportTests.cs`

**Interfaces:**
- `POST /api/courses` body `SaveCourseRequest` → 201 `CourseDetailDto` — `Courses.Create` + `CheckUnitAsync(Courses.Create, OwnerOrgUnitId)` (policy `CreateCourses`); owner rule (Q1) → 400 `invalid_owner_unit`; code → 409 `course_code_taken`; language `^[a-z]{2}$`; a Supervisor creator becomes the course's first supervisor. (`AllocateToOrgUnitIds` is handled in Task 7; Task 6 rejects a non-empty list with 400 until then.)
- `GET /api/courses` (`CourseQuery`) → `PagedResult<CourseSummaryDto>` via `ICourseCatalog.ListVisibleAsync`; `SupervisedByMe` filters by the caller's `CourseSupervisor` rows. `GET /api/courses/{id}` → `CourseDetailDto` (`CanEdit` = caller passes `Courses.Update` at the owner unit and relationship). Inherited/invisible → as `ICourseCatalog.FindAsync` (404 when null).
- `PUT /api/courses/{id}`, `PUT /{id}/status`, `DELETE /{id}` — `Courses.Update` / `Courses.Update` / `Courses.Delete` + `CheckUnitAsync` at owner + `IRelationshipScope` (Supervisor → supervised). Moving the owner needs `Courses.Create` at the new owner. Delete with live allocations or ClassCourses → 409 `course_in_use`; deactivate always allowed (inactive courses cannot be newly allocated or added to classes).
- Import `Kind = "courses"`, `Permission = Courses.Import`; columns `Code` ✓, `NameAr` ✓, `NameEn` ✓, `DescriptionAr`, `DescriptionEn`, `Language` ✓, `WeeklyPeriods` ✓, `OwnerUnitCode` ✓ (Task 7 adds `AllocateToUnitCodes`); row errors `course_code_taken`, `duplicate_in_file` (code), `invalid_owner_unit`, `unknown_unit`, `blocked_by_policy`.

- [ ] **Step 1: Write the failing tests** `Manager_creates_course_owned_by_school`; `Owner_without_course_management_flag_is_400`; `Code_taken_in_sibling_school_is_409`; `CreateCourses_policy_blocks_create_and_import`; `School_admin_sees_root_course_as_inherited_read_only_and_PUT_is_404`; `Supervisor_creator_becomes_supervisor`; `Supervisor_cannot_edit_unsupervised_course_404` (Review Focus 1); `Delete_course_in_use_is_409_and_deactivate_works`; `List_search_by_code_prefix_and_arabic_name`; `Course_changes_are_audited`; `Import_of_40_courses_all_or_nothing_with_error_report`; `Teacher_gets_403_on_create`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(courses): course catalog CRUD, status and Excel import`.

### Task 7: Course allocations, courses per node, allocate at creation — lane: BE1

**PRD:** FR-CRS-02. **Depends on:** Task 6. DEV-PLAN item 2.

**Files:**
- Create: `src/Lms.Api/Features/Courses/AllocationEndpoints.cs`, `AllocationHandlers.cs`
- Modify: `CourseHandlers.cs` (`AllocateToOrgUnitIds` on create), `Import/CourseImportDefinition.cs` (`AllocateToUnitCodes`, `;`-separated)
- Test: `tests/Lms.IntegrationTests/Courses/AllocationTests.cs`

**Interfaces:**
- `POST /api/course-allocations` body `AllocateCoursesRequest` → `IReadOnlyList<CourseAllocationDto>` — `Courses.Allocate` + `CheckUnitAsync(Courses.Allocate, OrgUnitId)` + per course: visible and Active, owner is ancestor-or-self of the unit (400 `course_not_available`), relationship (Supervisor → supervised); already allocated at that unit → 409 `allocation_exists`; one transaction.
- `DELETE /api/course-allocations/{id}` — `Courses.Allocate` + unit; 409 `allocation_in_use` when a live ClassCourse at or below the unit uses the course and no other allocation at an ancestor-or-self of that class's unit covers it.
- `GET /api/org-units/{id}/courses` → `UnitCoursesDto` — `Courses.View` + unit; `Available` from `ICourseCatalog.AvailableAtAsync`, `AllocatedBelow` = allocations in the unit's subtree.

- [ ] **Step 1: Write the failing tests** `Root_course_allocated_at_school_is_available_to_its_grades`; `Course_owned_by_School1_cannot_be_allocated_in_School2_404_or_400` (Review Focus 2); `Duplicate_allocation_is_409`; `Removing_allocation_used_by_class_is_409_unless_covered_above`; `Allocate_at_creation_creates_rows_in_same_transaction`; `Inactive_course_cannot_be_allocated`; `Allocation_is_audited`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(courses): course allocations in the tree`.

### Task 8: Course restrictions API — lane: BE1

**PRD:** FR-CRS-03 (catalog, set on a course). **Depends on:** Task 6. DEV-PLAN item 3 (part 1 of 2 — split: exceptions are Task 11).

**Files:**
- Create: `src/Lms.Api/Features/Courses/RestrictionEndpoints.cs`
- Test: `tests/Lms.IntegrationTests/Courses/RestrictionTests.cs`

**Interfaces:**
- `PUT /api/courses/{id}/restrictions` body `SetCourseRestrictionsRequest` → `IReadOnlyList<CourseRestrictionDto>` — `Courses.Update` + owner unit + relationship; replace-all; values validated (`Religions.All`, `Genders.All`, live level type ids of the client); a type at most once; empty list removes all. Existing enrollments are **not** changed (the change applies to new enrollments; the response header `X-Lms-Affected-Enrollments` reports how many active enrollments no longer pass, for the UI warning).
- Restrictions are returned in `CourseDetailDto` (Task 6 mapper).

- [ ] **Step 1: Write the failing tests** `Islamic_religion_course_restricted_to_Muslim`; `Unknown_value_or_duplicate_type_is_400`; `Level_type_of_other_client_is_400`; `Clearing_restrictions_makes_course_unrestricted`; `Affected_enrollments_are_counted_not_dropped`; `Restriction_change_is_audited`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(courses): course restrictions`.

### Task 9: Course supervisors and "My courses" — lane: BE1

**PRD:** FR-CRS-04, §3.3 (Supervisor relationship). **Depends on:** Tasks 6, 12. DEV-PLAN item 4.

**Files:**
- Create: `src/Lms.Api/Features/Courses/SupervisorEndpoints.cs`
- Test: `tests/Lms.IntegrationTests/Courses/SupervisorTests.cs`

**Interfaces:**
- `PUT /api/courses/{id}/supervisors` body `SetCourseSupervisorsRequest` → `IReadOnlyList<CourseSupervisorDto>` — `Courses.ManageSupervisors` + owner unit; each user must hold a valid Supervisor-role assignment at the owner unit or an ancestor-or-self/descendant… **rule:** at a unit whose path is a prefix of, or prefixed by, the owner unit path (400 `validation_failed` otherwise); replace-all (soft-delete removed rows).
- `GET /api/me/supervised-courses` → `IReadOnlyList<CourseSummaryDto>` — `Courses.View`; the caller's live supervisor rows in the active client and scope.

- [ ] **Step 1: Write the failing tests** `Manager_assigns_two_supervisors`; `Non_supervisor_user_is_400`; `My_courses_lists_only_supervised_in_active_context`; `Removed_supervisor_loses_blueprint_edit_404`; `Supervisor_cannot_manage_supervisors_403`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(courses): course supervisors and my courses`.

### Task 10: Copy branch and templates with courses and allocations (FR-ORG-06 remainder) — lane: BE1

**PRD:** FR-ORG-06 ("with courses/allocations"), FR-ORG-07/08. **Depends on:** Task 7; M1 #50 (branch snapshot) and #52 (templates) merged — M1 Task 15 deferred courses/allocations to M3.

**Files:**
- Modify: `src/Lms.Infrastructure/Branches/BranchSnapshot.cs`, `BranchSnapshotter.cs`, `BranchMaterializer.cs` (names as delivered by #50), `src/Lms.Shared/Organizations/BranchDtos.cs` (**Lead adds** `bool IncludeCourses` to the copy and save-template requests in Task 3's PR or a follow-up contract PR)
- Test: `tests/Lms.IntegrationTests/Branches/CopyBranchCoursesTests.cs`

**Interfaces:**
- `BranchNode` gains `IReadOnlyList<Guid> AllocatedCourseIds` and `IReadOnlyList<BranchCourse> OwnedCourses` (`BranchCourse(int Key, string Code, NameAr, NameEn, DescriptionAr?, DescriptionEn?, Language, WeeklyPeriods, IReadOnlyList<(string Type, IReadOnlyList<string> Values)> Restrictions)`); materialize: courses owned inside the branch are created as new courses with `code + CodeSuffix` (409 `course_code_taken` lists conflicts, nothing created); allocations of courses owned outside the branch are copied only when the course is still available at the new location, else reported as skipped. Classes, blueprints and supervisors are never copied.

- [ ] **Step 1: Write the failing tests** `Copy_with_courses_creates_owned_courses_and_allocations`; `Allocation_of_outside_course_not_available_at_target_is_skipped_and_reported`; `Course_code_conflict_creates_nothing`; `Template_with_courses_applies_with_placeholders`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(branches): copy branches and templates with courses and allocations`.

### Task 11: Exceptional enrollment — request, approve, reject — lane: BE1

**PRD:** FR-CRS-03 ("exceptional enrollment requires Supervisor approval"), X-07. **Depends on:** Tasks 8, 12, 24. DEV-PLAN item 3 (part 2 of 2). *If BE1 is still busy when Task 24 merges, BE4 takes this task.*

**Files:**
- Create: `src/Lms.Domain/Enrollment/EnrollmentException.cs`; configuration; migration `AddEnrollmentExceptions`; `src/Lms.Api/Features/Enrollment/Exceptions/ExceptionEndpoints.cs`
- Test: `tests/Lms.IntegrationTests/Enrollment/EnrollmentExceptionTests.cs`

**Interfaces:**
- `[Audited] EnrollmentException : AuditableEntity, IOrgUnitScoped { Guid ClassCourseId, Guid StudentId, string FailedTypesJson, string Reason, ExceptionStatus Status, Guid RequestedBy, Guid? DecidedBy, DateTime? DecidedAt, string? DecisionComment }`.
- `POST /api/class-courses/{id}/exceptions` body `RequestExceptionRequest` → 201 — `Enrollment.RequestException` + class unit; student active in the class (400 `not_in_class`), currently fails a restriction (else 409 `validation_failed` — enroll normally), no pending request for the pair (409 `exception_pending`); notifies `Enrollment.ExceptionRequested` (course supervisors; none → Managers `AncestorsAndSelf`).
- `GET /api/enrollment-exceptions` (`ExceptionQuery`) — `Enrollment.ApproveException` + unit + relationship (Supervisor → supervised courses only). `POST /{id}/approve` → `IEnrollmentEngine.EnrollManualAsync(cc, student, Exceptional)`; `POST /{id}/reject` (comment required); `POST /{id}/cancel` (requester); decided → 409 `exception_closed`; notifies `Enrollment.ExceptionDecided`.

- [ ] **Step 1: Write the failing tests** `Counselor_requests_exception_for_christian_student_in_islamic_course_and_supervisor_is_notified`; `Supervisor_approves_and_enrollment_is_Exceptional`; `Supervisor_of_other_course_gets_404` (Review Focus 1); `Course_without_supervisor_notifies_school_manager`; `Reject_requires_comment`; `Second_pending_request_is_409`; `Student_passing_restrictions_cannot_request`; `Exception_rows_are_isolated` (isolation `EnrollmentException`).
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(enrollment): exceptional enrollment requests and approval`.

### Task 12: Relationship scopes — Teacher own ClassCourses, Supervisor supervised courses, Student own enrollments — lane: BE2

**PRD:** §3.3 step 2, §3.4 (Teacher "own", Supervisor "supervised"), D32, FR-TCH-02/03 ("teacher sees only own ClassCourses"). **Depends on:** Tasks 1, 5. **First task of BE2; most feature tasks depend on it.**

**Files:**
- Create: `src/Lms.Domain/Access/IRelationshipScope.cs`, `RelationshipFilter.cs`; `src/Lms.Infrastructure/Access/RelationshipScope.cs`; `src/Lms.Api/Infrastructure/RelationshipAccess.cs`
- Test: `tests/Lms.IntegrationTests/Access/RelationshipScopeTests.cs`

**Interfaces:**
- `IRelationshipScope.ForAsync(string permission, CancellationToken) : Task<RelationshipFilter>` — reads the active assignment's role; `RelationshipRules.For(role.BuiltInKey, permission)` null → `RelationshipFilter.Unrestricted`; `OwnClassCourses` → ids of ClassCourses where the caller has a `ClassCourseTeacher` row with `From ≤ today ≤ To ?? ∞` (+ `HomeroomClassIds`); `SupervisedCourses` → course ids of live `CourseSupervisor` rows of the caller; `OwnEnrollments` → ClassCourse ids of the caller's student profile's active enrollments. Cached per request.
- `RelationshipFilter { bool IsUnrestricted; IReadOnlySet<Guid> ClassCourseIds; IReadOnlySet<Guid> ClassIds /* via class courses + homeroom */; IReadOnlySet<Guid> CourseIds; bool AllowsClassCourse(Guid); bool AllowsClass(Guid); bool AllowsCourse(Guid); IQueryable<StudentProfile> Students(IQueryable<StudentProfile>, LmsDbContext) /* active enrollment in an allowed ClassCourse or active placement in an allowed class */ }`.
- `RelationshipAccess` (API): `CheckClassCourseAsync(this HttpContext, string permission, Guid classCourseId, CancellationToken) : Task<(ClassCourse? Cc, IResult? Problem)>` = `CheckUnitAsync` at the class unit + filter, miss → 404; `CheckClassAsync`, `CheckCourseAsync` (owner unit), `CheckStudentAsync` likewise.

- [ ] **Step 1: Write the failing tests** `Teacher_filter_contains_only_current_class_courses` (ended assignment excluded, future excluded); `Homeroom_teacher_sees_class_and_its_students`; `Supervisor_filter_contains_only_supervised_courses`; `OrgAdmin_and_custom_role_are_unrestricted`; `Student_filter_is_own_enrollments`; `Check_class_course_returns_404_for_foreign_class_course_of_same_school` (Review Focus 1); `Filter_is_computed_once_per_request`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.IntegrationTests --filter RelationshipScopeTests` → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(access): relationship scopes for teachers, supervisors and students`.

### Task 13: Classes CRUD, bulk create, copy to next year, close — lane: BE2

**PRD:** FR-CLS-01, FR-CLS-02, FR-CLS-04, X-04, §4.3 `CreateClasses`. **Depends on:** Tasks 4, 5, 12. DEV-PLAN item 6.

**Files:**
- Create: `src/Lms.Api/Features/Classes/ClassesModule.cs`, `ClassEndpoints.cs`, `ClassHandlers.cs`, `BulkClassHandlers.cs`, `ClassValidators.cs`, `ClassMapper.cs`
- Test: `tests/Lms.IntegrationTests/Classes/ClassEndpointsTests.cs`, `BulkClassesTests.cs`

**Interfaces:**
- `POST /api/classes` body `SaveClassRequest` → 201 `ClassSummaryDto` — `Classes.Create` + `CheckUnitAsync` (policy `CreateClasses`); unit level type `CanHostClasses` (400 `invalid_level_type`); year effective at the unit (400 `year_not_effective`) and not Closed (409 `year_closed`); code (409 `class_code_taken`); homeroom teacher: Teacher-role assignment at an ancestor-or-self unit (400 `teacher_not_in_unit`).
- `GET /api/classes` (`ClassQuery`, default year = current at the queried unit) → `PagedResult<ClassSummaryDto>` — `Classes.View` + unit + relationship filter. `GET /api/classes/{id}`.
- `PUT /api/classes/{id}` — `Classes.Update`; capacity below current active students → 400; moving unit/year not allowed (400). `DELETE /api/classes/{id}` — `Classes.Delete`; 409 `class_in_use` with any ClassStudent or ClassCourse. `POST /api/classes/{id}/close` — `Classes.Close`; status Closed; every later write on the class → 409 `class_closed`.
- `POST /api/classes/bulk` (`BulkCreateClassesRequest`, `To − From + 1 ≤ 50`) → `IReadOnlyList<ClassSummaryDto>`; `{N}` rendered without padding; any code conflict → 409 listing codes, nothing created.
- `POST /api/classes/copy` (`CopyClassesRequest`, ≤ 500) → `CopyClassesResultDto` — `Classes.Create` at `OrgUnitId`; copies live classes of the source year in the subtree to the target year (target must be effective at each class unit, else skipped); names/codes with `ReplaceFrom → ReplaceTo`; existing codes skipped and reported; homeroom teacher kept; students never copied. `IncludeCourses` is implemented in Task 14.

- [ ] **Step 1: Write the failing tests** `Admin_creates_class_in_grade_for_current_year`; `Unit_without_CanHostClasses_is_400`; `Year_not_effective_at_unit_is_400_and_closed_year_is_409`; `Code_unique_per_year_not_across_years`; `CreateClasses_policy_blocks_create_bulk_and_copy`; `Bulk_creates_Class_1_to_5`; `Copy_2026_to_2027_replaces_year_text_and_skips_existing`; `Closed_class_rejects_update_409`; `Delete_class_with_students_is_409`; `Teacher_lists_only_own_classes` (Review Focus 1); `List_defaults_to_current_year`; `Class_changes_are_audited`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(classes): classes CRUD, bulk create, copy to next year and close`.

### Task 14: ClassCourses, bulk assign, distribution matrix, class view data — lane: BE2

**PRD:** FR-CLS-05, FR-CLS-03 (students, teachers, courses), FR-ENR-01 (trigger). **Depends on:** Tasks 7 (availability), 13. DEV-PLAN item 7.

**Files:**
- Create: `src/Lms.Api/Features/Classes/ClassCourseEndpoints.cs`, `ClassCourseHandlers.cs`, `MatrixQuery.cs`
- Modify: `BulkClassHandlers.cs` (`IncludeCourses` on copy)
- Test: `tests/Lms.IntegrationTests/Classes/ClassCourseTests.cs`, `MatrixTests.cs`

**Interfaces:**
- `POST /api/classes/{id}/courses` body `AddClassCoursesRequest` → `IReadOnlyList<ClassCourseDto>` — `Classes.ManageCourses` + class unit; class/year writable; each course Active and `ICourseCatalog.IsAvailableAtAsync(course, classUnit)` (400 `course_not_available`); exists → 409 `class_course_exists`; then every `IClassCourseLifecycleHandler.OnAddedAsync` in the same transaction.
- `GET /api/classes/{id}/courses` — `Classes.View` + relationship. `PUT /api/class-courses/{id}` (`UpdateClassCourseRequest`) → `OnOptionalChangedAsync`. `DELETE /api/class-courses/{id}` — handlers' `OnRemovingAsync` (Q5 default: blocked by manual/exceptional enrollments, group members, M4+ content via `ClassCourseInUseException` → 409 `class_course_in_use`); otherwise soft delete (auto enrollments dropped by the engine handler).
- `POST /api/class-courses/bulk-assign` (`BulkAssignCourseRequest`) → `{ Added, Skipped }` — per class as above, existing skipped.
- `GET /api/classes/matrix?orgUnitId&yearId` → `DistributionMatrixDto` (classes at the unit and below, ≤ 200 classes; courses available at the unit, plus courses already on those classes).

- [ ] **Step 1: Write the failing tests** `Course_allocated_above_can_be_added_to_class`; `Course_not_allocated_for_class_unit_is_400` (Review Focus 2); `Adding_course_calls_lifecycle_handlers_in_transaction` (test handler); `Handler_veto_blocks_removal_409`; `Bulk_assign_adds_to_all_classes_of_grade_and_skips_existing`; `Matrix_lists_cells_for_unit_and_year`; `Copy_with_courses_copies_class_courses`; `Closed_year_blocks_changes_409`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(classes): class courses, bulk assign and distribution matrix`.

### Task 15: Teacher eligibility and ClassCourse teacher assignment with history — lane: BE2

**PRD:** FR-TCH-01, FR-TCH-02, FR-TCH-03 (own ClassCourses; conflict warning deferred, Q8). **Depends on:** Tasks 12, 14. DEV-PLAN item 9.

**Files:**
- Create: `src/Lms.Api/Features/Teaching/TeachingModule.cs`, `EligibilityEndpoints.cs`, `TeacherAssignmentEndpoints.cs`, `TeacherAssignmentHandlers.cs`, `MyClassCoursesEndpoint.cs`; `src/Lms.Domain/Teaching/ITeacherScheduleConflicts.cs` (no-op implementation until M5)
- Test: `tests/Lms.IntegrationTests/Teaching/EligibilityTests.cs`, `TeacherAssignmentTests.cs`

**Interfaces:**
- `POST /api/teacher-eligibility` (`GrantEligibilityRequest`, ≤ 200 pairs) — `TeacherAssignments.Manage` + unit; users hold a valid Teacher-role assignment at an ancestor-or-self or descendant of the unit (400 `teacher_not_in_unit`); courses available at the unit; existing pairs skipped. `GET /api/teacher-eligibility` (`EligibilityQuery`); `DELETE /api/teacher-eligibility/{id}` (current assignments are kept).
- `POST /api/class-courses/{id}/teachers` (`AssignTeacherRequest`) → `ClassCourseTeacherDto` — `TeacherAssignments.Manage` + class unit + relationship (Supervisor → supervised course); teacher eligible for the course at an ancestor-or-self of the class unit (400 `teacher_not_eligible`) and holding a valid Teacher assignment there (400 `teacher_not_in_unit`); second open primary → 409 `primary_teacher_exists`; `From` default today, within the class year; notifies `Teaching.Assigned`.
- `POST /api/class-courses/{id}/teachers/{assignmentId}/replace` (`ReplaceTeacherRequest`) — old row `To = EffectiveOn − 1`, new row same role from `EffectiveOn` (one transaction, history kept). `POST …/end` (`EndTeacherRequest`). `GET /api/class-courses/{id}/teachers?includeHistory=true`.
- `POST /api/teacher-assignments/bulk` (`BulkAssignTeacherRequest`, ≤ 200) — all-or-nothing.
- `GET /api/me/class-courses?yearId` → `IReadOnlyList<MyClassCourseDto>` — `LessonPlans.View`; teacher: current own; student: active enrollments.

- [ ] **Step 1: Write the failing tests** `Grant_eligibility_for_math_to_two_teachers_at_school`; `Assigning_ineligible_teacher_is_400`; `Teacher_of_other_school_is_400_teacher_not_in_unit`; `Second_primary_is_409_and_replace_keeps_history`; `Replaced_teacher_loses_access_from_effective_date` (via Task 12 filter); `Supervisor_assigns_only_on_supervised_course_else_404`; `Teacher_sees_own_class_courses_only`; `Assignment_notifies_teacher_and_is_audited`; `Closed_year_blocks_assignment_409`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(teaching): teacher eligibility and class-course teacher assignment`.

### Task 16: Blueprints — model, versioning, draft editing, publish, copy — lane: BE2

**PRD:** FR-LP-01, FR-LP-02, FR-LP-04 (apply mode choice), FR-CRS-04 (only supervisors edit), D7. **Depends on:** Tasks 3, 6, 12. DEV-PLAN item 5 (API).

**Files:**
- Create: `src/Lms.Domain/Blueprints/Blueprint.cs`, `BlueprintSection.cs`, `BlueprintLesson.cs`; `src/Lms.Domain/Classes/ClassLessonPlan.cs`, `ClassSection.cs`, `ClassLesson.cs` (tables only; logic Task 17); configurations; migration `AddBlueprints`
- Create: `src/Lms.Api/Features/Blueprints/BlueprintsModule.cs`, `BlueprintEndpoints.cs`, `BlueprintDraftHandlers.cs`, `PublishBlueprintHandler.cs`, `BlueprintValidators.cs`
- Test: `tests/Lms.IntegrationTests/Blueprints/BlueprintDraftTests.cs`, `BlueprintPublishTests.cs`

**Interfaces:**
- `[Audited] Blueprint { Guid CourseId, int Version /* 0 while draft */, BlueprintStatus Status, int Revision, DateTime? PublishedAt, Guid? PublishedBy, BlueprintApplyMode? ApplyMode }` (one Draft per course: unique `(CourseId, DraftKey)`); `BlueprintSection { Guid BlueprintId, Guid Key, int Order, TitleAr, TitleEn }`; `BlueprintLesson { Guid SectionId, Guid Key, int Order, TitleAr, TitleEn, ObjectivesAr?, ObjectivesEn? (≤ 4000), int? PlannedOffsetDays }`. Class plan entities: `ClassLessonPlan { Guid ClassCourseId, Guid? BlueprintId, int? BlueprintVersion, DateTime? SyncedAt }`, `ClassSection { Guid PlanId, Guid Key, int Order, TitleAr, TitleEn, bool IsHidden }`, `ClassLesson { Guid SectionId, Guid Key, int Order, TitleAr, TitleEn, ObjectivesAr?, ObjectivesEn?, int? PlannedOffsetDays, bool IsHidden }`.
- `GET /api/courses/{id}/blueprints` → versions (`Blueprints.View`, course visible); `GET /{version}` → `BlueprintDto`.
- `POST /api/courses/{id}/blueprints/draft` → 201 draft (copy of latest published with the same keys, or empty) — `Blueprints.Edit` + owner unit + relationship; existing → 409 `blueprint_draft_exists`. `GET …/draft`; `PUT …/draft` (`SaveBlueprintDraftRequest`) — revision mismatch → 409 `blueprint_stale`; keys must be unique and belong to this course's history or be null; ≤ 100 sections, ≤ 500 lessons; `Revision + 1`. `DELETE …/draft`.
- `POST …/draft/copy-from` (`CopyBlueprintRequest`) — source course visible to the caller (any course in `ICourseCatalog`, including another course or an older version, Q-note "copy from another year"); replaces the draft's tree with **new** keys.
- `POST …/draft/publish` (`PublishBlueprintRequest`) — `Blueprints.Publish` + relationship; empty or a section without lessons → 400 `blueprint_empty`; draft → Published with `Version = max + 1`, previous Published → Superseded, `ApplyMode` stored; after commit raises `IBlueprintPublished` handlers (Task 17 registers the push) → `PublishBlueprintResultDto`.

- [ ] **Step 1: Write the failing tests** `Supervisor_creates_draft_saves_tree_and_publishes_version_1`; `Second_draft_copies_keys_of_published_version`; `Stale_revision_is_409`; `Unsupervised_supervisor_gets_404_and_admin_view_only_403_on_edit` (Review Focus 1; OrgAdmin has `Blueprints.View` only); `Publish_empty_blueprint_is_400`; `Copy_from_other_course_gets_new_keys`; `Only_one_draft_per_course_even_concurrently`; `Blueprint_rows_are_isolated`; `Publish_is_audited`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(blueprints): versioned course blueprints with drafts, publish and copy`.

### Task 17: Class lesson plans — copy on create, locked plan, publish propagation job, plan tree read — lane: BE2

**PRD:** FR-LP-03, FR-LP-04, FR-LP-05 (view tree; module authoring in M4, Q8), D7. **Depends on:** Tasks 14, 16. DEV-PLAN item 8.

**Files:**
- Create: `src/Lms.Infrastructure/Classes/LessonPlans/ClassLessonPlanSync.cs` (`IClassCourseLifecycleHandler` + `IBlueprintPublished` handler), `BlueprintPushJob.cs`, `LessonPlanBackfill.cs` (startup, idempotent); `src/Lms.Api/Features/Classes/LessonPlanEndpoints.cs`
- Test: `tests/Lms.IntegrationTests/Classes/ClassLessonPlanTests.cs`, `BlueprintPushTests.cs`

**Interfaces:**
- `ClassLessonPlanSync.OnAddedAsync(ClassCourse)` — creates the plan from the course's latest published version (or an empty plan with `BlueprintVersion = null`).
- On publish: `PushToExisting` → `IBackgroundJobs.Enqueue<BlueprintPushJob>(j => j.RunAsync(blueprintId, CancellationToken.None))`; `NewClassesOnly` → push only to plans with `BlueprintVersion = null`. `ClassesQueued` = plans that will be touched.
- `BlueprintPushJob.RunAsync(Guid blueprintId, CancellationToken)` — for every live plan of the course whose class is Active and whose year is not Closed: merge by key (add new sections/lessons at the blueprint order; update titles, objectives, offsets, order; set `IsHidden = true` on keys no longer present; un-hide keys that came back); `BlueprintVersion`, `SyncedAt` set; one transaction per plan; idempotent; notifies the current teachers (`Blueprints.Pushed`) once per ClassCourse.
- `GET /api/class-courses/{id}/lesson-plan` → `ClassLessonPlanDto` — `LessonPlans.View` + `RelationshipAccess.CheckClassCourseAsync`; hidden items excluded for the Student role; `PlannedDate = year.StartDate + PlannedOffsetDays`; `SectionLabel` from `Blueprints.SectionLabel` at the class unit. No endpoint writes class sections/lessons (locked).

- [ ] **Step 1: Write the failing tests** `New_class_course_gets_copy_of_published_blueprint`; `NewClassesOnly_publish_leaves_existing_plans_but_fills_empty_ones`; `Push_adds_new_lesson_updates_renamed_and_hides_removed` (Review Focus 4); `Push_skips_closed_year_and_closed_class`; `Running_push_twice_changes_nothing`; `Removed_then_restored_lesson_is_unhidden`; `Student_sees_plan_without_hidden_lessons_and_teacher_of_other_class_gets_404`; `Teachers_are_notified_once`; `Backfill_creates_missing_plans_once`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(classes): class lesson plans with blueprint propagation`.

### Task 18: Student code generator — lane: BE3

**Carried from M2 Task 12** (appendix "Task 12: Student code generator", updated for D25). **PRD:** FR-STU-03, D25. **Depends on:** Tasks 1, 2. Files, interfaces and tests exactly as in the appendix (`StudentCodeCounter`, `StudentCodePattern`, `IStudentCodeGenerator`, migration `AddStudentCodeCounters`, `SettingValueValidator` change).

- [ ] **Steps 1–5** as the appendix. **Commit** `feat(students): configurable student code generator`.

### Task 19: Guardians — linking, siblings, matching, mandatory setting, guardian endpoints — lane: BE3

**Carried from M2 Task 13.** **PRD:** FR-STU-06, X-02, D30. **Depends on:** Task 2; M2 #181, #191. Files, interfaces and tests as the appendix, with these deltas:
- Add `IGuardianLinkObserver { Task OnLinksChangedAsync(GuardianProfile guardian, CancellationToken); }` called by `GuardianLinker` after link/unlink/primary change (Task 25 registers the portal sync — BE4 never edits `GuardianLinker.cs`).
- Student fixtures are seeded `StudentProfile` rows (Task 21 endpoints come later); the `/api/students/{id}/guardians` routes check the student with `CheckUnitAsync(Students.*, student.OrgUnitId)` + `IRelationshipScope` for `Students.View` (Task 12).

- [ ] **Steps 1–5** as the appendix + `Observer_is_called_after_commit_on_link_and_unlink`. **Commit** `feat(guardians): guardian linking with siblings, primary and mandatory setting`.

### Task 20: Student accounts — default password, must change at first sign-in, credentials delivery — lane: BE3

**PRD:** D24, FR-STU-06 (accounts), FR-SEC-07, X-08. **Depends on:** Task 2; M2 #191 (`IPersonAccounts`), #193 (external login — SSO sessions skip the must-change step). New task (D24 needs a column and a sign-in step that M2 did not build).

**Files:**
- Modify: `src/Lms.Domain/Identity/User.cs` (`bool MustChangePassword`), migration `AddMustChangePassword`; `src/Lms.Domain/People/PersonServices.cs` (+ `IStudentCredentials`); `src/Lms.Api/Pages/Account/Login.cshtml.cs`, `src/Lms.Api/Features/Auth/AuthorizationEndpoints.cs` (no authorization code while `MustChangePassword`); `src/Lms.Domain/Identity/IAccountEmails.cs` + `src/Lms.Api/Features/Account/AccountEmails.cs` (`SendStudentCredentialsAsync`); `src/Lms.Api/Resources/AccountResource.{ar,en}.resx`
- Create: `src/Lms.Api/Pages/Account/ChangePassword.cshtml(.cs)`, `src/Lms.Infrastructure/People/Students/StudentCredentials.cs`, `PasswordGenerator.cs`
- Test: `tests/Lms.IntegrationTests/Auth/MustChangePasswordTests.cs`, `tests/Lms.IntegrationTests/People/StudentCredentialsTests.cs`, `tests/Lms.UnitTests/People/PasswordGeneratorTests.cs`

**Interfaces:**
- `IStudentCredentials.IssueAsync(StudentProfile student, string? requestedPassword, CancellationToken) : Task<IssuedCredentials(User User, string Password, string DeliveredTo)>` — `EnsureUserAsync(student.Email, code.ToLowerInvariant(), names)`; password = requested (validated by the Identity password policy → 400 `validation_failed` with policy errors) or `PasswordGenerator.Generate()` (12 chars, policy-compliant, no ambiguous characters); sets the password, `MustChangePassword = true`, links `StudentProfile.UserId`; queues delivery for **after commit** (`IInvitationOutbox`-style queue): student email → `SendStudentCredentialsAsync` to the student; else primary guardian email; else `None`. The password is never logged, audited (`[AuditIgnore]` on hash already; no audit of the clear value), stored, or returned outside `StudentCreatedDto`.
- Sign-in: after a successful password sign-in with `MustChangePassword`, redirect to `/Account/ChangePassword?returnUrl=…`; the authorize endpoint refuses to issue a code while the flag is set (redirects to the page); a successful change clears the flag, rotates the security stamp and continues `returnUrl`. External (SSO) sign-ins ignore the flag.
- AccountResource keys (en / ar): `ChangePassword_Title` Choose a new password / اختر كلمة مرور جديدة; `ChangePassword_Intro` You signed in with a temporary password. Choose your own password to continue. / لقد سجلت الدخول بكلمة مرور مؤقتة. اختر كلمة مرور خاصة بك للمتابعة.; `ChangePassword_Current` Temporary password / كلمة المرور المؤقتة; `ChangePassword_New` New password / كلمة المرور الجديدة; `ChangePassword_Confirm` Confirm new password / تأكيد كلمة المرور الجديدة; `ChangePassword_Submit` Save and continue / حفظ ومتابعة; email `StudentCredentials_Subject` Your school account / حسابك المدرسي and body (user name, password, sign-in link, "you must change it at first sign-in").

- [ ] **Step 1: Write the failing tests** (unit) `Generated_password_meets_policy_and_avoids_ambiguous_chars`; (integration) `Must_change_user_gets_no_authorization_code_until_changed` (Review Focus 5); `Change_clears_flag_and_continues_to_app`; `Credentials_email_goes_to_student_else_primary_guardian_else_none`; `Credentials_are_sent_only_after_commit`; `Password_never_appears_in_logs_or_audit` (test log sink + audit rows); `Weak_requested_password_is_400`; `Sso_login_skips_must_change`; `Change_password_page_renders_rtl_in_arabic`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(identity): student default passwords with forced change at first sign-in`.

### Task 21: Students — create with placement, list, profile, edit, status, delete, flags, reset password — lane: BE3

**Carried from M2 Task 14.** **PRD:** FR-STU-01, FR-STU-02 (incl. class placement), FR-STU-03 (use), FR-STU-06 (enforced on create), §4.4, X-09, D24, D26, D32. **Depends on:** Tasks 12, 18, 19, 20, 24 (placement). Files, interfaces and tests as the appendix, with these deltas:
- `CreateStudentRequest.ClassId` → after the profile is created, `IEnrollmentEngine.PlaceAsync(student, cls, today, confirmOverCapacity: false)` in the same transaction; class unit ≠ `HomeOrgUnitId` → 400 `class_unit_mismatch`; class/year rules from Task 24. The response is `StudentCreatedDto` (one-time password via Task 20's `IStudentCredentials`). `StudentAccountSync` from the appendix is replaced by `IStudentCredentials` + `IRoleAssignmentSync` (Student role at the home unit).
- `GET /api/students` and `GET /api/students/{id}` apply `IRelationshipScope.ForAsync(Students.View)` (D32: a teacher sees only students of own classes; others → 404); filters `ClassId`, `Unplaced`, `AcademicYearId`; summary carries the current class.
- `PUT /api/students/{id}` moving the home unit of a **placed** student → 409 `already_placed` (use a transfer, Task 28).
- `POST /api/students/{id}/reset-password` replaces resend-invitation (D24).
- Extra tests: `Create_with_class_places_and_auto_enrolls`; `Class_of_other_unit_is_400`; `Teacher_lists_only_students_of_own_classes_and_gets_404_for_others` (Review Focus 1); `Reset_password_returns_new_password_once_and_sets_must_change`.

- [ ] **Steps 1–5** as the appendix + deltas. **Commit** `feat(students): student profiles with codes, placement, duplicate detection and guardians`.

### Task 22: Student self-edit and change-request approval queue — lane: BE3

**Carried from M2 Task 18** unchanged (migration `AddStudentChangeRequests`, `StudentFieldRules`, `StringSet` validator, `/api/me/student-profile*`, `/api/student-change-requests*`). **PRD:** FR-STU-04, D27. **Depends on:** Tasks 1, 21.

- [ ] **Steps 1–5** as the appendix. **Commit** `feat(students): self-edit and change request approval queue`.

### Task 23: Student import including guardians and class placement — lane: BE3

**Carried from M2 Task 20** (D24/D25 text). **PRD:** FR-STU-05, FR-STU-06, FR-ENR-03 (placement on import), X-06, D8, D34. **Depends on:** Tasks 19, 20, 21, 24; M2 #159 / PR #202 (framework, incl. `ImportColumn.Sensitive` — add it here if #202 did not). Deltas:
- Extra column `ClassCode` — Class code / رمز الفصل (optional): class of the **current year** at the row's unit; errors `unknown_class`, `class_closed`, `class_full` (Block mode), `class_unit_mismatch`; Warn-mode over-capacity is listed in the preview, not an error. Commit places through `IEnrollmentEngine.PlaceAsync` per row (auto-enrollment included).
- `DefaultPassword` is declared `ImportColumn.Sensitive = true` and exposed as `ImportColumnDto.Sensitive = true` (Task 2 contract): its cell values are never stored in `ImportRow.ValuesJson` (only a "filled" marker so commit knows a value existed — the commit re-reads the value from the uploaded file, which is deleted right after commit or discard), the rows API and preview return the mask `"••••••"` (or `null` when empty), and the error report writes the mask in that column; framework-side change in `ImportService`/`ErrorReportWriter`/rows endpoint (BE1's #202 files — coordinate, or BE3 adds it here if #202 merged without it).
- `DefaultPassword` blank → generated (Q9); the commit result offers a one-time **credentials sheet** download (`GET /api/imports/students/{id}/credentials`, available once, within 15 minutes of commit, to the requester only; generated in memory from the commit's issued passwords held in a short-lived protected cache — never written to `ImportRow` or storage).
- Extra tests: `Default_password_column_is_masked_in_rows_api_preview_and_error_report` (Review Focus 5); `Default_password_is_not_in_import_row_values_json_or_logs`; `Columns_endpoint_marks_DefaultPassword_sensitive`; `Rows_with_class_code_are_placed_and_auto_enrolled`; `Unknown_class_code_blocks_the_file`; `Credentials_sheet_downloads_once_and_only_for_requester`; `Failure_on_last_row_rolls_back_codes_guardians_accounts_placements_and_enrollments` (Review Focus 3/6).

- [ ] **Steps 1–5** as the appendix + deltas. **Commit** `feat(students): student Excel import with guardians and class placement`.

### Task 24: Enrollment engine — placement, auto-enrollment, optional courses, unenroll, views — lane: BE4

**PRD:** FR-ENR-01, FR-ENR-02, FR-ENR-04, FR-CLS-02 (capacity), D9, X-09. **Depends on:** Tasks 4, 5, 12. DEV-PLAN item 10. **Critical path for Tasks 21, 23, 26–28.**

**Files:**
- Create: `src/Lms.Infrastructure/Enrollment/EnrollmentEngine.cs`, `EnrollmentClassCourseHandler.cs` (`IClassCourseLifecycleHandler`), `PlacementGuard.cs`; `src/Lms.Api/Features/Enrollment/EnrollmentModule.cs`, `ClassStudentEndpoints.cs`, `EnrollmentEndpoints.cs`
- Test: `tests/Lms.IntegrationTests/Enrollment/EnrollmentEngineTests.cs`, `EnrollmentEndpointsTests.cs`, `EnrollmentPerformanceTests.cs`

**Interfaces:**
- `IEnrollmentEngine` (Domain, from Task 5): `PlaceAsync(StudentProfile student, Class cls, DateOnly effectiveOn, bool confirmOverCapacity, CancellationToken) : Task<PlacementResult(ClassStudent Placement, int EnrollmentsCreated, IReadOnlyList<RestrictionSkip> Skips, bool OverCapacity)>` — class Active, year not Closed, student Active, no other active placement in that year (409 `already_placed`), capacity per `Classes.CapacityMode` (409 `class_full` / `over_capacity`); moves the student's home to the class unit (`MoveHome`, `StudentGuardian.Repoint`, Student-role assignment sync); enrolls (Source `Auto`) in every non-optional live ClassCourse that passes `CourseRestrictionEvaluator`; set-based inserts. `EnrollManualAsync(ClassCourse cc, StudentProfile s, EnrollmentSource source, CancellationToken)` — student actively placed in the class (400 `not_in_class`); non-optional and source `Manual` → 409 `enrollment_not_optional`; active → 409 `already_enrolled`; restriction fails and source ≠ `Exceptional` → 409 `restriction_not_met` (extension `failedTypes`). `DropAsync(CourseEnrollment e, string reason, DateOnly on, CancellationToken)` (Dropped, `To`, removes group membership). `RemoveFromClassAsync(ClassStudent cs, string reason, DateOnly on, CancellationToken)` (drops all, `Removed`). `TransferAsync(...)` signature reserved for Task 28.
- `EnrollmentClassCourseHandler`: `OnAddedAsync` → enroll active students (non-optional, restrictions pass); `OnRemovingAsync` → veto (`ClassCourseInUseException`) when Manual/Exceptional enrollments or group members exist (Q5), else drop Auto enrollments; `OnOptionalChangedAsync` → becoming non-optional enrolls the rest, becoming optional keeps existing enrollments.
- Endpoints: `POST /api/classes/{id}/students` (`AddStudentsToClassRequest`, ≤ 500) → `AddStudentsResultDto` — `Enrollment.Manage` + class unit + each student visible (`Students.View` scope) — all-or-nothing; `GET /api/classes/{id}/students` — `Classes.View` + relationship; `POST /api/classes/{id}/students/{classStudentId}/remove` (`RemoveFromClassRequest`). `POST /api/class-courses/{id}/enrollments` (`EnrollStudentsRequest`) — `Enrollment.Manage` (optional courses); `GET /api/class-courses/{id}/enrollments` — `Enrollment.View` + relationship; `POST /api/enrollments/{id}/drop` (`DropEnrollmentRequest`); `GET /api/students/{id}/enrollments` — `Enrollment.View` + student check + relationship; `GET /api/enrollments` (`EnrollmentQuery`).

- [ ] **Step 1: Write the failing tests** `Placing_student_enrolls_in_all_non_optional_class_courses`; `Restricted_course_is_skipped_and_reported` (Review Focus 3); `Adding_course_to_class_enrolls_current_students`; `Optional_course_needs_manual_enrollment_and_manual_on_mandatory_is_409`; `Second_placement_in_same_year_is_409_already_placed`; `Block_mode_full_class_is_409_and_warn_mode_needs_confirmation`; `Placement_moves_home_unit_guardian_links_and_student_role`; `Drop_keeps_history_and_removes_group_membership`; `Removing_course_with_manual_enrollment_is_409_otherwise_auto_enrollments_dropped`; `Teacher_reads_enrollments_of_own_class_course_only` (Review Focus 1); `Closed_year_blocks_every_write_409`; `Placing_500_students_into_class_with_12_courses_under_5_seconds`; `Enrollment_rows_are_audited`.
- [ ] **Step 2: Run** `dotnet test tests/Lms.IntegrationTests --filter "EnrollmentEngineTests|EnrollmentEndpointsTests|EnrollmentPerformanceTests"` → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(enrollment): class placement and automatic course enrollment engine`.

### Task 25: Guardian portal accounts and "my children" — lane: BE4

**Carried from M2 Task 15.** **PRD:** FR-STU-06 (portal), §3.1, §3.4 Guardian row, §4.4 `GuardianPortal`, D30. **Depends on:** Tasks 19, 21. Delta: `GuardianAccountSync` is registered as an `IGuardianLinkObserver` (Task 19) instead of editing `GuardianLinker.cs`; `MyChildDto` children also show `CurrentClassNameAr/En` when the contract has them (else unchanged).

- [ ] **Steps 1–5** as the appendix. **Commit** `feat(guardians): guardian portal accounts and my children`.

### Task 26: Add students to classes in bulk from Excel (class placement import) — lane: BE4

**PRD:** FR-ENR-03, X-06, D8, D34. **Depends on:** Task 24; M2 #159 / PR #202. DEV-PLAN item 11.

**Files:**
- Create: `src/Lms.Api/Features/Enrollment/Import/ClassStudentsImportDefinition.cs`
- Modify: `src/Lms.Api/Resources/ImportResource.{ar,en}.resx`
- Test: `tests/Lms.IntegrationTests/Enrollment/ClassStudentsImportTests.cs`

**Interfaces:**
- `Kind = "class-students"`, `Permission = Enrollment.Import`; target unit = the batch unit; columns `StudentCode` — Student code / كود الطالب ✓, `ClassCode` — Class code / رمز الفصل ✓, `EffectiveOn` — Effective date / تاريخ البدء. Year = current year at the target unit (the wizard shows it). Row errors: `unknown_student` (not visible in the target subtree), `unknown_class`, `already_placed`, `class_closed`, `year_closed`, `class_full` (Block), `duplicate_in_file` (student code), `not_active`. Warn-mode over-capacity shown per class in the preview. Commit: `PlaceAsync` per row in file order.

- [ ] **Step 1: Write the failing tests** `Import_places_300_students_into_10_classes_and_auto_enrolls`; `Student_of_sibling_school_is_unknown_student`; `Already_placed_student_blocks_file`; `Full_class_in_block_mode_blocks_file`; `Failure_on_last_row_leaves_no_placement_or_enrollment` (Review Focus 3); `Manager_without_Enrollment_Import_gets_403`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(enrollment): class placement Excel import`.

### Task 27: Student groups per ClassCourse with the one-group rule — lane: BE4

**PRD:** FR-GRP-01, FR-GRP-02, FR-GRP-03 (target model only; content targeting in M4), D6. **Depends on:** Tasks 12, 24. DEV-PLAN item 12.

**Files:**
- Create: `src/Lms.Api/Features/Enrollment/Groups/GroupEndpoints.cs`, `GroupHandlers.cs`; `src/Lms.Domain/Enrollment/IGroupTargets.cs` (M4 uses it to validate a target group)
- Test: `tests/Lms.IntegrationTests/Enrollment/GroupTests.cs`

**Interfaces:**
- `POST /api/class-courses/{id}/groups` (`SaveGroupRequest`) → 201 — `Groups.Manage` + `RelationshipAccess.CheckClassCourseAsync` (Teacher → own); `MatchType`/`MatchValue` must be a restriction type/value pair (Task 1 catalogs) or both null. `GET /api/class-courses/{id}/groups` — `Groups.View` + relationship. `PUT /api/groups/{id}`, `DELETE /api/groups/{id}` (soft-deletes members).
- `POST /api/groups/{id}/members` (`GroupMembersRequest`, ≤ 500) — each student actively enrolled in the ClassCourse (400 `not_enrolled`) and in no other live group of it (409 `already_in_group`, also when the unique index fires); all-or-nothing. `POST /api/groups/{id}/members/remove`. `GET /api/groups/{id}/members`.
- `GET /api/groups/{id}/suggestions` → `IReadOnlyList<GroupMemberDto>` — enrolled students in no group whose gender/religion/level matches `MatchType/MatchValue` (FR-GRP-01 "suggestion by course restriction").
- `IGroupTargets.IsValidTargetAsync(Guid classCourseId, Guid groupId, CancellationToken)`.

- [ ] **Step 1: Write the failing tests** `Teacher_creates_Islamic_and_Christian_groups_on_own_religion_class_course`; `Teacher_of_other_class_course_gets_404` (Review Focus 1); `Student_not_enrolled_is_400`; `Student_in_another_group_of_same_class_course_is_409_even_concurrently` (D6); `Same_student_may_be_in_groups_of_two_class_courses`; `Suggestions_list_ungrouped_muslim_students`; `Dropped_enrollment_removes_membership`; `Counselor_can_view_not_manage_403`; `Group_rows_are_isolated_and_audited`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(enrollment): student groups with one group per class course`.

### Task 28: Transfer between classes mid-year — lane: BE4

**PRD:** FR-ENR-05, X-02, X-07. **Depends on:** Tasks 24, 27. DEV-PLAN item 13.

**Files:**
- Create: `src/Lms.Infrastructure/Enrollment/TransferService.cs`, `src/Lms.Api/Features/Enrollment/TransferEndpoints.cs`
- Modify: `src/Lms.Infrastructure/Enrollment/EnrollmentEngine.cs` (`TransferAsync`)
- Test: `tests/Lms.IntegrationTests/Enrollment/TransferTests.cs`

**Interfaces:**
- `POST /api/transfers/preview` (`TransferStudentRequest`) → `TransferPreviewDto`; `POST /api/transfers` → `StudentTransferDto` — `Enrollment.Transfer` + `CheckUnitAsync` at **both** class units + student visible; rules (400 `transfer_invalid` with extension `reason`: `same_class`, `different_year`, `different_level_type`, `not_placed`, `date_outside_year`); target writable and capacity per setting; cross-school transfer allowed when both units are visible (Q7).
- `IEnrollmentEngine.TransferAsync(StudentProfile s, Class to, DateOnly on, string reason, bool confirmOverCapacity, CancellationToken) : Task<StudentTransfer>` — old placement `TransferredOut` (`To = on − 1`); new placement from `on` (home moves, links repoint); for each old active enrollment: course offered in the target → new enrollment (`Source = Transfer`, `CarriedFromEnrollmentId`, same optional/manual state, restrictions re-checked) and old one Dropped (`DropReason = "transfer"`); course not in target → Dropped; target non-optional courses not covered → auto-enroll; all old group memberships removed (target groups are not auto-filled); `StudentTransfer` row; one transaction; after commit `Enrollment.StudentTransferred` to the student's guardians with accounts and current teachers of both classes. Grades carry-over (`Enrollment.TransferCarriesGrades`) is applied by M5 through `CarriedFromEnrollmentId`.
- `GET /api/students/{id}/transfers` → history.

- [ ] **Step 1: Write the failing tests** `Transfer_remaps_common_courses_drops_others_and_adds_target_courses`; `Old_class_history_and_enrollments_stay_readable`; `Different_level_type_or_year_is_400`; `Target_full_in_block_mode_is_409`; `Group_memberships_are_removed`; `Guardian_and_both_teachers_are_notified`; `Cross_school_transfer_needs_both_units_visible_else_404`; `Transfer_is_audited`; `Restricted_course_in_target_is_reported_not_enrolled`.
- [ ] **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(enrollment): transfer students between classes`.

---

### Frontend tasks
Every FE task: API interface + HTTP + fake (`Lms:UseFakeApi=true`) for its module if not yet there; bUnit tests first; keys in both resx files (prefixes given per task — the parity test enforces both languages); RTL screenshots ar 360 px / ar desktop / en desktop in `docs/screenshots/pr<N>/`; nav items added to `NavCatalog.cs` with their permission; pages behind a permission show the 403 page from #135.

### Task 29: Courses list, create/edit and import page — lane: FE1

**PRD:** FR-CRS-01, FR-CRS-02 (allocate at creation), X-06, X-09. **Depends on:** Task 3 (fake until Tasks 6, 7); import wizard #169 / PR #195.

**Files:** Create `src/Lms.Client/Services/Api/ICoursesApi.cs`, `CoursesApi.cs`, `FakeCoursesApi.cs`; `src/Lms.Client/Pages/Courses/CourseListPage.razor` (`/courses`), `CourseDialog.razor`, `CourseImportPage.razor` (`/courses/import`); Modify `NavCatalog.cs` (`Nav_Courses`, `Courses.View`), `ClientServices.cs`, resources. Test `tests/Lms.Client.Tests/Pages/Courses/CourseListPageTests.cs`, `CourseDialogTests.cs`.

**Interfaces:** list (`ServerTable`: code, name, owner unit, periods, status, "Inherited" chip, allocation count; filters unit/status/search/"supervised by me"); dialog (code `dir=ltr`, `BilingualNameFields`, descriptions, language select ar/en/fr/de, weekly periods, owner unit picker limited to root + course-management units, optional "allocate to" multi-unit picker); inherited rows open read-only; `invalid_owner_unit` / `course_code_taken` messages. Import page = `ImportWizard` kind `courses`. Keys `Nav_Courses`, `Course_*`, `CourseStatus_*`, `CourseImport_*`.

- [ ] **Step 1: bUnit tests** `List_marks_inherited_courses_read_only`; `New_button_hidden_without_Courses_Create`; `Owner_picker_offers_root_and_course_management_units_only`; `Code_taken_shows_field_message`; `Import_page_uses_courses_kind`; `Pages_render_rtl_in_arabic`. **Steps 2–5** FAIL → implement → PASS + screenshots → commit `feat(client): courses list, editor and import`.

### Task 30: Course profile — allocations, restrictions, supervisors; unit courses tab; My courses — lane: FE1

**PRD:** FR-CRS-02, FR-CRS-03, FR-CRS-04. **Depends on:** Task 29 (BE Tasks 7–9 or fakes).

**Files:** Create `src/Lms.Client/Pages/Courses/CourseProfilePage.razor` (`/courses/{Id:guid}`; tabs Details · Allocations · Restrictions · Supervisors · Blueprint (link to Task 31)), `AllocationsTab.razor`, `RestrictionsTab.razor`, `SupervisorsTab.razor`, `MyCoursesPage.razor` (`/me/courses`); `src/Lms.Client/Pages/Organization/UnitCoursesTab.razor` (hosted by the org tree node page — one-line change in `Pages/Organization`, coordinated in the PR). Modify `ICoursesApi`, `NavCatalog.cs` (`Nav_MyCourses`, `Courses.View`, shown to Supervisor role). Test `CourseProfilePageTests.cs`, `UnitCoursesTabTests.cs`.

**Interfaces:** allocations (add with unit picker, remove with `allocation_in_use` message); restrictions (type rows with multi-select of religions/genders/level types; save warns "{n} current enrollments no longer match" from the header); supervisors (staff autocomplete from `IStaffApi.ListAsync`, filtered to Supervisor role); unit tab lists available (inherited marked) and below; My courses = cards with blueprint status. Keys `CourseProfile_*`, `Allocation_*`, `Restriction_*`, `RestrictionType_*`, `Supervisor_*`, `MyCourses_*`.

- [ ] **Step 1: bUnit tests** `Allocation_in_use_shows_message`; `Restriction_editor_sends_replace_all_and_shows_affected_warning`; `Supervisor_picker_lists_supervisors_only`; `Tabs_hidden_by_permission_and_CanEdit`; `My_courses_shows_supervised_only`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): course allocations, restrictions, supervisors and my courses`.

### Task 31: Blueprint builder — tree with drag-and-drop, versions, publish with apply mode, copy — lane: FE1

**PRD:** FR-LP-01, FR-LP-02, FR-LP-04, D7. **Depends on:** Task 3 (BE Task 16 or fake).

**Files:** Create `IBlueprintsApi.cs`, `BlueprintsApi.cs`, `FakeBlueprintsApi.cs`; `src/Lms.Client/Pages/Blueprints/BlueprintPage.razor` (`/courses/{Id:guid}/blueprint`), `BlueprintTree.razor` (`MudDropContainer`, sections → lessons, keyboard move up/down buttons for accessibility X-10), `LessonDialog.razor`, `PublishBlueprintDialog.razor`, `CopyBlueprintDialog.razor`, `BlueprintVersionsPanel.razor`. Test `tests/Lms.Client.Tests/Pages/Blueprints/*Tests.cs`.

**Interfaces:** read-only view of the latest published version for viewers; "Edit draft" (creates or opens the draft) for `Blueprints.Edit` + `CanEdit`; save sends the whole tree with `Revision` (`blueprint_stale` → reload prompt); unsaved-changes guard; section label from `Blueprints.SectionLabel` (`SectionLabel_Week|Unit|Chapter`); publish dialog with the two apply modes explained (D7) and the queued-classes result; copy dialog (course autocomplete + version); versions panel (view old versions read-only). Keys `Blueprint_*`, `BlueprintStatus_*`, `ApplyMode_*`, `SectionLabel_*`.

- [ ] **Step 1: bUnit tests** `Drag_lesson_to_other_section_reorders_and_marks_dirty`; `Move_buttons_reorder_without_mouse`; `Save_sends_revision_and_stale_shows_reload`; `Publish_requires_apply_mode_choice`; `Viewer_sees_read_only_tree`; `Section_label_follows_setting`; `Builder_renders_rtl_in_arabic` (drag handles mirrored). **Steps 2–5** → commit `feat(client): blueprint builder with versions and publishing`.

### Task 32: Classes list, create, bulk create, copy to next year, close — lane: FE1

**PRD:** FR-CLS-01, FR-CLS-02, FR-CLS-04, X-04. **Depends on:** Task 4 (BE Task 13 or fake).

**Files:** Create `IClassesApi.cs`, `ClassesApi.cs`, `FakeClassesApi.cs`; `src/Lms.Client/Pages/Classes/ClassListPage.razor` (`/classes`), `ClassDialog.razor`, `BulkClassesDialog.razor`, `CopyClassesDialog.razor`; Modify `NavCatalog.cs` (`Nav_Classes`, `Classes.View`). Test `ClassListPageTests.cs`, `BulkClassesDialogTests.cs`.

**Interfaces:** list bound to `IYearStore` (year chip, read-only banner for Closed); columns code, name, unit, students / capacity (warning colour when full), homeroom teacher, courses, status; dialog (unit picker limited to `CanHostClasses` units, capacity, homeroom teacher autocomplete); bulk dialog with live preview of the rendered names/codes; copy dialog (source year → target year, include courses, replace text) with the skipped-codes result; close with confirm. Keys `Nav_Classes`, `Class_*`, `ClassStatus_*`, `BulkClasses_*`, `CopyClasses_*`.

- [ ] **Step 1: bUnit tests** `List_uses_selected_year_and_closed_year_is_read_only`; `Bulk_preview_renders_names_from_pattern`; `Copy_shows_skipped_codes`; `Unit_picker_offers_only_class_hosting_units`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): classes list, bulk create, copy and close`.

### Task 33: Class view, class courses tab and distribution matrix — lane: FE1

**PRD:** FR-CLS-03, FR-CLS-05. **Depends on:** Task 32 (BE Task 14 or fake).

**Files:** Create `src/Lms.Client/Pages/Classes/ClassPage.razor` (`/classes/{Id:guid}`; tabs Students (component from Task 43, placeholder until then) · Courses · Teachers (Task 34) · Timetable (hidden until M5, Q8)), `ClassCoursesTab.razor`, `AddClassCoursesDialog.razor`, `DistributionMatrixPage.razor` (`/classes/matrix`). Test `ClassPageTests.cs`, `DistributionMatrixPageTests.cs`.

**Interfaces:** courses tab (add from available courses only, optional toggle, remove with `class_course_in_use`); matrix: rows classes × columns courses for a unit + year, checkbox cells (add/remove), column header "assign to all" (bulk assign), optional marker, sticky headers, horizontal scroll inside the grid only (no page scroll at 360 px). Keys `ClassPage_*`, `ClassCourse_*`, `Matrix_*`.

- [ ] **Step 1: bUnit tests** `Add_dialog_lists_available_courses_only`; `Matrix_toggle_adds_and_removes_class_course`; `Assign_to_all_calls_bulk_assign`; `Removal_blocked_message`; `Timetable_tab_hidden`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): class view, class courses and distribution matrix`.

### Task 34: Teacher eligibility and ClassCourse teachers — lane: FE1

**PRD:** FR-TCH-01, FR-TCH-02. **Depends on:** Task 33 (BE Task 15 or fake).

**Files:** Create `ITeachingApi.cs`, `TeachingApi.cs`, `FakeTeachingApi.cs`; `src/Lms.Client/Pages/Teaching/EligibilityPage.razor` (`/teaching/eligibility`), `GrantEligibilityDialog.razor`, `src/Lms.Client/Pages/Classes/ClassTeachersTab.razor`, `AssignTeacherDialog.razor`, `ReplaceTeacherDialog.razor`, `BulkAssignTeacherDialog.razor`. Test `EligibilityPageTests.cs`, `ClassTeachersTabTests.cs`.

**Interfaces:** eligibility grid (teacher × course, filter by unit/teacher/course, bulk grant teachers × courses); class teachers tab per ClassCourse (primary + assistants, history toggle, replace with effective date, end); assign dialog lists only eligible teachers; bulk assign from a teacher's point of view (pick ClassCourses). Keys `Eligibility_*`, `Teacher_*`, `TeacherRole_*`.

- [ ] **Step 1: bUnit tests** `Assign_dialog_lists_eligible_teachers_only`; `Second_primary_shows_replace_hint`; `Replace_sends_effective_date_and_history_shows_both`; `Bulk_grant_sends_cross_product`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): teacher eligibility and class-course teachers`.

### Task 35: My class courses and class lesson plan view — lane: FE1

**PRD:** FR-LP-05 (view tree), FR-TCH-03 ("teacher sees only own"), §3.4 Teacher/Student rows. **Depends on:** Task 4 (BE Tasks 15, 17 or fakes).

**Files:** Create `src/Lms.Client/Pages/Teaching/MyClassCoursesPage.razor` (`/me/class-courses`), `src/Lms.Client/Pages/Classes/LessonPlanPage.razor` (`/class-courses/{Id:guid}/plan`), `LessonPlanTree.razor`; Modify `NavCatalog.cs` (`Nav_MyClassCourses`, `LessonPlans.View`), `Pages/Home.razor` (teacher/student landing card). Test `MyClassCoursesPageTests.cs`, `LessonPlanPageTests.cs`.

**Interfaces:** cards per own ClassCourse (teacher) or enrolled course (student); plan tree sections → lessons with planned dates, hidden lessons greyed for staff and absent for students, version + synced-at line, "modules arrive in a later release" empty slot reserved for M4. Keys `MyClassCourses_*`, `LessonPlan_*`.

- [ ] **Step 1: bUnit tests** `Teacher_sees_own_class_courses`; `Student_tree_has_no_hidden_lessons`; `Planned_dates_render_in_culture_and_hijri_when_set`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): my class courses and lesson plan view`.

### Task 36: Enrollment exception requests and approval queue — lane: FE1

**PRD:** FR-CRS-03 (exceptions). **Depends on:** Task 4 (BE Task 11 or fake); Task 43's restriction-skip display (FE2) links here.

**Files:** Create `src/Lms.Client/Pages/Enrollment/ExceptionQueuePage.razor` (`/enrollment-exceptions`), `RequestExceptionDialog.razor` (opened from a restriction skip row), `DecideExceptionDialog.razor`; Modify `IEnrollmentApi` (+ exception methods; FE2 owns the file — append-only methods, coordinated), `NavCatalog.cs` (`Nav_Exceptions`, `Enrollment.ApproveException`). Test `ExceptionQueuePageTests.cs`.

**Interfaces:** queue with unit/status filters, failed restriction chips, approve (optional comment) / reject (required comment); requester view of own requests with cancel. Keys `Exception_*`, `ExceptionStatus_*`.

- [ ] **Step 1: bUnit tests** `Reject_requires_comment`; `Approve_sends_request_and_removes_row`; `Nav_hidden_without_permission`; `Page_renders_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): enrollment exception queue`.

### Task 37: Students list and create with guardians and class placement — lane: FE2

**Carried from M2 Task 27** (appendix text incl. D24 one-time credentials dialog). **Depends on:** Task 2 (fakes until BE Task 21); #189 components. Deltas: create page has an optional **Class** select (classes of the selected year at the chosen unit, capacity shown; over-capacity → confirm and resend with confirm flag); the credentials dialog shows code, password (copy), and where it was sent (`CredentialsDelivery_*`); list gains class column and "Unplaced" filter; teacher sees only own students (server-filtered).

- [ ] **Steps 1–5** as the appendix + `Class_select_lists_classes_of_unit_and_year`, `Credentials_dialog_shows_password_once_and_delivery`. **Commit** `feat(client): students list and create with guardians and placement`.

### Task 38: Student profile, edit, status, delete, feature flags, reset password — lane: FE2

**Carried from M2 Task 28.** Deltas: profile header shows the current class (link); "Reset password" replaces "Resend invitation" (one-time dialog, same as Task 37); a **Classes & enrollments** tab hosts `StudentEnrollmentsTab` from Task 43 (placeholder until then). **Depends on:** Task 37.

- [ ] **Steps 1–5** as the appendix. **Commit** `feat(client): student profile, edit and feature flags`.

### Task 39: Guardians tab, guardian pages and portal invitation — lane: FE2

**Carried from M2 Task 29** unchanged. **Depends on:** Task 38 (BE Tasks 19, 25 or fakes).

- [ ] **Steps 1–5** as the appendix. **Commit** `feat(client): guardians tab, guardian pages and portal invitations`.

### Task 40: Student self-profile, change requests, approval queue, critical-fields setting editor — lane: FE2

**Carried from M2 Task 30** unchanged. **Depends on:** Task 38 (BE Task 22 or fakes); M1 `SettingEditor`.

- [ ] **Steps 1–5** as the appendix. **Commit** `feat(client): student self-profile and change request approval`.

### Task 41: Guardian "My children" page — lane: FE2

**Carried from M2 Task 31.** Delta: each card shows the child's current class. **Depends on:** Task 39 (BE Task 25 or fake).

- [ ] **Steps 1–5** as the appendix. **Commit** `feat(client): guardian my children page`.

### Task 42: Student import and class placement import pages — lane: FE2

**Carried from M2 Task 26 (student part)** + FR-ENR-03. **Depends on:** Task 37; import wizard #169 / PR #195 (BE Tasks 23, 26 or fakes). Deltas: student page help panel also explains `ClassCode` and default passwords; after commit, a one-time "Download credentials sheet" button (Task 23 endpoint) with the warning `StudentImport_CredentialsOnce`; new page `/classes/import` = wizard kind `class-students` with the current-year banner. Keys from the appendix + `ClassImport_*`.

`ImportPreviewTable` (FE2's #195 component) renders sensitive columns as the mask and never shows the raw cell, also in error tooltips.

- [ ] **Steps 1–5** as the appendix + `Sensitive_column_is_masked_in_preview_table`, `Credentials_button_shown_once_after_commit`, `Class_import_page_uses_class_students_kind`. **Commit** `feat(client): student and class placement import pages`.

### Task 43: Class students tab, enrollments, optional enrollment, unenroll and transfer — lane: FE2

**PRD:** FR-ENR-01..05, FR-CLS-03 (students tab). **Depends on:** Task 4 (BE Tasks 24, 28 or fakes); Task 33 hosts the tab (FE1 file, one-line coordinated).

**Files:** Create `IEnrollmentApi.cs`, `EnrollmentApi.cs`, `FakeEnrollmentApi.cs`; `src/Lms.Client/Pages/Enrollment/ClassStudentsTab.razor`, `AddStudentsDialog.razor` (search students of the unit, multi-select, capacity confirm), `RestrictionSkipsPanel.razor` (with "Request exception" → Task 36 dialog), `ClassCourseEnrollmentsTab.razor` (optional course: enroll / drop), `StudentEnrollmentsTab.razor` (per student: placements history, enrollments with source/status, transfers), `TransferDialog.razor` (target class picker → preview kept/dropped/added → confirm). Test `ClassStudentsTabTests.cs`, `TransferDialogTests.cs`, `StudentEnrollmentsTabTests.cs`.

**Interfaces:** remove from class with reason/date; drop enrollment with reason; transfer dialog shows `TransferPreviewDto` before confirm and the `transfer_invalid` reason text. Keys `ClassStudents_*`, `Enrollment_*`, `EnrollmentSource_*`, `EnrollmentStatus_*`, `Transfer_*`.

- [ ] **Step 1: bUnit tests** `Add_students_reports_restriction_skips_with_request_exception_link`; `Over_capacity_asks_for_confirmation`; `Optional_course_enroll_and_drop`; `Transfer_dialog_previews_before_confirm`; `Transfer_invalid_reason_is_localized`; `Student_tab_lists_history`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): class students, enrollments and transfers`.

### Task 44: Groups per ClassCourse — lane: FE2

**PRD:** FR-GRP-01, FR-GRP-02. **Depends on:** Task 43 (BE Task 27 or fake).

**Files:** Create `IGroupsApi.cs`, `GroupsApi.cs`, `FakeGroupsApi.cs`; `src/Lms.Client/Pages/Enrollment/GroupsTab.razor` (on the ClassCourse view and on the teacher's lesson-plan page header), `GroupDialog.razor`, `GroupMembersDialog.razor` (two-list picker: ungrouped enrolled students ↔ group members, "Add suggested"). Test `GroupsTabTests.cs`.

**Interfaces:** create/rename/delete groups with optional match type/value; members dialog shows only ungrouped enrolled students on the left (one-group rule visible), `already_in_group` message on conflict; teacher sees own ClassCourses only. Keys `Group_*`.

- [ ] **Step 1: bUnit tests** `Members_picker_excludes_students_in_other_groups`; `Add_suggested_moves_matching_students`; `Conflict_shows_already_in_group`; `Manage_actions_hidden_without_Groups_Manage`; `Pages_render_rtl_in_arabic`. **Steps 2–5** → commit `feat(client): student groups per class course`.

---

### Task 45: M3 isolation and permission probe suite — lane: QA (QA2)

**PRD:** §4.2.3, FR-SEC-05, §3.3 step 2, PRD §9.2. **Depends on:** BE Tasks 5–28 (grows as they merge; first PR after Task 6); M2 Task 34 (`M2Callers`, matrix pattern, #182). *Includes the student/guardian/change-request/`/api/me/student-profile*`/`/api/me/children` rows and callers deferred from M2 Task 34.*

**Files:** Create `tests/Lms.IntegrationTests/Security/M3Callers.cs` (extends M2: School1 teacher A with ClassCourse X, School1 teacher B with ClassCourse Y, supervisor of course C, supervisor of another course, student in class 1, guardian, School2 admin, client B admin), `ProbeRows/M3*.cs` (one file per module), `M3EndpointMatrixTests.cs`, `M3IsolationTests.cs`, `M3RelationshipProbeTests.cs`.

**Interfaces:** matrix rows for every M3 route × caller (e.g. teacher A on teacher B's ClassCourse enrollments → 404; teacher A on `/api/students?classId=<class 2>` → empty; supervisor on unsupervised course draft → 404; OrgAdmin on blueprint publish → 403; School2 admin adding a School1-owned course to a School2 class → 400/404; student on another class's lesson plan → 404; guardian on `/api/students/{own child}` → 403); `Every_M3_api_route_is_in_the_probe_matrix`; isolation for every Task 5/11/16/18/22 table; `CourseCatalog` never returns sibling/other-client courses (Review Focus 2).

- [ ] **Step 1: Write the tests** for merged endpoints. **Step 2: Run** → FAILs filed as `bug` (`sev:high` for any leak) against the owning lane. **Step 3:** extend with each BE PR (QA review blocks a BE PR whose routes are missing). **Step 4:** green on `main` at milestone end. **Step 5: Commit** `test(security): M3 endpoint matrix, relationship and isolation probes`.

### Task 46: E2E exit demo 1 — courses, allocations, blueprint, classes, teachers, lesson plan — lane: QA (QA1)

**PRD:** FR-CRS-01..04, FR-LP-01..05, FR-CLS-01..05, FR-TCH-01..02, DEV-PLAN §1 M3 exit ("courses allocated in the tree, blueprint published, classes created, teachers assigned"). **Depends on:** Tasks 6, 7, 9, 13–17, 29–35; dev job trigger #199 / PR #201 (to run the push job deterministically, else poll).

**Files:** Create `tests/Lms.E2ETests/M3/AcademicStructureTests.cs`, `M3Names.cs`, page objects under `tests/Lms.E2ETests/Pages/` (`CoursesPage`, `BlueprintPage`, `ClassesPage`, `ClassPage`).

**Scenarios (each `[Theory]` `"ar"` / `"en"`; Arabic asserts `dir="rtl"`):** `Root_admin_creates_course_allocates_to_school_assigns_supervisor`; `Supervisor_builds_blueprint_by_drag_and_drop_and_publishes_version_1`; `Admin_bulk_creates_classes_adds_courses_from_matrix_and_assigns_eligible_teacher`; `Teacher_signs_in_and_sees_only_own_class_course_and_its_plan`; `Supervisor_publishes_version_2_with_push_and_teacher_sees_new_lesson`; `Admin_copies_classes_to_next_year`.

- [ ] **Steps 1–4** as M2 Task 35 (write → FAIL until merged → fix test-side issues, file bugs → PASS in CI). **Step 5: Commit** `test(e2e): M3 courses, blueprints, classes and teachers`.

### Task 47: E2E exit demo 2 — students, guardians, portal, default password, change requests — lane: QA (QA3)

**Carried from M2 Task 37** (appendix scenarios incl. `Student_signs_in_with_default_password_and_must_change_it_first`). **Depends on:** Tasks 19–22, 25, 37–41. Files under `tests/Lms.E2ETests/M3/` (`StudentsTests.cs`, `ChangeRequestTests.cs`). Delta: the create scenario places the student in a class and asserts the auto-enrolled courses on the profile.

- [ ] **Steps 1–5** as the appendix. **Commit** `test(e2e): M3 students, guardians and change requests`.

### Task 48: E2E exit demo 3 — imports, auto-enrollment, restrictions, groups, transfer — lane: QA (QA2)

**PRD:** X-06, FR-STU-05, FR-ENR-01..05, FR-CRS-03, FR-GRP-01/02, DEV-PLAN §1 M3 exit ("students auto-enrolled, groups formed, a transfer performed"); M2 Task 36 student scenario. **Depends on:** Tasks 11, 23, 24, 26–28, 36, 42–44; Task 46 fixtures.

**Files:** Create `tests/Lms.E2ETests/M3/EnrollmentTests.cs`, extend `tests/Lms.E2ETests/Infrastructure/XlsxFiles.cs`.

**Scenarios (ar + en):** `Student_import_with_siblings_and_class_codes_creates_codes_one_shared_guardian_and_auto_enrollments` (30 rows, 2 siblings, guardian required at the school; credentials sheet downloaded once); `Christian_student_is_not_enrolled_in_islamic_course_counselor_requests_exception_supervisor_approves`; `Teacher_forms_two_religion_groups_using_suggestions_and_cannot_add_a_student_twice`; `Admin_transfers_student_to_parallel_class_and_guardian_gets_notification`; `Placement_import_with_full_class_in_block_mode_shows_error_report`.

- [ ] **Steps 1–5** as Task 46. **Commit** `test(e2e): M3 imports, enrollment, groups and transfer`.

---

## Execution notes for the Team Lead
- **Contract PRs first (`lead/m3-contracts-*`):** Task 1 before anything (one matrix bump for the whole milestone); Tasks 2, 3, 4 right after (may be one PR with Task 1 if small). Task 10's `IncludeCourses` contract field goes in with Task 3 if #50/#52 have merged by then, else in a follow-up contract PR.
- **M2 / M1 prerequisites:** BE3 Tasks 23 and BE1 Task 6 (import part), BE4 Task 26 need the import framework (#159 / PR #202) merged; FE Tasks 29 and 42 need the wizard (#169 / PR #195). Task 20 needs external login #168 / PR #193 merged (or adds the SSO skip as a follow-up). Task 10 needs M1 #50 and #52. Owner rule Q1 is testable now (`OrgUnit.MarkAsCourseManagementLevel` exists), but admins set the flag through M1 #48 / PR #148 — merge it before E2E Task 46. M3 BE work can start once Tasks 1–5 merge even if the M2 staff PRs are still in review; nothing in M3 edits `Features/Staff`.
- **Critical path:** Task 1 → Task 5 (BE1) → Task 12 (BE2) → Task 24 (BE4) → Task 21 (BE3) → Tasks 23 / 47 / 48. Review Task 5, 12 and 24 PRs first. BE2 may open Task 12 as a draft on top of the Task 5 branch.
- **BE lanes (split by folder; two agents never edit the same files):**
  - **BE1 — Courses (`Domain/Courses`, `Domain/Classes`/`Teaching`/`Enrollment` entity files in Task 5 only, `Infrastructure/Courses`, `Features/Courses`, `Infrastructure/Branches`, `Features/Enrollment/Exceptions`, migration `AddAcademicStructure`, `AddEnrollmentExceptions`):** 5 → 6 → 7 → 8 → 9 → 10 → 11. After Task 5 merges, entity files under `Domain/Classes|Teaching|Enrollment` pass to BE2/BE4 (behaviour methods added by them). If BE1 lags, Task 11 moves to BE4 (it only needs `IEnrollmentEngine`).
  - **BE2 — Classes, teaching, blueprints (`Domain/Access/IRelationshipScope*`, `Infrastructure/Access/RelationshipScope.cs`, `Api/Infrastructure/RelationshipAccess.cs`, `Features/Classes`, `Features/Teaching`, `Domain/Blueprints`, `Features/Blueprints`, `Infrastructure/Classes`):** 12 → 13 → 14 → 15 → 16 → 17.
  - **BE3 — Students and guardians (`Domain/People/*` additions, `Infrastructure/People/Students`, `Infrastructure/People/Guardians/GuardianLinker.cs`, `Features/Students`, `Features/Guardians` except portal files, `Domain/Identity/User.cs`, `Pages/Account/ChangePassword*`, the must-change lines in `Login.cshtml.cs` and `AuthorizationEndpoints.cs`):** 18 → 19 → 20 → 21 → 22 → 23. Task 21 waits for Task 24; BE3 fills the gap with Tasks 18–20 and may start Task 22's `StringSet` validator part early.
  - **BE4 — Enrollment, groups, transfers, portal (`Infrastructure/Enrollment`, `Features/Enrollment` except `Exceptions`, `Infrastructure/People/Guardians/GuardianAccountSync.cs`, `Features/Guardians/MyChildrenEndpoints.cs`, `GuardianPortalEndpoints.cs`):** 24 → 25 → 26 → 27 → 28 (+ 11 if moved). Task 24 can start as soon as Tasks 5 and 12 are in review.
- **Shared BE files (serialize merges):** `Program.cs` (two lines per module), `ImportResource.{ar,en}.resx` (BE1, BE3, BE4 append), `IsolationBypassGuardTests.cs` allow-list (Task 5), `ProbeRows/` (each PR its own file), `StampingInterceptor.cs` (Task 5 only), `AccountResource.{ar,en}.resx` (Task 20), `Lms.Shared` (Lead only).
- **Migrations:** Tasks 5, 11, 16, 18, 20, 22 add one each; whoever merges second regenerates on top of `main`.
- **FE lanes:** **FE1 — academic structure (`Pages/Courses`, `Pages/Blueprints`, `Pages/Classes`, `Pages/Teaching`, `Pages/Enrollment/Exception*`, `Pages/Organization/UnitCoursesTab.razor`; APIs `ICoursesApi`, `IBlueprintsApi`, `IClassesApi`, `ITeachingApi`):** 29 → 32 → 30 → 33 → 31 → 34 → 35 → 36. **FE2 — people and enrollment (`Pages/Students`, `Pages/Guardians`, `Pages/Me`, `Pages/Enrollment` except exceptions; APIs `IStudentsApi`, `IGuardiansApi`, `IChangeRequestsApi`, `IMyProfileApi`, `IEnrollmentApi`, `IGroupsApi`):** 37 → 38 → 39 → 43 → 40 → 41 → 42 → 44. Shared FE files: `NavCatalog.cs`, `ClientServices.cs`, both resx, `ClassPage.razor` (FE1 owns; FE2's tabs plug in), `IEnrollmentApi.cs` (FE2 owns; FE1 appends exception methods in Task 36). **FE is the bottleneck (8 tasks each vs 5–7 per BE):** if FE lags by more than one merge cycle, add FE3 (Lead may add agents) and give it Tasks 31 (blueprint builder), 35 and 44 — they share no files with the others.
- **QA:** QA1 reviews BE1, BE2 and FE1 PRs and owns Task 46; QA2 reviews BE4 PRs, owns Tasks 45 and 48; QA3 reviews BE3 and FE2 PRs and owns Task 47. Every review checks the Review Focus items, the `ISOLATION-BYPASS` marker and allow-list, explicit permissions, **a relationship-scope call on every restricted endpoint**, resource parity, RTL screenshots, and the one-E2E-stack lock.
- **Splits from DEV-PLAN §2 M3:** item 0 → Tasks 18–23 (BE), 25 (BE), 37–42 (FE), 47 (QA), plus new Task 20 (D24 sign-in); item 1 → 6 + 29; item 2 → 7 + 30 (+ Task 10 for the FR-ORG-06 remainder M1 Task 15 deferred); item 3 → 8 + 11 + 30 + 36; item 4 → 9 + 30; item 5 → 16 + 31; item 6 → 13 + 32; item 7 → 14 + 33; item 8 → 17 + 35; item 9 → 15 + 34; item 10 → 24 + 43; item 11 → 26 + 42; item 12 → 27 + 44; item 13 → 28 + 43. Kernels: Tasks 5 and 12.
- **Exit demo:** Tasks 46, 47, 48 green in CI in ar and en, Task 45 green on `main` = M3 done; then update `docs/STATUS.md` and record the owner's answers below as D37+ in `docs/PRD.md` §8. Update `docs/DEV-PLAN.md` M3 lines for the deferrals in Q8 when approved.

## Open questions for the product owner
Each has a recommended default that this draft already assumes.

1. **Where are courses owned, and what does the "course-management level" flag mean?** FR-CRS-01 says courses have an "owner unit"; FR-ORG-05 has an `IsCourseManagementLevel` flag with no stated effect. **Recommended:** a course is owned by the client root or by a unit flagged course-management level (e.g. each School); it can be allocated at or below its owner; edits (and its blueprint) are made at the owner unit, so a school-level supervisor can supervise only courses owned at their school or below. Courses owned above a unit are visible there read-only.
2. **Course code uniqueness:** unique in the whole client, or only within the owner unit? **Recommended:** unique in the whole client (Excel imports, reports and allocations refer to courses by code; schools that share a subject code create it once at the root and allocate it).
3. **Course restriction types:** FR-CRS-03 lists religion, gender, level and "custom attribute", but students have no custom attributes. **Recommended:** v1 ships Religion, Gender and Level (the level type of the class's unit); several types on one course must all match, any listed value within a type matches; custom attributes later. Changing a restriction does not remove students already enrolled (the screen warns how many no longer match).
4. **Who approves exceptional enrollment?** FR-CRS-03 says the Supervisor; the §3.4 matrix gives Supervisors only "View" on enrollment and Managers "Approve". **Recommended:** the course's supervisors approve; if the course has no supervisor, Managers (and Org Admins) at or above the class approve; Counselors, Managers and Org Admins can raise the request.
5. **Removing a course from a class that has enrolled students:** FR-CLS-05 blocks removal "if enrollments exist", but auto-enrollment (FR-ENR-01) enrolls every student immediately, so removal would always be blocked. **Recommended:** removal is allowed while all enrollments are automatic and have no activity (they become Dropped, history kept); it is blocked by manual/exceptional enrollments, group members and, from M4/M5, any content, submissions or grades.
6. **Limits of the relationship rules:** **Recommended:** built-in Teachers see only their own ClassCourses (and the class they are homeroom teacher of); built-in Supervisors may edit, allocate, assign teachers to and approve exceptions only for courses they supervise; custom roles have no such limit (they act on their whole unit), so an "Assistant teacher" custom role sees the whole unit. Alternative: let the role editor mark a custom role as "teacher-like".
7. **Transfers between schools:** FR-ENR-05 requires "same level/year". Is a mid-year move to a parallel class in **another school** of the group a transfer? **Recommended:** yes, when the level type and academic year match and the admin can see both units (e.g. a group-level admin); the student's home school changes and both schools keep their history.
8. **Deferring three items that depend on later milestones:** **Recommended:** the teacher schedule-conflict warning (FR-TCH-03) moves to M5 with the timetable; teachers adding lesson modules (FR-LP-05) moves to M4 with the module types (M3 delivers the plan view); the class view's timetable tab (FR-CLS-03) appears in M5.
9. **Blank default password (D24 follow-up):** when the admin or the Excel row leaves the default password empty, should the system generate one per student or refuse? **Recommended:** generate a strong one per student (no shared class password); it is shown once after create, emailed per D24, and for imports downloadable once as a credentials sheet within 15 minutes.
10. **Is class placement mandatory?** FR-STU-02 says "create student with class placement". **Recommended:** optional on create and import (schools register students before classes exist); an "Unplaced" filter lists students without a class in the selected year; placing later auto-enrolls them.

## PRD / DEV-PLAN inconsistencies found
- DEV-PLAN M3 includes FR-TCH-03 (conflict warning needs the M5 timetable), FR-LP-05 (module authoring needs M4 module types) and FR-CLS-03's timetable tab (M5) — Q8.
- FR-CRS-03 (Supervisor approves exceptions) vs §3.4 (Supervisor only views enrollment; Manager approves) — Q4.
- FR-CLS-05 "block removal if enrollments exist" vs FR-ENR-01 auto-enrollment — Q5.
- FR-LP-02 "copy blueprint from another course or year", but §7 has no year on blueprints; the plan reads "year" as an older version of any visible course.
- FR-CRS-03 "custom attribute" restriction type has no student attribute in §6.4/§7 — Q3.
- §3.4 Blueprints row gives Org Admin and Manager only "View" (supervisors alone edit); the plan follows it literally (OrgAdmin gets `Blueprints.View` only) — a client without supervisors cannot build blueprints until one is assigned.
- §7 has no class code, but Excel placement (FR-ENR-03, FR-STU-05) needs a class key — the plan adds `Class.Code`.
- FR-ORG-06 "copy with courses/allocations" was deferred from M1 Task 15 to M3 but is not listed in DEV-PLAN §2 M3 — added as Task 10 (and M1 #50/#52 are still open).
- D24 requires must-change-password at first sign-in, but `User` has no such flag and no M2 task built it — added as Task 20.
- The M2 appendix student task uses problem code `invalid_level_type`, which no contract defines yet — added in Task 1.
- FR-ENR-05 grade carry-over belongs to the M5 gradebook; M3 stores the link (`CarriedFromEnrollmentId`) and the setting only.
