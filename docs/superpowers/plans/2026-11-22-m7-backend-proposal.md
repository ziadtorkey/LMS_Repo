# M7 Backend Proposal: Year-End & Release (close year, grade-change approval, promotion, lifecycle, new year, SAML/LDAP, performance, security)

> **Status: DRAFT proposal for the Team Lead (no code).** Written by Backend 1 from `docs/DEV-PLAN.md` M7, `docs/PRD.md` FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06, FR-SEC-07/08, X-08, X-09, X-10, §9 and the owner decisions **D78-D87**, the approved contract **#812** (`lead/m7-contracts-1`), FE2's proposal **#808** (`fe/m7-proposal`, section 4 read), and the code on `main` plus the M5/M6 plans that are still in flight. Modelled on the M6 backend proposal (#613) and `2026-11-15-m4b-backend-proposal.md`. Items marked **(M5)** or **(M6)** depend on work that is not on `main` yet; everything else was read on `main`.

**Goal:** A client can end a school year safely and start the next one without engineering help: close the year (grades and attendance become read-only), approve late grade fixes, promote 50,000 students with a preview, a per-student job and a short undo window, withdraw, graduate and re-admit students, open the next year from a copy or a template, and let people sign in with SAML or LDAP. The whole product then passes the PRD §9 acceptance suite in Arabic and English, meets X-09 on a seeded 50,000-student client and a security review (X-08).

**Spec:** FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06, FR-SEC-07, FR-SEC-08 part 2, X-08, X-09, X-10, PRD §9; D12 (promotion undo), D16/D28 (role escalation), D17 (years are the Org Admin's), D18 (client-root settings), D22, D25 (student code kept), D30/D42, D58 (lock semantics), D78-D87. Master plan: `docs/DEV-PLAN.md` M7 items 1-8.

---

## 1. What M7 builds on

| Seam | Where | M7 use |
|---|---|---|
| Academic year | `Domain/AcademicYears/AcademicYear` (`Status` Planned/Active/Closed; `Close()` exists, "state exists so Closed years are read-only"), `AcademicYearEndpoints` (a closed year already answers 409 `year_closed` on edits of the year, terms and calendar), `IAcademicYearResolver` | The close action sets the status; the guard becomes load-bearing for every writer |
| Write guard | `Infrastructure/Enrollment/PlacementGuard.EnsureWritableAsync` (class Active and year not Closed, else `class_closed` / `year_closed`; reads the year at an ancestor unit through a reviewed bypass) | One place that already refuses writes into a Closed year for placements and enrolments; attendance and assignments reuse it (section 4.1) |
| Gradebook lock **(M5)** | `Gradebook.State = Locked`, `LockedAtUtc`, `IGradebookWriter` refuses with 409 `gradebook_locked` (M5 plan L5, Task 12 tests it with a test-only lock "for M7"); reads return `GradeSnapshot(Kind = Lock)` values; events after the lock are ignored and counted | Close turns the seam on; approvals write through a new writer method (section 4.2) |
| Attendance **(M5)** | sessions and marks; M5 plan makes closed-year sessions read-only through the year | The attendance lock is the year status, not a row flag (section 4.1) |
| Enrolment engine | `EnrollmentEngine` (placement, auto-enrolment by class-course rules, transfers, `StudentTransfer` history), `ClassStudent` (`Active`, `Removed`, `TransferredOut`; `To`, `EndReason`, `ActiveYearKey` = one active class per student per year), `CourseEnrollment`, `GroupMember` | Promotion places students in the target year through the engine, never by raw inserts |
| Level transfer | `OrgUnit.IsLevelTransfer`, `NextTransferUnitId`; `AcademicFlagsHandler`; the Level Transfer tab (FR-ORG-09) | The only source of the suggested outcome and target level (D83) |
| Person status | `StudentProfile.Status` (`Active`, `Deactivated`, `Expired`), `PersonStatusService` (recomputes the user's status from the profiles; a user with no active profile is Deactivated and cannot sign in, FR-SEC-07), `PersonStatusChange` history | Withdrawal and graduation drive the same path, so sign-in blocking is not new code |
| Guardians | `GuardianProfile`, `StudentGuardian` (unit-scoped link), `GuardianVisibility`, portal "my children" (D30/D42), `GuardianAccessGuardTests` allow-list | D81: the guardian keeps read access to a leaver's closed years (section 4.4) |
| Templates and copy | `SaveTemplateDialog`/`ApplyTemplateRequest` (placeholders), `BranchMaterializer` (copy of a branch), blueprints (M3) | New year from copy or template reuses the materializer pattern (section 4.5) |
| Jobs | `IBackgroundJobs` (Hangfire on MySQL: enqueue, keyed schedule that moves or cancels, recurring as system), `IJobKeyLock`, the import jobs (`ImportJobs`, progress polling), `AssignmentDueSoonJob`/`ZoomSyncSweepJob` per-client sweeps | Promotion, close and copy are keyed, resumable jobs; the effective-date sweep is a recurring system job |
| Identity providers | `IdentityProvider` (M2 Task 21), `IdentityProviderKinds` = Oidc, AzureAd, `ExternalSchemeRegistry` (dynamic schemes per client), `ExternalLoginLinker` (match by email / national id, first login links), `ProviderSecretProtector` (Data Protection, write-only secrets), `OutboundGuard` (SSRF) | SAML and LDAP plug into the same registry, linker, protector and guard (section 4.6) |
| Audit | `[Audited]`, `IAuditWriter.WriteSecurityEventAsync` (ids and counts only), `AuditCatalog` (#812 adds `GradeChangeRequest`, `PromotionRun`, `StudentExit`) | Every year-end action is audited |
| Notifications | `INotificationPublisher`; #812 adds `Grades.ChangeRequested`, `Grades.ChangeDecided`, `Promotion.Finished`, `Years.NextOpened` | Fired from the services below |
| Isolation guards | `IsolationBypassGuardTests`, `GuardianAccessGuardTests`, the M1/M2/M3/M4a/M4b matrix with `ProbeRows/*` modules | Every new bypass gets an allow-list entry; every route gets probe rows (`ProbeMilestones.M7`) |

The M5 and M6 chains are still being reviewed. Close and approvals cannot start before the M5 grading and attendance PRs (#629 and the gradebook chain) are on `main`; the rest of M7 can start earlier (section 10).

---

## 2. Decisions this proposal assumes (confirm in review; the owner's answers D78-D87 are already folded in)

| # | Decision | Source |
|---|---|---|
| L1 | **Close is a status flip plus a bounded lock, never a long synchronous job.** `POST /close` runs in one transaction: year `Closed`, every gradebook of the year `Locked` with `LockedAtUtc` (one `UPDATE` per client, not per row), `YearClosedDto` with the counts. The expensive part of the lock, the `GradeSnapshot(Kind = Lock)` rows, is filled by a keyed job per gradebook; until a snapshot exists a locked gradebook reads through the live calculation cut at `LockedAtUtc` (the M5 L5 "events after lock are ignored" rule already defines that cut). **Needs the M5 owner's agreement** (Q1). | D79, M5 L5, X-09 |
| L2 | **Attendance and assignments lock by year status.** No per-row flag: the writers ask `PlacementGuard`/`IYearWriteGuard` (the same call, extended to attendance marks, assignment submissions and grading, quiz attempts) and answer 409 `year_closed`. The sweep of writers is a task of its own (Task 3) with an architecture test that fails when a new write endpoint under a year-scoped route has no guard. | D79, FR-YEAR-04 |
| L3 | **One open grade-change request per (gradebook item, student)** (unique `OpenKey`), created only while the gradebook is locked (otherwise 409 and the normal entry route applies). The request carries the grade `Version` it was made against; approval re-checks it (409 `concurrency_conflict`, never a silent overwrite). | D80, #812 |
| L4 | **Approver is never the requester.** Enforced in the handler against the stored `RequestedByUserId` (403 `grade_change_not_allowed`), not in the UI, with a test that tries it as Manager, as Org Admin and as a user who holds both roles. A Manager approves only inside their units (`UnitAccess` at the class course's unit). | D80, QA #812 note |
| L5 | **An approved change is written as a new grade event of kind `ApprovedChange`** that counts after the lock (the only kind that does), refreshes the `GradeSnapshot(Lock)` of that student and gradebook in the same transaction, and writes an audited `GradeChangeRequest` decision. The original lock snapshot values stay readable in the history. Nothing edits a closed grade in place. | FR-GRD-06, D58 |
| L6 | **The promotion preview is computed, not stored.** A draft run stores only the run and the **overrides** the user made (`PromotionDecision`); the rows endpoint is a paged query over the source placements joined to the level mapping and the overrides. A draft of 50,000 students costs one row. At **execute** the run freezes the full set into `PromotionItem` rows (one per student) that the job processes. | D83, X-09, #812 |
| L7 | **Execution is per student and idempotent** (D84): each item moves `Pending` to `Done` or `Failed` in its own transaction through the enrolment engine; a crashed job resumes by taking the `Pending` and `Failed`-retryable items; re-running a run re-processes only failures. | D84 |
| L8 | **Undo restores only what the run did**, and only while `now < ExecutedAt + window` (setting `Years.PromotionUndoWindowMinutes`, default 10) **and** no activity exists in the target year for the run's students. "Activity" is defined by a probe interface each module implements (`IYearActivityProbe`: attendance marks, grade events or entries, submissions, quiz attempts, forum posts, enrolment exceptions, manual placements by someone other than the run), enumerated by an architecture test so a new module cannot forget it (Q2). Two codes: `promotion_undo_window_over`, `promotion_undo_activity_exists`. | FR-STU-07, D12 |
| L9 | **Old placements are completed, not deleted.** `ClassStudent.Status` gains `Completed` (with `To` = the source year's end date and `EndReason` = `Promoted`, `Repeated`, `Graduated`, `TransferredOut`); history stays readable; the new year gets new placements. | FR-STU-07 |
| L10 | **Withdrawal is a dated, reversible exit.** `StudentExit` (kind Withdrawn, Graduated, TransferredOut) with an effective date: today or earlier acts at once, a later date is applied by a daily system sweep at the client's local midnight. Acting = `StudentProfile.Status` to `Deactivated` through `PersonStatusService` (sign-in blocked, hidden from active lists), open enrolments dropped with reason `Exit`, group memberships ended, history kept. Re-admission creates a new placement, keeps the code (D25) and re-activates through the same service. | FR-STU-08, D25 |
| L11 | **The withdrawal reason and note are staff-only** at the query level: no student, guardian or portal DTO has the field, and the archive and profile history DTOs that carry it are served only under `Students.ViewArchive`/`Students.Withdraw` with an architecture test on the DTO property names. | D81, D82 |
| L12 | **Guardian read access after an exit** (D81) is a rule in `GuardianVisibility`: a linked guardian reads the child's **closed-year** published grades and attendance summary after withdrawal or graduation, never the open year and never the reason; the guardian's own account is not deactivated by the child's exit. | D81 |
| L13 | **The new year is created by a job from a request, idempotent per (source year, new year)**, copying structure only (classes, class courses, blueprint references, timetable periods; terms with shifted dates), never students; a template application reuses `ApplyTemplate`. Planned status, `year_overlap` and one Active year per defining unit are checked by the same rules as manual creation. | FR-YEAR-05, FR-ORG-07/08 |
| L14 | **SAML is SP-initiated only; LDAP is search-bind over LDAPS or StartTLS only; no group-to-role mapping** (D85). Both implement the existing provider abstraction, resolve users through `ExternalLoginLinker` (email or national id; first login links), store secrets only through `ProviderSecretProtector`, and send no request to a private address unless the development flag allows it (`OutboundGuard`). | D85, FR-SEC-08 |
| L15 | **Library choice for SAML and LDAP is decided by a short spike and an ADR** before the implementation tasks (section 4.6, Task 9): signature validation, wrapping attacks, replay, clock skew, licence and maintenance for SAML; TLS enforcement and binding behaviour for LDAP. | X-08 |
| L16 | **Every list in M7 is server-paged, filterable and sortable, and every bulk action is by filter, never by id list** (X-09). The 50,000-student pass is a task of its own with a seeder, a measurement harness and a written result; a query that is not index-backed does not ship. | X-09, #812 |
| L17 | **A year-end checklist is a read model, not stored state** (D86): computed from the year, its terms, unpublished items, open requests and the promotion state; the home pages ask for it from 60 days before the year end. | D86 |

---

## 3. Domain model (new entities) and migrations

All new tables follow the M3-M6 rules: tenant-owned rows carry `OrganizationId`; unit-scoped rows copy `OrgUnitId`/`OrgUnitPath` from the row they hang off; soft delete for business rows; history rows are append-only; `[Audited]` where a person changes a business fact; **an isolation test per table** (`Table_filter_hides_other_clients...`) and probe rows per route.

| Entity | Scope | Notes |
|---|---|---|
| `GradeChangeRequest` | unit (class course) | `ClassCourseId`, `GradebookId`, `GradeItemId`, `StudentProfileId`, `OldValue`, `NewValue` (points, null = no score), `Reason` (20-500 chars), `State` (Pending, Approved, Rejected), `RequestedByUserId`, `RequestedAtUtc`, `GradeVersion` (the version it was made against), `DecidedByUserId`, `DecidedAtUtc`, `DecisionNote`, `OpenKey` (unique with the item and student while Pending; the M5 `LiveKey` pattern). Audited. |
| `PromotionRun` | unit (source unit) | `SourceYearId`, `SourceOrgUnitId`, `TargetYearId`, `IncludeSubUnits`, `State` (Draft, Running, Done, PartiallyFailed, Undone, Failed), `Total`, `Processed`, `Failed`, `ExecutedAtUtc`, `UndoUntilUtc`, `ConfirmedCount`, `CreatedByUserId`, `OpenKey` (= the source unit while Draft or Running: one open run per source unit, 409 `promotion_in_progress`). Audited. |
| `PromotionDecision` | unit | An **override** only: `RunId`, `StudentProfileId`, `Outcome`, `TargetClassId?`. Unique (run, student). Absent = the suggestion stands. |
| `PromotionItem` | unit | Frozen at execute, one per student: `RunId`, `StudentProfileId`, `SourceClassStudentId`, `Outcome`, `TargetClassId?`, `State` (Pending, Done, Failed, Undone), `FailureCode`, `NewClassStudentId?`, `ProcessedAtUtc`. Unique (run, student); index (run, state) for the job and the failures list. No names, no grades. |
| `StudentExit` | unit (student's home unit) | Append-only history: `StudentProfileId`, `Kind`, `ReasonKey?`, `Note?` (staff-only), `EffectiveDate`, `AppliedAtUtc?`, `ByUserId`, `ReadmittedAtUtc?`, `ReadmittedByUserId?`, `LastClassStudentId?`. The archive is the latest unreadmitted exit per student. Audited. |
| `WithdrawalReason` | tenant | `Key` (stable, unique per client), `NameAr`, `NameEn`, `IsActive`, `SortOrder`; "Other" is built in and not stored. Managed at the client root. |
| `YearOpenRun` | unit (defining unit) | The new-year job: `SourceYearId?`, `TemplateId?`, `NewYearId?`, options JSON (parts, unit ids, dates), `State`, counts (classes, class courses, periods, units from template, skipped codes), `CreatedByUserId`. Idempotency key (source, new name). |
| `GradeSnapshot` rows | existing **(M5)** | `Kind = ApprovedChange` refreshes the lock snapshot of one student; no new table. |
| `ClassStudent.Status = Completed`, `EndReason` values | existing | Column values only. |
| `IdentityProvider` | existing | New kinds `Saml`, `Ldap` and their settings (SAML: IdP entity id, SSO URL, signing certificates, name-id and attribute names, clock skew; LDAP: host, port, TLS mode, base DN, bind DN, search filter, attribute names) in the existing configuration JSON plus protected secrets; a migration only if a column is needed (Task 10/11 decide). |

**Migrations (one per PR, a working `Down()`, whoever merges second regenerates on top of `main`; never duplicate another PR's migration, as the M5 chain taught us):** `AddGradeChangeRequests` (Task 4), `AddPromotion` (Task 5: run, decision, item), `AddStudentExits` (Task 7: exit, withdrawal reasons), `AddYearOpenRuns` (Task 8), `AddClassStudentCompleted` is data-only inside Task 5, `AddSamlLdapProviderSettings` only if Task 10/11 need columns. Close (Task 3) adds **no table**: it changes statuses and fills existing snapshot rows.

---

## 4. Designs

### 4.1 Close year and locks (FR-YEAR-04, D79, Task 3)

- **Preview** (`GET close-preview`, `AcademicYears.Close`, `UnitAccess` at the year's defining unit): counts of gradebooks and attendance sessions to lock, `Blocking` (none today except "year not Active"; the contract keeps the list for later rules) and `Warnings` (`UnpublishedGradeItems`, `OpenAssignments`, `OpenTerms`, `PendingGradeChangeRequests`, `PendingExcuseRequests`). Warnings never block (D79); every count is one grouped query per client, index-backed (X-09).
- **Close** (`POST close`, Org Admin only, D17): the caller types the year's name (`CloseYearRequest.ConfirmName`); only an `Active` year closes (409 `year_close_blocked` with the list otherwise); one transaction flips the year and locks the gradebooks (L1); an audit security event records counts only; the response is `YearClosedDto`. **No reopen in v1** (D79): the late-fix path is the approval flow (4.2). A recurring-safe job (`year-lock:{yearId}`) then fills the lock snapshots gradebook by gradebook in batches; it is keyed, resumable and idempotent, and the read path is correct before and after it (L1, tested both ways).
- **Writers**: the guard sweep (L2) adds `year_closed` to attendance marking, assignment submit and grade, quiz attempt and grading, forum posts in the year's class courses (policy question Q3: forums stay open?), enrolment exceptions and imports that touch a closed year's classes. Reads are untouched. An architecture test lists the year-scoped write endpoints and fails on one without the guard.
- **Closing report** (`GET closing-report`): per class counts (students, gradebooks, published, attendance sessions), read-only, from grouped queries.
- **Isolation**: the year is read at an ancestor unit through the existing reviewed bypass; the new close job reads class and gradebook rows by `OrganizationId` and the year's id only; allow-list entries with isolation statements for each new site.

### 4.2 Grade-change approval (FR-GRD-06, D80, Task 4)

- **Create** (`POST …/gradebook/change-requests`, `Grades.ChangeRequest`, relationship rule: a Teacher only for own ClassCourse, a miss is 404): validates a locked gradebook (else 409), the reason length (422 `validation_failed` on `Reason`), the grade `Version` (409 `concurrency_conflict`), one open request per grade (409 `grade_change_pending`). Notifies approvers (`Grades.ChangeRequested`, course name only, never a value).
- **Queue** (`GET /api/grade-change-requests`): approvers (`Grades.ChangeApprove`) see their unit subtree; a requester sees only their own; server-paged, filter by state, year, class course.
- **Decide** (`POST approve|reject`): L3-L5. Approve applies the value through `IGradebookWriter.ApplyApprovedChangeAsync` (the one method allowed to write into a locked gradebook), re-checks the version, recalculates that student's snapshot, writes the audit decision and notifies the requester (`Grades.ChangeDecided`, decision only). Reject needs a note. A request is decided once (409 on a second decision).
- **Tests that matter** (QA #812 notes): `A_requester_never_approves_their_own_change` (Manager requester is impossible by permission, but an Org Admin who is also a teacher of the class course, and a user holding both roles, are not); `A_manager_of_a_sibling_unit_cannot_see_or_decide_the_request` (404); `Approval_after_the_grade_moved_is_a_409`; `A_closed_grade_is_never_edited_in_place`; the snapshot and the audit row exist for every decision.

### 4.3 Promotion (FR-STU-07, D12, D78, D83, D84, Tasks 5 and 6)

**Create and preview.** `POST /api/promotion/runs` (`Promotion.Run`; Org Admin anywhere, Manager inside their units, D78; `UnitAccess` on the source unit): requires the source year `Closed` (409 `year_not_closed`, D79) and the target year Planned or Active, one open run per source unit (L6 `OpenKey`, 409 `promotion_in_progress`). The rows endpoint is a **paged query** (`PromotionRowsQuery`: outcome, class, problems only, search; server-paged and sortable) over the source placements (`ClassStudent` of the source year, in the caller's scope, active) with: the suggested outcome and target level from `NextTransferUnitId` (D83: none from grades), the target class suggestion (rule in Q4), the user's override from `PromotionDecision`, and `ProblemCode` / `WarningCodes`:

- Blocking (`ProblemCode`): `NoLevelMapping`, `TargetClassMissing`, `AlreadyPlacedInTargetYear`, `TargetYearClosed`, `SourceYearNotClosed`. A top level with no `NextTransferUnitId` suggests **Graduate** (D83), so "no mapping" is a block only for a mid-level unit that should have one.
- Warnings (D79, D84): open grade-change requests, open excuse requests, over capacity of the target class, unpublished items. Warnings never block.

**Decisions.** `PUT …/decisions` (at most 200 rows per call, a Draft run only, 409 `promotion_conflict` otherwise) and `PUT …/decisions/filter` (a **filter**, never a list of ids; `Outcome = null` resets to the suggestion) write overrides. Counts (`PromotionCountsDto`) come from one grouped query over the same preview query.

**Execute.** `POST …/execute` (202): the caller confirms the student count (`ConfirmStudentCount` must equal the preview total, 422 otherwise), blocking issues are 409 `promotion_conflict`. The request freezes the full set into `PromotionItem` rows in bulk (batches of 5,000, one transaction each, resumable), sets the run `Running` and enqueues the keyed job `promotion-run:{runId}` **with the caller's captured context**, so a Manager's run stays inside their scope end to end (the announcement jobs of M6 use the same pattern). Per item the job, in its own transaction: re-checks the item is still `Pending`, completes the source placement (L9), places the student in the target class through the enrolment engine (auto-enrolment by class-course rules, capacity re-checked), applies `Graduate`/`TransferOut` through the lifecycle service (4.4, no sign-in wait: the effective date is the source year end or today, whichever is later), records `NewClassStudentId` or a stable `FailureCode`, and bumps the run's counters with one conditional `UPDATE`. A failure never stops the run (D84). The run ends `Done`, `PartiallyFailed` or `Failed` (job crash with nothing processed), sets `UndoUntilUtc` (L8) and sends `Promotion.Finished` to whoever ran it.

**Failures and re-run.** `GET …/failures` lists failed items with their code; fixing the cause (a missing target class, a full class) and `POST execute` again on a `PartiallyFailed` run re-processes only failed items (L7).

**Undo.** `POST …/undo` (202, same permission): refuses outside the window (409 `promotion_undo_window_over`) or when any probe reports activity (409 `promotion_undo_activity_exists`), then, as a keyed job, restores only items in `Done`: removes the new placements and enrolments (soft delete), reopens the completed source placements, restores graduated or transferred-out students through the lifecycle service. Items that were `Failed` are untouched. The run ends `Undone`. The window and the activity rule are **server facts**: `UndoUntilUtc` and `ServerNowUtc` are in every run DTO so the client shows the clock time, not only a countdown.

**Scale (X-09).** The preview query is one indexed join (`ClassStudent (ClassId, Status)`, `StudentProfile` by id) with `COUNT` over the same predicate; a 50,000-row run is browsed 50 rows at a time. Execution is 200 students per job slice with progress written every slice; a throughput target (students per second) is measured in Task 13 and the batch size tuned from it. The job never loads all items into memory.

**Isolation.** The target year and the next-level unit usually sit **above** the Manager's scope: they are read by id and client through two reviewed bypass sites (`PromotionYearReader`, `LevelMappingReader`) that return only ids, names and flags. The rows query itself runs under the caller's filters, so a Manager of one school never sees another school's students in the preview, the failures or the history (test per route).

### 4.4 Withdrawal, graduation, archive, re-admission (FR-STU-08, D81, D82, Task 7)

- **Withdraw / graduate** (`Students.Withdraw`: Org Admin, Manager, Counselor, D82; `UnitAccess` at the student's home unit): `WithdrawStudentRequest(ReasonKey, Note, EffectiveDate)` validates the reason against the client list or "Other" (a note is then required), 409 `student_already_inactive`, 422 `effective_date_invalid` (not before the student's registration or the last placement start; an upper bound of one year ahead). L10. **No bulk withdrawal in v1** (D84); a class of leavers is individual calls (the FE may loop, the API offers no bulk).
- **Effective date**: `student-exit-effective` is a recurring system job (per client, local midnight from `Org.TimeZone`) that applies due exits through `PersonStatusService`, so sign-in is blocked from the effective date and not before (FR-SEC-07), idempotent, keyed per exit.
- **Re-admit** (`Students.Readmit`: Org Admin, Manager): into a class of an Active or Planned year, capacity checked (409 `readmit_class_full`, `readmit_year_closed`), placement through the engine, the student code is kept (D25), the exit row gets `ReadmittedAtUtc`.
- **Archive** (`GET /api/students/archive`, `Students.ViewArchive`: Org Admin, Manager, Counselor, Supervisor): the latest unreadmitted exit per student with the last class and year names, search by name or code (indexed key columns), filter by kind, year, unit; server-paged; **never a national id**; `CanReadmit` computed from the caller's permission and unit.
- **Reasons** (`GET/PUT /api/withdrawal-reasons`): the list is read by anyone who may withdraw; **writing needs its own permission** (QA #812 note): this proposal asks the Lead for `Students.ManageWithdrawalReasons` (Org Admin only, client root) instead of overloading `Settings.Update` (Q5). The built-in "Other" cannot be edited; deactivating a reason hides it from new withdrawals and never rewrites history.
- **Guardians** (L12, D81): `GuardianVisibility` gains the closed-year read for exited children; the portal DTOs never carry the reason. The guardian-access allow-list entry names this reason.
- **Interaction with promotion**: promotion's `Graduate` and `TransferOut` outcomes create the same `StudentExit` rows (one code path), so the archive shows them with no reason.

### 4.5 Open the next year (FR-YEAR-05, Task 8)

`POST open-next/preview` returns counts and skipped items with nothing written; `POST open-next` (202, `AcademicYears.Create`, an Org Admin of the defining unit) creates a `YearOpenRun` and enqueues the keyed job `year-open:{runId}`. The job creates the Planned year (dates and name from the request, terms copied with **shifted dates** by the year offset, never past the new year's bounds), then copies the chosen `YearCopyParts` for the chosen units in batches inside per-unit transactions: classes (new year, same codes and capacities, no students), class courses (same rules, teachers **not** copied unless Q6 says so), blueprint references and timetable periods; a template is applied through the existing `ApplyTemplate` with `{Year}` placeholders. Checks: `year_overlap`, `copy_nothing_selected` (400), one Active year per defining unit. The run reports counts and skipped codes; `Years.NextOpened` notifies the unit's Org Admins; re-posting the same request returns the same run.

### 4.6 SAML 2.0 and LDAP (FR-SEC-08 part 2, D85, Tasks 9-11)

- **Common**: new `IdentityProviderKinds.Saml` and `Ldap` beside Oidc and AzureAd; configuration validated on save (field rules, URL and host through `OutboundGuard`), secrets write-only and protected, a **Test connection** endpoint (`POST …/{id}/test`) returning a list of named checks and never a secret (`provider_test_failed` 422), and SP metadata for SAML. Users are matched by email or national id by the existing linker; the first login links the account; an unmatched user gets the generic "no account" answer. No group-to-role mapping: roles stay in the LMS (D85).
- **SAML (SP-initiated only)**: AuthnRequest with a stored request id (replay and unsolicited-response protection), RelayState bound to the request, response **and** assertion signature validation against the configured IdP certificates (rotation: two certificates at once), audience and recipient checks, `NotBefore`/`NotOnOrAfter` with a configured skew, one-time assertion id cache, no XML external entities, wrapping-attack tests from the published corpus. Built on the dynamic scheme registry (one scheme per provider, per client, as OIDC).
- **LDAP (search-bind)**: LDAPS or StartTLS only (a plain `ldap://` configuration is rejected on save and at connect), certificate validation on (a custom CA bundle per provider is allowed, "trust all" is not), bind with the service account, search by a filter template with **escaped** user input (LDAP injection test), then a bind as the found DN with the typed password; the password exists only in the request body and one call frame: never logged, never in a URL, a generic failure message, per-user and per-IP rate limit on top of the existing auth limiter. Timeouts and a size limit on every search.
- **Library choice**: an ADR from the spike (Task 9, QA + BE1) decides between the maintained candidates for each protocol; the open-source licence, last release, CVE history and whether it fits the dynamic scheme model are the criteria (L15).
- **Probe rows and tests**: Org Admin of the client root only (D18); a sibling-unit admin, a Manager and another client get 403/404; an LDAP and a SAML fake (in-memory IdP and directory) drive the integration tests; no real directory in CI.

### 4.7 Performance pass X-09 (Tasks 12 and 13)

- **Seeder**: a development-only command (`dotnet run --project src/Lms.Api -- seed-perf --students 50000`) builds one client with a realistic tree (a few schools, ~1,500 classes, ~10 class courses each, enrolments, grade entries for a configurable share, guardians, staff) in bulk inserts, deterministic from a seed so runs are comparable; never reachable in production (environment guard, a test).
- **Harness**: an xUnit-based perf project (`tests/Lms.PerfTests`, not part of the normal suite) that starts the API against a Testcontainers MySQL with the seeded client and measures p50/p95/p99 over repeated calls for the list pages that matter: students, staff, classes, enrolments, gradebook grid, attendance, promotion rows, archive, notifications, audit; results written as a Markdown table in the PR and kept in `docs/perf/`. Runs nightly and on demand, **not per PR** (seeding time, Q7).
- **Tuning**: `EXPLAIN` review of every list query, composite indexes for the filter and sort columns, no `COUNT(*)` over unfiltered large tables on every page (cached or bounded where acceptable), keyset paging where offset degrades, search on normalised key columns with a prefix index (`NameArKey`/`NameEnKey` exist), projection instead of entity loads, `AsNoTracking`, no N+1 (a test counts queries per endpoint). Excel export streams (X-09: exportable) and is itself measured.
- **Budget**: p95 below 2 s per list page at 50,000 students (X-09), and a promotion preview page and a 50,000-student execution within an agreed job time (target to set after the first measurement).

### 4.8 Security review X-08 (QA3 lead; BE fixes)

An OWASP ASVS L2 pass over the whole product: the full authorization matrix (every route has probe rows; M5/M6/M7 rows added to the matrix and a test that **fails on a mapped route with no probe row**), the isolation tables, mass-assignment and over-posting on every request DTO, rate limits and lockout, headers (CSP, HSTS, cookie flags), upload paths, SSRF through providers and webhooks, secret hygiene (`gitleaks`-style scan in CI), dependency vulnerabilities (`dotnet list package --vulnerable` in CI), logging of personal data, and the SAML/LDAP specifics above. Findings are filed as `bug` + `sev:*` against the owning lane; no open Critical or High at release (PRD §9).

---

## 5. Endpoints, permissions, relationships, isolation, probe rows

Routes are exactly the ones in #812 (`Routes.YearEnd.cs`); a relationship miss is 404, a missing permission 403, an unknown id 404. Each route lands with its probe rows (`ProbeMilestones.M7`) in the PR that adds it; rows cover RootAdmin, School1Admin, School2Admin, School1Teacher, ClientBAdmin, PlatformAdmin, Anonymous, and for the Manager-scoped routes a **Manager-of-sibling-unit** caller.

| Area | Permission | Who (built-in defaults in #812) | Relationship / scope |
|---|---|---|---|
| Close preview, close, closing report | `AcademicYears.Close` (report: `AcademicYears.View`) | Org Admin only (D17) | `UnitAccess` at the year's defining unit |
| Open next year | `AcademicYears.Create` | Org Admin | defining unit |
| Grade-change create | `Grades.ChangeRequest` | Teacher (own ClassCourse), Manager, Org Admin | `RelationshipAccess.CheckClassCourseAsync` |
| Grade-change queue / decide | `Grades.ChangeApprove` | Manager, Org Admin | unit subtree; never the requester (L4) |
| Promotion (all routes) | `Promotion.Run` | Org Admin; Manager inside their units (D78) | `UnitAccess` on the source unit; rows clipped by scope |
| Withdraw, graduate | `Students.Withdraw` | Org Admin, Manager, Counselor (D82) | student's home unit |
| Re-admit | `Students.Readmit` | Org Admin, Manager | student's home unit and the target class's unit |
| Archive | `Students.ViewArchive` | Org Admin, Manager, Counselor, Supervisor | unit scope |
| Withdrawal reasons | read `Students.Withdraw`; write **`Students.ManageWithdrawalReasons` (new, asked of the Lead)** | read: those who withdraw; write: Org Admin | client root (D18) |
| Identity providers (SAML, LDAP) | `IdentityProviders.Manage` (existing) | client-root admin (D18) | client root |

**D16 / D28 escalation guard (QA #812 note).** The Manager now holds `Grades.ChangeApprove`, `Promotion.Run`, `Students.Withdraw`, `Students.Readmit`, `Students.ViewArchive`. The role-assignment rule (an assigner cannot grant a role holding a permission they lack, D16/D28) is unchanged and must stay so: `BuiltInRoleDefaults` gets a comment at the M7 block stating that these permissions flow only to Manager and Org Admin by default and that **`AcademicYears.Close` is deliberately absent from the Manager** because the D16 guard would otherwise let a Manager assign a role that can close a year; a `BuiltInRoleDefaults` version bump with a reconciler test per M5/M6 precedent (`CurrentVersion` is shared with the in-flight chains: the Lead sequences it) and a test that a Manager cannot assign Org Admin or any role containing `AcademicYears.Close`.

**Isolation bypass sites (new, each with an allow-list entry and an isolation statement):** (1) `PlacementGuard`/year guard extended to attendance and the other writers (existing site, new callers); (2) `PromotionYearReader` (target year at an ancestor unit, ids and flags only); (3) `LevelMappingReader` (next-level unit and its classes above the scope, ids and names); (4) `YearActivityProbes` (reads module tables by class course ids of the run within the client; counts only); (5) `GuardianVisibility` closed-year read for exited children (guardian-linked, closed years, published values only); (6) `StudentExitSweep` (system job, per client, by id); (7) `YearOpenJob` (defining unit's subtree, client-matched). `GuardianAccessGuardTests` gets entries for sites that read guardian rows.

**Isolation tests (one per table, plus behaviours):** `GradeChangeRequest`, `PromotionRun`, `PromotionDecision`, `PromotionItem`, `StudentExit`, `WithdrawalReason`, `YearOpenRun` (table filters); a **Manager of a sibling unit** cannot read, run, fail, undo, withdraw, re-admit or find in the archive anything of the other unit (404, every route); client B sees nothing of A; a platform context run is pinned to the run's own client by hand (the M6 lesson: every query in a job carries `OrganizationId` explicitly, tested with a staff and a student chip equivalent).

---

## 6. Audit, notifications, settings

- **Audit** (`[Audited]` + security events with ids and counts only): `GradeChangeRequest` create/approve/reject, `PromotionRun` create/execute/undo with counts, close year, `StudentExit` withdraw/graduate/readmit (no note text in the event), `WithdrawalReason` list saves, identity provider create/update/test (no secret, no certificate body). A test per catalog entry that the event is written and carries no personal text.
- **Notifications** (events from #812; titles and names only, never a grade value, a reason or a note): `Grades.ChangeRequested` to approvers (ancestors-and-self scope), `Grades.ChangeDecided` to the requester, `Promotion.Finished` to whoever ran it, `Years.NextOpened` to the unit's Org Admins. Through the M6 publisher, in-app by default (the catalog decides channels).
- **Settings** (client level, bounds in the catalog): `Years.PromotionUndoWindowMinutes` (default 10, range 1-60, FR-STU-07), `Years.EndChecklistDays` (default 60, D86). A guard test that both are editable at the client root only.
- **Year-end checklist read model** (L17): `GET /api/me/year-end-checklist` (route and DTO are a contract gap, section 8) returns the steps and their state (close, publish grades, approve changes, run promotion, open next year) for the years the caller manages, only from 60 days before the year end.

---

## 7. Jobs (Hangfire; keyed, resumable, idempotent, context captured or system as noted)

| Key / id | Trigger | Context | Notes |
|---|---|---|---|
| `year-lock:{yearId}` | after close | system, per client | fills lock snapshots in batches per gradebook; safe to re-run; reads are correct before it finishes (L1) |
| `promotion-prepare` / `promotion-run:{runId}` | execute | the caller's captured context | freezes items, then processes in slices of 200; resumes `Pending` items after a crash; writes progress every slice |
| `promotion-undo:{runId}` | undo | the caller's captured context | restores `Done` items only |
| `year-open:{runId}` | open next year | the caller's captured context | per-unit transactions; idempotent per (source, new name) |
| `student-exit-effective` | recurring, daily at local midnight per client | system | applies due exits; keyed per exit; counts and failures logged without personal data |
| `year-end-reminder` (optional) | recurring daily | system | only if the owner wants a notification as well as the home checklist (Q8) |

Dev triggers (`DevJobDefinition`) exist for the daily sweep so E2E can run it; none in production.

---

## 8. Contracts the Lead still has to add (gaps found while writing)

#812 covers close, grade changes, promotion, lifecycle, archive, reasons and open-next. Missing for the backend tasks and FE H7/H8:

1. **Withdrawal-reasons write permission** (`Students.ManageWithdrawalReasons`, Q5) and the `Years.PromotionUndoWindowMinutes` and `Years.EndChecklistDays` setting keys.
2. **Identity provider contract**: `IdentityProviderKinds.Saml`/`Ldap`, their config DTOs (secrets write-only), `POST …/{id}/test` with `ProviderTestResultDto`, SP metadata for SAML, the LDAP sign-in route and request (the one place the app receives an external password), the generic failure codes.
3. **Year-end checklist** route and DTO (FE H8).
4. **Close job state**: if L1 is accepted, a way for the client to see "locks are being finalised" (a field on `YearClosedDto` or the closing report); not blocking.
5. **Problem codes**: `active_year_exists` (in FE's list, absent from #812), `lifecycle_*` none needed; `year_close_blocked` carries the blocking list in `extra`.
6. `PromotionRunDto.JobId` (FE proposes it) is not in #812: the run id doubles as the job handle in this design; confirm.

---

## 9. Migrations (see section 3 for names)

`AddGradeChangeRequests` (Task 4) → `AddPromotion` (Task 5) → `AddStudentExits` (Task 7) → `AddYearOpenRuns` (Task 8) → provider settings only if needed (Tasks 10/11). Each PR carries exactly its migration and snapshot, a working `Down()`, a drift check, and never regenerates another PR's. Because Tasks 4, 5, 7 and 8 run in parallel on different lanes, the Lead sequences merges; the second to merge regenerates on `main`.

---

## 10. Task breakdown (sizes: S ≤ 0.5 day, M ≈ 1-2 days, L ≈ 3-5 days of one backend agent; lanes BE1-BE4; QA/UX/FE shown for dependencies)

| # | Task | Lane | Size | Depends on | Acceptance |
|---|---|---|---|---|---|
| 1 | **Contract wave 1** (#812): routes, DTOs, permissions, codes, events, audit entries | Lead | - | approved | merged |
| 2 | **Contract wave 2**: reasons permission, settings keys, identity provider kinds and DTOs, checklist, codes (section 8) | Lead | S | 1 | merged before Tasks 7, 10, 11, 14 |
| 3 | **Close year and locks**: preview, close, closing report, lock snapshot job, year-guard sweep over attendance, assignments, quizzes, forums, imports; architecture test | BE1 | L | M5 grading and attendance on `main`, 1 | close/lock/closed-year-write tests, `Close_is_correct_before_and_after_the_snapshot_job`, the sweep test, isolation, probe rows |
| 4 | **Grade-change approval**: entity, create, queue, approve/reject, writer method, snapshot refresh, notifications, audit | BE2 | M | 3 (lock), 1 | requester-never-approves (3 variants), sibling-unit Manager 404, version conflict 409, audit rows, isolation, probe rows |
| 5 | **Promotion kernel**: run, overrides, computed preview and counts, rows/filter routes, blockers and warnings, level-mapping and year readers | BE3 | L | 1, M3 level mapping | preview of 50,000 in the perf budget, filter bulk by query, one open run per unit, D83 suggestions, isolation, probe rows |
| 6 | **Promotion execute, failures, undo**: freeze items, per-student job, counters, re-run, undo with the activity probes, `Promotion.Finished` | BE3 | L | 5, 3 | per-student idempotence and resume after a kill, partial failure and re-run, undo window and activity (two codes), architecture test for probes, audit |
| 7 | **Lifecycle**: withdraw, graduate, readmit, archive, reasons, effective-date sweep, guardian closed-year read, exit rows shared with promotion | BE4 | L | 2, 1 | sign-in blocked from the effective date, history kept, code kept, reason staff-only (DTO test), guardian reads closed years only, archive search at scale, isolation, probe rows |
| 8 | **Open next year**: preview, job, terms shift, template apply, idempotence | BE2 | M | 1, M3 templates | counts match the preview, no students copied, `year_overlap`, idempotent re-post, isolation, probe rows |
| 9 | **Spike and ADR: SAML and LDAP libraries and attack tests** | QA + BE1 | S | 2 | ADR merged with the chosen libraries and the attack test list |
| 10 | **SAML provider** (SP-initiated) with a fake IdP | BE1 | M | 9 | signature, wrapping, replay, audience, skew tests; first-login linking; secrets never returned |
| 11 | **LDAP provider** (search-bind, TLS only) with a fake directory | BE2 | M | 9 | TLS-only enforcement, injection test, no password in logs, rate limits, linking |
| 12 | **50,000-student seeder** and the production guard | BE4 | M | 7 (shapes) | deterministic, bulk, environment-guarded, documented |
| 13 | **X-09 pass**: perf harness, EXPLAIN review, indexes, query-count tests, Excel export streaming, results doc | BE1 + BE3 | L | 12, 5 | p95 < 2 s per list page, promotion preview and execution targets met, results in `docs/perf/` |
| 14 | **Year-end checklist read model, settings, home data** | BE2 | S | 2, 3, 6 | correct from 60 days before the end, per role, no stored state |
| 15 | **M7 probe rows and isolation suite** (Manager of a sibling unit included), grows per BE merge | QA2 | M | each BE PR | matrix green; the "mapped route without probe row" test |
| 16 | **Security review X-08** and fixes | QA3 + owning lanes | L | 3-11 | no open Critical/High; checklist signed |
| 17 | **Accessibility pass X-10** (WCAG 2.1 AA, ar/en, 360 px) | FE + UX | L | FE tasks | axe/manual checklist, no open serious issues |
| 18 | **PRD §9 acceptance E2E suite**, ar and en | QA | L | 3-8, FE | green in both languages |

**Order and parallelism.** Tasks 5 and 7 and 9 can start as soon as #812 (and Task 2 for 7 and 9) merge, in parallel with the M5/M6 reviews; Tasks 3 and 4 wait for the M5 grading and attendance PRs on `main`. Suggested waves: **A** (Tasks 2, 5, 7, 9), **B** (3, 6, 8, 10, 11, 12), **C** (4, 14, 13), **D** (15-18 with the release pass). Four backend agents keep one L task each; BE3 owns the promotion end to end because 5 and 6 share the run model.

---

## 11. Open questions (defaults in force are this proposal's assumptions)

| # | Question | Default |
|---|---|---|
| Q1 | **Lock snapshots** (L1): is "flip the status, lock gradebooks with one update, fill the snapshots by a job, read live-cut meanwhile" acceptable to the M5 owner, or must the lock snapshot exist at close? A synchronous snapshot of 500k+ rows will not fit a request. | L1 as written |
| Q2 | **What counts as activity** for the undo rule (L8)? Proposed: any attendance mark, grade event or entry, submission, quiz attempt, forum post, enrolment exception or manual placement in the target classes after the run's execute time; system actions of the run itself excluded. | the proposed list |
| Q3 | After close, do **forums and messaging** stay open in a closed year's class courses (D79 speaks of grades and attendance)? | forums stay open; grades, attendance, assignments, quizzes lock |
| Q4 | **Target class suggestion** across levels (Grade 5 A to Grade 6 A): by the same section code, by class name suffix, or only "the next level's unit, class chosen by hand" when ambiguous? | same section code when unique in the target level, else blank and a warning |
| Q5 | **Permission to manage withdrawal reasons**: a new `Students.ManageWithdrawalReasons` (Org Admin) or an existing settings right? | new permission |
| Q6 | Are **teachers copied** with the class courses when opening the next year? | not copied (assigned fresh each year) |
| Q7 | **Perf run cadence and environment**: nightly plus on demand on a larger runner, or per PR on a reduced 5,000-student client with the 50,000 run before release? | nightly + on demand; a small smoke per PR |
| Q8 | Besides the home checklist, a **notification** 60 days before the year end? | home checklist only |
| Q9 | **Repeat outcome**: which class in the target year (same level, same section, capacity-aware)? | same level, same section code, else chosen by hand |
| Q10 | **Graduating students' guardians**: do they keep portal access if **all** their children left (D81 says read access to closed years)? | yes, closed-year read only |
| Q11 | **Future-dated withdrawal while a promotion runs**: the promotion skips students with a pending exit and lists them as a warning? | skipped with a warning code |
| Q12 | **LDAP/SAML test accounts** for the release pass: fake IdP/directory containers in CI only, no real directory | fakes only |

---

## 12. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Locking and snapshotting a 50,000-student year inside a request | timeouts, partial close | L1: a status flip plus a bounded update, a resumable job for snapshots, live-cut reads in between, tested both ways |
| Promotion at scale (preview, execute, undo) | slow pages, long jobs, memory | computed preview with overrides only, filter-based bulk, per-student transactions in slices of 200, no full load; throughput measured in Task 13 |
| A Manager's run leaks across units | isolation breach | the run executes under the captured context, the rows query uses the caller's filters, the two bypass sites return ids only, a sibling-unit Manager test on every route |
| A write path forgets the year guard | edits to a closed year | an architecture test over year-scoped write endpoints (L2) and a closed-year test per module |
| Undo after hidden activity | data loss in the new year | the activity probe interface with an enumeration test; if in doubt the answer is `promotion_undo_activity_exists` |
| Reason or note leaks to guardian or student | privacy (D81) | staff-only DTOs, an architecture test on DTO property names, guardian DTO tests |
| SAML signature/wrapping flaws, LDAP injection or plaintext bind | account takeover | ADR from a spike, attack-corpus tests, TLS-only and escaped filters, fakes in CI, QA3 review |
| `BuiltInRoleDefaults` version and migration ordering across parallel chains | duplicated migrations or role drift | the Lead sequences versions and merges; one migration per PR; reconciler tests; never duplicate another PR's migration |
| Perf seeding makes CI slow or flaky | the X-09 gate is skipped | nightly job on a larger runner, deterministic seed, a small per-PR smoke |
| M5/M6 merges delay close and approvals | M7 slips | waves A and B start without them; Tasks 3 and 4 start the day the M5 PRs merge |

---

## 13. PRD / DEV-PLAN points to confirm or amend

1. FR-STU-07 says "target class" per student; the level mapping gives a **unit**, not a class. The class rule (Q4) should be written into the PRD.
2. FR-STU-08 lists "archive search (read-only)" and "re-admission" without a permission split; #812 gives re-admission to Org Admin and Manager only (not Counselor). Confirm the Counselor split is intended.
3. D79 says closing "locks grades and attendance"; the lock list (L2, Q3) should name assignments and quizzes explicitly.
4. FR-GRD-06 says "Manager approval"; D80 adds the Org Admin. Keep PRD and D80 aligned.
5. DEV-PLAN M7 item 7 pairs the accessibility pass with the security review; this proposal treats them as separate tasks (16, 17) with different owners.
6. PRD §9 acceptance names ar + en runs; the E2E suite (Task 18) should declare the performance seed and the two languages as separate jobs.

---

## 14. Folded in from the reviews of #812 and #808

| Note | Where addressed |
|---|---|
| A requester never approves their own change (test) | L4, 4.2, Task 4 acceptance (three variants: Manager, Org Admin, a user holding both roles) |
| A Manager of a sibling unit is isolated (test) | section 5 isolation tests, every Manager-scoped route, QA2's Task 15 |
| D16 escalation-guard comment on the new built-in role block | section 5, D16/D28 paragraph: comment, version bump, reconciler test, "a Manager cannot assign a role with `AcademicYears.Close`" |
| Withdrawal-reasons permission | 4.4, section 8 item 1, Q5 (`Students.ManageWithdrawalReasons`) |
| FE #808 section 4: close, grade changes, promotion, lifecycle, new year, providers; server rules for fakes | sections 4.1-4.6 follow the same routes; section 4.2 of #808 (locks, preview determinism, one run per unit, two undo codes, per-student idempotence, history readable, code kept, reason required, secrets never returned) is each a test in Tasks 3-8, 10, 11; the FE codes list (`active_year_exists`, `provider_test_failed`) is in section 8 |
| FE risk: a client that downloads all promotion rows is a bug | L6, L16: server paging and filter-bulk are the only routes; no list-of-ids endpoint exists |
