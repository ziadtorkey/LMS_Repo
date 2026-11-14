# M7 Year-End & Release Implementation Plan

> **Status: DRAFT for review (Team Lead, 2026-11-24).** Merges the backend proposal (PR #822, `be/m7-proposal`, BE1), the frontend proposal (PR #808, `fe/m7-proposal`, FE2, revision 2), contract wave 1 **#812** (`lead/m7-contracts-1`, QA-approved at `91c599ed`), the wave 2 draft that BE1 is writing as `lead/m7-contracts-2` (SSO, the undo setting, the withdrawal-reasons permission), the design spec draft **#814** (`ux/m7-design`) and the components PR **#816** (`ux/m7-components`, on #815) into one task list for **M7**. Owner decisions applied: **D78** to **D87** and **D88** to **D91** (all 2026-11-14; D88–D91 answer this plan's O1–O4, revision 2). QA's four notes on #812 become tests (section "QA notes on #812 folded in"). Where the two proposals disagree, the Lead's resolution is an **L** decision below. The FE PRs already open on fakes (#815 H1, #818 H2, #823 H5) are kept and rebased on the merged contracts; this plan wins where they differ. Team: Lead, BE1–BE4, FE1, FE2, QA1–QA3, UI/UX Designer.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. In this project each task = one GitHub issue and one PR; the lane is marked on each task.

**Goal:** A client can end a school year safely and start the next one without engineering help: close the year (grades, attendance and the work that produces grades become read-only), approve late grade fixes, promote up to 50,000 students with a preview, a per-student job and a short undo window, withdraw, graduate and re-admit students, open the next year from a copy or a template, and let people sign in with SAML or LDAP. The whole product then passes the PRD §9 acceptance suite in Arabic and English, meets X-09 on a seeded 50,000-student client, passes the X-08 security review and the X-10 accessibility pass (WCAG 2.1 AA, D87).

**Architecture:** Same modular monolith. Feature areas: **YearEnd** (`Domain/YearEnd`, `Infrastructure/YearEnd/{Close,Promotion,OpenNext,Checklist}`, `Features/YearEnd`) for close, promotion, open-next and the checklist; **Grading** gains `GradeChangeRequest` and one writer method (`IGradebookWriter.ApplyApprovedChangeAsync`); **Students** gains `StudentExit`, `WithdrawalReason` and the lifecycle service on top of `PersonStatusService`; **Identity** gains the `Saml` and `Ldap` provider kinds on the existing `ExternalSchemeRegistry`, `ExternalLoginLinker`, `ProviderSecretProtector` and `OutboundGuard`. Long work runs as keyed, resumable Hangfire jobs through `IBackgroundJobs`; the run id is the job handle. Local days (effective dates, the checklist window) come from M5's `IClientCalendar` (`Org.TimeZone`, D64).

**Tech Stack:** unchanged. SAML and LDAP libraries chosen by the Task 10 spike and ADR. A new test project `tests/Lms.PerfTests` (not in the normal suite).

**Spec:** `docs/PRD.md` FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06, FR-SEC-07, FR-SEC-08 part 2, X-08, X-09, X-10, §9; decisions D12, D16/D28, D17, D18, D22, D25, D30/D42, D58, D64, D78–D87. Master plan: `docs/DEV-PLAN.md` §2 M7 items 1–8 and §4.1 push discipline. Proposals: `2026-11-22-m7-backend-proposal.md` (#822), `2026-11-22-m7-frontend-proposal.md` (#808). Previous plan: `2026-11-20-m6-communication.md`.

## Scope

**In scope**
- Close year: preview with warnings, typed-name close, gradebook lock, lock-snapshot job, the closed-year write guard over every year-scoped writer (grades, attendance, assignments, quizzes, forums and messaging, D88), closing report.
- Grade-change approval after close: request, queue, approve or reject, audited, the approved value written as a new grade event.
- Promotion: run, computed preview with overrides, filter-based bulk edits, execute as a per-student job, failure list and re-run, undo inside a window and before any activity, history.
- Lifecycle: withdraw and graduate with an effective date, archive (read-only), re-admission with the code kept, client-managed withdrawal reasons, guardian read of closed years after an exit.
- Open the next year from a copy and/or a template (structure only), as a job with a preview.
- SAML 2.0 (SP-initiated) and LDAP (search-bind over LDAPS or StartTLS) providers, set-up and sign-in.
- Year-end checklist on the Org Admin and Manager home pages from 60 days before the year end.
- Release pass: X-09 seeder, harness and tuning; X-08 security review; X-10 accessibility pass; PRD §9 acceptance E2E in ar and en.

**Out of scope (v1)**
- Reopening a closed year (D79); bulk withdrawal (D84); a grade-based pass/fail promotion rule (D83); IdP-initiated SAML, LDAP simple bind without TLS, group-to-role mapping (D85); AAA contrast (D87); a year-end notification besides the home checklist (L15); a certificate-expiry notification (L14).

## What M7 builds on (re-check on `origin/main` when each task starts)
- **Years:** `AcademicYear` (`Planned`/`Active`/`Closed`, `Close()`), `AcademicYearEndpoints` (a Closed year already answers 409 `year_closed` on edits of the year, terms and calendar), `IAcademicYearResolver`, problem codes `year_overlap` and **`active_year_exists` (already on `main`)**.
- **Write guard:** `PlacementGuard.EnsureWritableAsync` (`class_closed` / `year_closed`, a reviewed bypass reads the year at an ancestor unit).
- **Grading (M5):** `Gradebook.State = Locked`, `LockedAtUtc`, `IGradebookWriter` refusing with 409 `gradebook_locked`, `GradeSnapshot(Kind = Lock)`, M5 L5 "events after the lock are ignored", the M5 Task 12 test-only lock seam. **Attendance (M5):** sessions and marks; closed-year sessions read-only through the year.
- **Enrolment:** `EnrollmentEngine` (placement, auto-enrolment by class-course rules, transfers), `ClassStudent` (`Active`, `Removed`, `TransferredOut`, `ActiveYearKey`), `CourseEnrollment`, `GroupMember`; level transfer `OrgUnit.IsLevelTransfer` + `NextTransferUnitId` (FR-ORG-09).
- **People:** `StudentProfile.Status`, `PersonStatusService` (no active profile = no sign-in, FR-SEC-07), `PersonStatusChange`; guardians `StudentGuardian`, `GuardianVisibility`, `GuardianAccessGuardTests`.
- **Templates:** `ApplyTemplateRequest` with placeholders, `ApplyTemplateResultDto.SkippedLockedSettings`, `BranchMaterializer`.
- **Identity:** `IdentityProvider`, `IdentityProviderKinds` (Oidc, AzureAd), `ExternalSchemeRegistry`, `ExternalLoginLinker`, `ProviderSecretProtector`, `OutboundGuard`.
- **Jobs, audit, notifications:** `IBackgroundJobs` (keyed, `EnqueueAsSystem`, recurring), `IJobKeyLock`, `[Audited]`, `IAuditWriter.WriteSecurityEventAsync`, the M6 notification publisher and outbox.
- **FE:** `AcademicYearsPage`, `IdentityProvidersPage`/`IdentityProviderDialog`, `StudentListPage`/`StudentProfilePage`, `PersonStatusChip`, `AcademicFlagsTab`, the import job polling pattern, `ServerTable`, `UnitPicker`, `Announcer`, `NavCatalog`, the Stitch layer, and the M7 components of #816.
- **Messaging (M6):** `Conversation` (group `ClassCourseId`), `ConversationDto.CanPost` + `ReadOnlyReason`, `ConversationAccess`, the send path of M6 Task 10 (BE3). **Forums (M4a):** thread, post and reply writers under `Features/Forums`.
- **Prerequisites:** M6 wave 1 #724 (defaults version 11) merged before Task 2 (version 12, L1). Tasks 4 and 5 need the M5 grading and attendance PRs (#629 and the gradebook chain) on `main`. H2's locked state (Task 17) needs the M5 FE gradebook and attendance pages on `main`.

## Decisions

### Owner decisions (restated)

| # | Decision | Source |
|---|---|---|
| D78 | Org Admin runs the promotion anywhere; a Manager inside their own units. | PRD D78 |
| D79 | A year must be Closed before promotion runs; no reopen in v1 (late fixes through grade-change approval); unpublished grades warn, never block. | PRD D79 |
| D80 | After close a Manager or the Org Admin approves a grade change, never the requester. | PRD D80 |
| D81 | After withdrawal or graduation the guardian keeps read access to the child's closed years; the withdrawal reason is staff-only. | PRD D81 |
| D82 | Org Admin, Manager and Counselor may withdraw; the reason comes from a client-managed list plus "Other" with a note. | PRD D82 |
| D83 | Promotion suggests the next level from the level mapping only; every row stays editable. | PRD D83 |
| D84 | Open grade-change and excuse requests are warnings, not blocks; a run succeeds per student with a failure list to fix and re-run; undo restores the successful ones; no bulk withdrawal. | PRD D84 |
| D85 | SP-initiated SAML only; LDAP search-bind over LDAPS or StartTLS only; no group-to-role mapping. | PRD D85 |
| D86 | Year-end checklist on the home pages from 60 days before the year end. | PRD D86 |
| D87 | WCAG 2.1 AA; no AAA requirement. | PRD D87 |
| D88 | A closed year's grades, attendance, assignments, quizzes, **forums and messaging** are read-only: no forum posts or replies, no sending in conversations or groups scoped to that year's classes or class courses (answers O1; changes the earlier default). | PRD D88 |
| D89 | Target class suggested by section code (Promote: next level, same section; Repeat: same level and section); every row editable (answers O2). | PRD D89 |
| D90 | Opening the next year does not copy teacher assignments (answers O3). | PRD D90 |
| D91 | Only the Org Admin and the Manager re-admit; the Counselor withdraws but does not re-admit (answers O4). | PRD D91 |

### Lead decisions

| # | Decision | Settles |
|---|---|---|
| L1 | **Contract waves and defaults version 12.** Wave 1 = **#812** (seven permissions in `IntroducedInVersion12`, routes, DTOs, codes, events, audit entities; `CurrentVersion` untouched). Wave 2 = `lead/m7-contracts-2` (BE1 drafts, the Lead owns): adds the eighth permission `Students.ManageWithdrawalReasons` to `IntroducedInVersion12` **and sets `BuiltInRoleDefaults.CurrentVersion = 12`**, so all M7 permissions share one version. Wave 2 merges only after M6 #724 (version 11) is on `main`; wave 1 may merge earlier. Tests `Defaults_version_is_12` and `Reconciler_11_to_12_adds_exactly_the_eight_m7_permissions`. No later M7 wave adds a permission. | #812 body, #822 §5 |
| L2 | **Contract names win over #808 §2 and §4.1.** `AcademicYears.Close` (not `Years.Close`), `AcademicYears.Create` for open-next (not `Years.Open`), `Students.Withdraw` / `Students.Readmit` / `Students.ViewArchive` (not `Students.Lifecycle` / `Students.Archive.View`), `Grades.ChangeRequest` / `Grades.ChangeApprove`, `Promotion.Run`, `IdentityProviders.Manage`. Routes are `Routes.YearEnd.cs`: a grade change is created at `…/class-courses/{id}/gradebook/change-requests` with `GradeItemId`, `StudentId` and the grade `Version` (not per gradebook-entry id). | #808 §2, §4.1 vs #812 |
| L3 | **Close is a status flip plus a bounded lock** (#822 L1): one transaction sets the year `Closed` and locks every gradebook of the year with one `UPDATE` per client; the `GradeSnapshot(Lock)` rows are filled by the keyed job `year-lock:{yearId}`; until a gradebook's snapshot exists, reads use the live calculation cut at `LockedAtUtc` (the M5 L5 rule). Accepted by the Lead as the M5 owner's seam (#822 Q1); tested before and after the job. Wave 2 adds `YearClosedDto.LocksFinalising` and the same flag on the closing report so the UI can say "locks are being finalised". | #822 Q1, §8.4 |
| L4 | **Close blocks only on "year not Active".** Unpublished items, open terms, open assignments, pending grade-change and excuse requests are **warnings** (D79). #808 H2's "block when a term has unpublished grades if the client requires it" is dropped. `Blocking` stays in the DTO for later rules; 409 `year_close_blocked` carries it in `extra`. | #808 H2 vs D79 |
| L5 | **What the close locks** (D88, replacing #822 Q3's "forums stay open"): grades, attendance marks, assignment submissions and grading, quiz attempts and grading, enrolment exceptions and imports into the closed year's classes, **forum threads, posts, replies and edits** in the year's class courses, and **messaging**: sending, adding members and attaching files in a conversation or group whose `ClassCourseId` (or class) belongs to the closed year all answer 409 `year_closed` (or `gradebook_locked`). Such a conversation reads `CanPost = false` with **`ReadOnlyReason = year_closed`** (a new value beside the M6 reasons, added in wave 2). Direct conversations not scoped to a class or class course are not year-scoped and stay open. The forum part is in Task 4 (BE1); the messaging part is **Task 4a (BE3)**, after M6 Task 10. An architecture test lists every year-scoped write endpoint (forum and messaging writes included) and fails on one without the guard. Reads are untouched. | #822 L2, Q3; D88 |
| L6 | **Grade change** (#822 L3–L5): one open request per (grade item, student) (`OpenKey`), only while the gradebook is locked; reason 20–500 characters; approval re-checks the `Version` (409 `concurrency_conflict`); the approved value is a new grade event of kind `ApprovedChange` that refreshes the student's lock snapshot in the same transaction; nothing is edited in place. **The decider is never the requester**, checked against the stored `RequestedByUserId` in the handler (403 `grade_change_not_allowed`); the FE disables the button too. Reject needs a note; a request is decided once. | #822 L3–L5, D80 |
| L7 | **Promotion data shape** (#822 L6–L7): the preview is computed (paged query over source placements, the level mapping and overrides); a draft stores only the run and the user's overrides (`PromotionDecision`); execute freezes one `PromotionItem` per student; each item is processed in its own transaction, idempotently, and a crashed job resumes. Edits are ≤ `PromotionLimits.DecisionsPerCall` (200) rows per `PUT …/decisions` or **by filter** (`PUT …/decisions/filter`); no endpoint takes a list of ids for bulk work. **The run id is the job handle**: no `JobId` on `PromotionRunDto` (#808 asked for one); the FE puts the run id in the URL (`/promotion/runs/{id}`) and polls `GET …/{id}`; the same holds for the open-next run (`…/open-next/{runId}`). | #808 §4.1 `JobId`, #822 §8.6 |
| L8 | **Undo** (#822 L8, D12): allowed while `ServerNowUtc < UndoUntilUtc` (setting `Years.PromotionUndoWindowMinutes`, default 10, range 1–60, client root; wave 2) **and** no activity exists in the target year for the run's students. Activity = any attendance mark, grade event or entry, submission, quiz attempt, forum post, enrolment exception or manual placement in the target classes after `ExecutedAtUtc`, actions of the run itself excluded (#822 Q2 default), each reported by an `IYearActivityProbe`; an architecture test enumerates the probes so a new module cannot forget one. Two codes: `promotion_undo_window_over`, `promotion_undo_activity_exists`. Undo restores only `Done` items. The UI shows the clock time from server time, never the browser clock. | #822 L8, Q2; #808 H4 |
| L9 | **Target class suggestion** (D89, answering #822 Q4/Q9): for Promote, the class of the next-level unit with the same section code when exactly one exists; for Repeat, the same level and section code in the target year; otherwise empty, and the row shows the blocking `TargetClassMissing` until the user picks a class. A top level with no `NextTransferUnitId` suggests Graduate (D83). Over capacity is a warning. | #822 Q4, Q9; #808 H3 |
| L10 | **One exit path.** `StudentExit` (Withdrawn, Graduated, TransferredOut) is written by both the lifecycle routes and the promotion's Graduate and TransferOut outcomes. Effective date: today or earlier acts at once; a later date (at most one year ahead, 422 `effective_date_invalid` otherwise) is applied by the daily system sweep `student-exit-effective` at the client's local midnight (`IClientCalendar`). Acting = `PersonStatusService` deactivation (sign-in blocked from that date), enrolments dropped with reason `Exit`, group memberships ended, history kept. A student with a pending future exit is skipped by the promotion with a warning (#822 Q11 default). No bulk withdrawal (D84). | #822 L10, Q11; #808 H5 |
| L11 | **Privacy after an exit.** The reason and note are only in staff DTOs (served under `Students.Withdraw` / `Students.ViewArchive`), with an architecture test on DTO property names. Guardians keep read access to the child's **closed years** only (published grades, attendance summary), including guardians whose children have all left (D81 answers #822 Q10); the guardian's own account is not deactivated. **The archive searches by name or student code only, never by national id** (#808 H5 asked for national id; the contract DTO never carries it and a search by it would be an enumeration path). The blocked sign-in page uses the same words for withdrawn, graduated and expired accounts. | D81, #808 H5 vs #812 |
| L12 | **Withdrawal reasons are written under their own permission** `Students.ManageWithdrawalReasons` (Org Admin, client root, D18; wave 2), read under `Students.Withdraw`. "Other" is built in and needs a note; deactivating a reason hides it from new withdrawals and never rewrites history. | QA #812 note 3, #822 Q5 |
| L13 | **Open next year** (#822 L13): a job, idempotent per (source year, new name), creates a Planned year with terms shifted by the year offset and copies structure only (classes, class courses, blueprint references, timetable periods) for the chosen units; a template is applied through `ApplyTemplate` with `{Year}` defaulted to the new name; **students are never copied; teachers are not copied** (D90). Activating the new year is the existing button; `active_year_exists` (already on `main`) is reused, so no new code is added for it. | #822 L13, Q6; #808 H6 |
| L14 | **SAML and LDAP** (D85, #822 L14–L15): a spike and an ADR (Task 10) choose the libraries before Tasks 11 and 12. Secrets write-only through `ProviderSecretProtector`; `OutboundGuard` on every host and URL; LDAPS or StartTLS only (no plain option in the API or UI), certificate validation on, escaped filters, the typed password lives in one request body and one call frame. Users are matched by the existing linker (email or national id); unmatched users get the generic answer. The IdP signing certificate's expiry is a field of the provider DTO and the list shows **Needs attention** 30 days before it; **there is no certificate-expiry notification event in v1** (#808 H8 listed one; it is in neither the PRD nor #812). CI uses an in-memory fake IdP and directory only (#822 Q12). | #808 H7/H8, #822 §4.6 |
| L15 | **Year-end checklist is a read model** (D86, #822 L17): `GET /api/me/year-end-checklist` (wave 2) returns, for the years the caller manages and only from `Years.EndChecklistDays` (default 60) before the year end, **five** steps with a state each: publish grades, decide pending grade changes, close the year, run the promotion, open the next year (#808 H8 had four; the approval step is added). Nothing is stored and nothing blocks. No extra notification (D86 names the home pages; #822 Q8). | #808 H8 vs #822 §6 |
| L16 | **Notifications are the four events of #812**: `Grades.ChangeRequested`, `Grades.ChangeDecided` (Approved or Rejected), `Promotion.Finished` (one event whose `State` is Done, PartiallyFailed, Failed or Undone; #808 H8's separate "failed" and "undone" events are this one), `Years.NextOpened`. Titles and names only, never a grade value, a reason or a note. | #808 H8 vs #812 |
| L17 | **X-09 ownership and cadence.** BE2 owns the 50,000-student seeder and the perf harness (Tasks 14 and 15; #822 had BE4 for the seeder and BE1 + BE3 for the pass); index and query fixes are made by the lane that owns the module, filed from Task 15's results. The 50,000 run is a nightly and on-demand workflow, never per PR (the CI queue is the bottleneck); per PR, the normal integration suite carries the query-count tests (no N+1) and a "no unpaged list" test. Budget: p95 < 2 s per list page (X-09); promotion execution and preview targets are set from the first measurement and recorded in `docs/perf/`. | #822 Task 12/13, Q7 |
| L18 | **BE lane rebalance.** Because BE2 takes X-09, the LDAP provider (#822 Task 11, BE2) moves to **BE4**, who no longer has the seeder. All other BE lanes are as #822 proposes: BE1 close, spike, SAML; BE2 grade change, open next, checklist, perf; BE3 promotion end to end; BE4 lifecycle, LDAP. | lane assignment |
| L19 | **#808 H9 is folded, not a task.** Each FE task adds its own `data-testid` hooks, ar/en parity, RTL and 360 px screenshots and the accessibility scan tests for its pages (the M6 `Problems()` helper); `docs/m7-e2e-testids.md` is created in H1 (Task 16) and agreed with QA1; the X-10 pass over every page added since M5 is QA1's Task 26, whose findings are filed against FE1/FE2; the designer's sign-off is part of Task 3. | #808 H9 vs the QA assignment |
| L20 | **D16/D28 escalation guard stays as is.** Wave 2 adds a comment at the M7 block of `BuiltInRoles.cs` and `BuiltInRoleDefaults.cs`: the M7 permissions flow only to Org Admin and Manager (Counselor: Withdraw and ViewArchive; Supervisor: ViewArchive; Teacher: ChangeRequest) and `AcademicYears.Close` is deliberately absent from the Manager, because the guard would otherwise let a Manager assign a role that can close a year. Test `Manager_cannot_assign_a_role_holding_AcademicYears_Close`. | QA #812 note 1, #822 §5 |

## QA notes on #812 folded in

| QA note | Where |
|---|---|
| 1. D16 escalation-guard comment | L20; Task 2 (comment + test) |
| 2. A requester never approves their own change | L6; Task 5 tests `A_requester_never_approves_their_own_change` as a Manager, as an Org Admin who teaches the class course, and as a user holding both roles; Task 24 probe rows; FE Task 17 `Requester_cannot_approve_own_request` |
| 3. Withdrawal-reasons permission pinned | L12; Task 2 adds `Students.ManageWithdrawalReasons`; Task 8 tests `Writing_reasons_needs_ManageWithdrawalReasons` |
| 4. Manager sibling-unit isolation for promotion (and every Manager-scoped route) | Tasks 5, 6, 7, 8 each test `A_manager_of_a_sibling_unit_gets_404`; Task 24 adds the **Manager-of-sibling-unit** caller to every M7 row |

## Global Constraints
- **Every M0–M6 Global Constraint still applies** (`X-Lms-Context`, Guid v7, utf8mb4, default culture `ar`, explicit permissions per built-in role, `UnitAccess.CheckUnitAsync` / `RelationshipAccess.CheckClassCourseAsync` with 404 on a miss, ProblemDetails `code` from `ProblemCodes`, `// ISOLATION-BYPASS: <reason>` + allow-list entry, no new `DbSet` properties, one migration per PR with a working `Down()`, `[Audited]` + an isolation test per business entity, probe rows per route in the PR that adds it, FE API interface + fake per module, ar/en parity, RTL screenshots, `ServerNowUtc` in every timed response, local days only through `IClientCalendar`).
- **Isolation shapes:** tenant-owned: `WithdrawalReason`; unit-scoped (`IOrgUnitScoped`): `GradeChangeRequest` (class course), `PromotionRun`, `PromotionDecision`, `PromotionItem` (source unit), `StudentExit` (the student's home unit), `YearOpenRun` (defining unit).
- **Scale (X-09):** every M7 list is server-paged, filterable and sortable; bulk actions are by filter; jobs never load all items into memory; a query that is not index-backed does not ship.
- **Privacy:** no grade value, reason, note, national id or password in notifications, audit summaries, logs or job state; audit events carry ids and counts only; LDAP passwords and SAML assertions are never logged.
- **Migrations:** `AddGradeChangeRequests` (Task 5), `AddPromotion` (Task 6, incl. the data-only `ClassStudent.Completed` status), `AddStudentExits` (Task 8), `AddYearOpenRuns` (Task 9), `AddSamlLdapProviderSettings` only if Task 11 or 12 needs a column. Close (Task 4) adds no table. Whoever merges second regenerates on top of `main`; never duplicate another PR's migration.
- **FE:** `IYearCloseApi`, `IGradeChangesApi`, `IPromotionApi`, `ILifecycleApi`, `INewYearApi`, the provider API extension, each with HTTP implementation + fake (`Lms:UseFakeApi=true`) mimicking the server rules the UI depends on (#808 §4.2); the shared `JobProgressPanel`, `ConfirmEffectDialog`, `YearChip`; key prefixes `YearClose_*`, `GradeChange_*`, `Promo_*`, `Lifecycle_*`, `Archive_*`, `NewYear_*`, `Idp_*`, `Login_*`, `YearEnd_*`, `Error_<code>`; files under ~300 lines; the lms-ui skill and the #814 spec.
- **Push discipline (DEV-PLAN §4.1):** test locally (unit, integration with Testcontainers, bUnit, the relevant E2E under the `e2e.lock`) before pushing; one push per finished fix; no refresh-only main merges; drafts stay drafts until locally green.

## Isolation bypass and system-context sites
Each gets an allow-list entry with an isolation statement and a cross-client test.

| # | Site | Reads | Task |
|---|---|---|---|
| 1 | `PlacementGuard` / year write guard (new callers) | the year at an ancestor unit, status only | 4 |
| 2 | `YearLockJob` (`year-lock:{yearId}`, system) | gradebooks of one year by `OrganizationId` + year id | 4 |
| 3 | `PromotionYearReader` | target year above the Manager's scope: ids, names, status | 6 |
| 4 | `LevelMappingReader` | next-level unit and its classes above the scope: ids, names, section codes | 6 |
| 5 | `YearActivityProbes` | module tables by the run's target class ids, counts only | 7 |
| 6 | `GuardianVisibility` closed-year read for exited children | guardian-linked, closed years, published values only | 8 |
| 7 | `StudentExitSweep` (system, per client) | due exits by id | 8 |
| 8 | `YearOpenJob` (captured context) | the defining unit's subtree, client-matched | 9 |

Jobs that run under a captured context (`promotion-run`, `promotion-undo`, `year-open`) carry `OrganizationId` explicitly in every query (the M6 lesson), tested with a run of client A while client B has data.

## Route, permission and relationship table (Task 24 tests every row)

| Area | Routes (`Routes.YearEnd.cs`) | Permission | Who (defaults) | Scope |
|---|---|---|---|---|
| Close | `GET …/close-preview`, `POST …/close` | `AcademicYears.Close` | Org Admin (D17) | year's defining unit |
| Closing report | `GET …/closing-report` | `AcademicYears.View` | as today | defining unit |
| Open next | `POST …/open-next/preview`, `POST …/open-next`, `GET …/open-next/{runId}` | `AcademicYears.Create` | Org Admin | defining unit |
| Grade change: create | `POST /api/class-courses/{id}/gradebook/change-requests` | `Grades.ChangeRequest` | Teacher (own ClassCourse), Manager, Org Admin | relationship; miss = 404 |
| Grade change: queue, decide | `GET /api/grade-change-requests`, `GET …/{id}`, `POST …/{id}/approve`, `POST …/{id}/reject` | `Grades.ChangeApprove` (a requester also reads their own) | Manager, Org Admin | unit subtree; never the requester (L6) |
| Promotion | `POST/GET /api/promotion/runs`, `GET …/{id}`, `…/rows`, `PUT …/decisions`, `PUT …/decisions/filter`, `POST …/execute`, `POST …/undo`, `GET …/failures` | `Promotion.Run` | Org Admin; Manager in own units (D78) | source unit; rows clipped by scope |
| Withdraw, graduate | `POST /api/students/{id}/withdraw`, `…/graduate` | `Students.Withdraw` | Org Admin, Manager, Counselor (D82) | student's home unit |
| Re-admit | `POST /api/students/{id}/readmit` | `Students.Readmit` | Org Admin, Manager (D91) | home unit and the target class's unit |
| Archive | `GET /api/students/archive` | `Students.ViewArchive` | Org Admin, Manager, Counselor, Supervisor | unit scope |
| Withdrawal reasons | `GET /api/withdrawal-reasons` / `PUT` | read `Students.Withdraw`; write `Students.ManageWithdrawalReasons` | write: Org Admin | client root (D18) |
| Identity providers | existing + `POST …/{id}/test`, SAML metadata import, SP metadata, ACS, LDAP sign-in (wave 2 names) | `IdentityProviders.Manage`; sign-in anonymous | client-root admin | client root |
| Closed-year guard (D88) | existing forum write routes; M6 send, add-member and attachment-upload routes of class-scoped conversations | unchanged | unchanged | 409 `year_closed` for a closed year's class courses (Tasks 4, 4a) |
| Checklist | `GET /api/me/year-end-checklist` (wave 2) | any of `AcademicYears.Close`, `Promotion.Run`, `Grades.ChangeApprove` | Org Admin, Manager | caller's units |

Rows cover RootAdmin, School1Admin, School2Admin, School1Teacher, School1Manager, **Manager of a sibling unit**, Counselor, Supervisor, Guardian, Student, ClientBAdmin, PlatformAdmin and Anonymous.

## Review Focus
1. **Closed means closed:** no write into a closed year's grades, attendance, grade-producing work, forums or class-scoped messaging except `ApplyApprovedChangeAsync` (Tasks 4, 4a, 5, 24, 25).
2. **Separation of duties:** the requester never decides; the D16 guard is unchanged (Tasks 2, 5, 24).
3. **Manager scope:** a Manager's promotion, archive, withdrawal and approval never reach a sibling unit, in the request or in the job (Tasks 5–8, 24).
4. **Undo safety:** undo restores only what the run did and never after activity (Task 7).
5. **Leaver privacy:** reason staff-only; guardians read closed years only; same sign-in words for every inactive kind (Tasks 8, 20, 25).
6. **External sign-in:** signature, wrapping, replay, audience, skew for SAML; TLS-only, escaped filters, no password in logs for LDAP; secrets never returned (Tasks 10–12, 22, 25).
7. **Scale:** paged lists, filter bulk, bounded jobs, p95 < 2 s (Tasks 6, 7, 8, 14, 15).

---

### Task 1: Contract — wave 1 (#812): year close, grade change, promotion, lifecycle, archive, open next — lane: Lead contract

**PRD:** FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06, D78–D84. **PR:** **#812** (`lead/m7-contracts-1`, QA-approved at `91c599ed`). **Depends on:** nothing; merges before Tasks 4–9, 16–21 and 24.

**Files (as in #812):** Modify `src/Lms.Shared/Authorization/Permissions.cs`, `BuiltInRoles.cs`, `src/Lms.Infrastructure/Access/BuiltInRoleDefaults.cs` (`IntroducedInVersion12`, `CurrentVersion` untouched), `src/Lms.Shared/ProblemCodes.cs` (14 codes), `Auditing/AuditCatalog.cs` (`GradeChangeRequest`, `PromotionRun`, `StudentExit`), `Notifications/NotificationEvents.cs` (four events, L16), resx; Create `src/Lms.Shared/Routes.YearEnd.cs`, `src/Lms.Shared/YearEnd/YearEndDtos.cs`; tests `tests/Lms.UnitTests/Contracts/YearEndContractTests.cs` and the updated matrix tests.

- [ ] **Step 1:** QA approval stands; CI green on the merge base. **Step 2: Commit** (squash) `feat(contracts): M7 wave 1 - year close, grade-change approval, promotion, withdrawal, archive, open next year`.

**Acceptance:** contract tests green; merged without changing `CurrentVersion` (L1).

### Task 2: Contract — wave 2: SSO, settings, reasons permission, checklist, version 12 — lane: Lead contract (drafted by BE1)

**PRD:** FR-SEC-08 part 2, FR-STU-07/08, D12, D18, D85, D86. **PR:** `lead/m7-contracts-2`. **Depends on:** Task 1; M6 #724 (version 11) on `main` (L1). Merges before Tasks 8, 10–13, 22, 23.

**Files:** Modify `Permissions.cs` (`Students.ManageWithdrawalReasons`), `BuiltInRoles.cs` (Org Admin; the L20 comment), `BuiltInRoleDefaults.cs` (**`CurrentVersion = 12`**, the eighth permission in `IntroducedInVersion12`, the L20 comment), `Configuration/SettingKeys.cs` + `SettingCatalog.cs` (`Years.PromotionUndoWindowMinutes` 10, 1–60; `Years.EndChecklistDays` 60; both client root), `IdentityProviderKinds` (`Saml`, `Ldap`), `YearEndDtos.cs` (`YearClosedDto.LocksFinalising`, closing report flag, L3), `ProblemCodes.cs` (generic external sign-in failure if the existing code does not fit), the messaging `ReadOnlyReason` value `year_closed` (L5, D88; on top of M6 #724), resx; Create `src/Lms.Shared/Identity/SamlLdapDtos.cs` (SAML and LDAP settings with write-only secrets, `ProviderTestResultDto` with named checks, `SpMetadataDto`, IdP metadata import request, `LdapSignInRequest`, certificate expiry field, L14), `src/Lms.Shared/YearEnd/YearEndChecklistDto.cs` (five steps, L15), route constants for the test, metadata, ACS, LDAP sign-in and checklist; tests `YearEndContractTests` (extended), `IdentityProviderContractTests`, `BuiltInRoleDefaultsTests`, `tests/Lms.IntegrationTests/Access/BuiltInRoleReconcilerTests.cs`.

- [ ] **Step 1: Write the failing tests** `Defaults_version_is_12`, `Reconciler_11_to_12_adds_exactly_the_eight_m7_permissions`, `ManageWithdrawalReasons_is_org_admin_only`, `Manager_cannot_assign_a_role_holding_AcademicYears_Close` (D16), `Provider_dtos_never_return_a_secret` (reflection), `Ldap_settings_have_no_plain_transport`, `Checklist_has_five_steps`, `Read_only_reasons_include_year_closed`, `Undo_window_setting_is_bounded_1_to_60`, `Every_wave_2_code_setting_and_permission_has_ar_and_en_labels`. **Steps 2–4** FAIL → implement → PASS; 0 warnings. **Step 5: Commit** `feat(contracts): M7 wave 2 - SAML/LDAP, year-end settings, withdrawal-reasons permission, checklist, defaults 12`.

**Acceptance:** contract tests green; merged after #724; QA confirms the role matrix against D78–D82 and L12.

### Task 3: M7 design landing — spec #814, components #816, design sign-off — lane: UX (UI/UX Designer)

**PRD:** X-10, D87. **PRs:** **#814** (`ux/m7-design`, spec `docs/design/m7-year-end.md`), **#816** (`ux/m7-components`, on #815). **Depends on:** Task 16 for #816.

**Scope:** close dialog and locked notice, grade-change dialog and queue, the three-step promotion wizard, preview table and its 360 px card, the undo banner (clock time + countdown), the result card, the lifecycle dialogs and archive, the new-year wizard, the SAML/LDAP field groups and the LDAP sign-in form, the checklist card. #816 merges after #815 and before Tasks 17–23 build pages on it (gate, as M6 Task 19). The designer reviews every FE PR of Tasks 17–23 and signs off the M7 pages at the end of Task 26.

**Acceptance:** #814 and #816 QA-approved and merged; sign-off comment on Task 26.

### Task 4: Close year, locks and the closed-year write guard — lane: BE1 · size L

**PRD:** FR-YEAR-04, D17, D79, D88, X-09. **Depends on:** Task 1; the M5 grading and attendance PRs on `main`.

**Files:** Create `src/Lms.Infrastructure/YearEnd/Close/YearCloseService.cs`, `YearClosePreviewQuery.cs`, `ClosingReportQuery.cs`, `YearLockJob.cs` (site 2), `IYearWriteGuard.cs` (extends `PlacementGuard`, site 1); `src/Lms.Api/Features/YearEnd/YearCloseEndpoints.cs`; Modify the attendance, assignment, quiz, enrolment-exception, import and **forum** (thread, post, reply, edit) writers to call the guard; test `tests/Lms.IntegrationTests/YearEnd/YearCloseTests.cs`, `ClosedYearWriteGuardTests.cs`, `YearLockJobTests.cs`, `tests/Lms.UnitTests/Architecture/YearScopedWritesAreGuardedTests.cs`, `ProbeRows/M7Close.cs`.

**Interfaces:** L3, L4, L5. `POST close` needs `ConfirmName` equal to the year name (422 otherwise); only an Active year closes; one transaction; audit security event with counts only; preview counts are one grouped, index-backed query per client.

- [ ] **Step 1: Write the failing tests** `Close_needs_the_typed_year_name`; `Only_an_active_year_closes`; `Warnings_never_block_close` (unpublished items, open terms, pending requests); `Close_locks_every_gradebook_of_the_year_in_one_transaction`; `Close_is_correct_before_and_after_the_snapshot_job`; `Lock_job_is_idempotent_and_resumes`; `Attendance_mark_in_a_closed_year_is_409_year_closed`; the same for assignment submit and grade, quiz attempt and grading, enrolment exception, import; `Forum_post_in_a_closed_year_is_409_year_closed`, `Forum_reply_in_a_closed_year_is_409_year_closed`, `Forum_edit_in_a_closed_year_is_409_year_closed` (D88); `Forums_of_a_closed_year_stay_readable`; `Year_scoped_write_endpoints_all_call_the_guard` (architecture); `Org_admin_of_a_sibling_unit_cannot_close` (404); `Manager_cannot_close` (403); `Closing_report_counts_per_class`; `Close_job_never_reads_another_client`. **Steps 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(year-end): close year, gradebook lock and the closed-year write guard`.

**Acceptance:** tests green; probe rows; QA3 review of the guard sweep.

### Task 4a: Closed-year guard for messaging — lane: BE3 · size S

**PRD:** FR-YEAR-04, FR-MSG-01, D88. **Depends on:** Task 4 (`IYearWriteGuard`), Task 2 (`ReadOnlyReason` `year_closed`), **M6 Task 10** (messaging kernel, BE3) on `main`; BE4's M6 policy code (Task 11) is only read, not changed.

**Files:** Modify the M6 send, add-member and attachment-upload handlers under `src/Lms.Api/Features/Messaging/` and the `CanPost` / `ReadOnlyReason` computation in `src/Lms.Infrastructure/Messaging/` to ask `IYearWriteGuard` for a conversation or group whose `ClassCourseId` or class belongs to a closed year; test `tests/Lms.IntegrationTests/Messaging/ClosedYearMessagingTests.cs`, `ProbeRows/M7Messaging.cs`.

- [ ] **Step 1: Write the failing tests** `Send_in_a_closed_year_class_course_group_is_409_year_closed`; `Adding_a_member_or_attachment_there_is_409_year_closed`; `Closed_year_group_reads_can_post_false_with_reason_year_closed`; `Closed_year_group_stays_readable_by_its_participants`; `Direct_conversation_without_a_class_scope_stays_open`; `Group_of_the_next_year_is_unaffected`; `Guard_never_reads_another_client`. **Steps 2–4** → **Step 5: Commit** `feat(messaging): closed-year groups are read-only (D88)`.

**Acceptance:** tests green; the M6 messaging suite still green; QA2 review; the FE thread shows the `year_closed` reason through the M6 read-only banner (resource pair `Msg_ReadOnly_year_closed`, added in Task 17).

### Task 5: Grade-change approval — lane: BE2 · size M

**PRD:** FR-GRD-06, D80, D58. **Depends on:** Task 4 (lock), Task 1.

**Files:** Create `src/Lms.Domain/Grading/GradeChangeRequest.cs`, `src/Lms.Infrastructure/Grading/GradeChanges/GradeChangeService.cs`, `GradeChangeQueueQuery.cs`, configuration, migration `AddGradeChangeRequests`; Modify `IGradebookWriter` (+ `ApplyApprovedChangeAsync`, the only method that writes into a locked gradebook); `src/Lms.Api/Features/Grading/GradeChangeEndpoints.cs`; test `tests/Lms.IntegrationTests/Grading/GradeChangeTests.cs`, `GradeChangeIsolationTests.cs`, `ProbeRows/M7GradeChanges.cs`.

**Interfaces:** L6; notifications `Grades.ChangeRequested` (approvers, ancestors-and-self) and `Grades.ChangeDecided` (requester), names only.

- [ ] **Step 1: Write the failing tests** `Request_needs_a_locked_gradebook`; `Reason_must_be_20_to_500_characters`; `One_open_request_per_grade_is_409_grade_change_pending`; `Teacher_of_another_class_course_gets_404`; `A_requester_never_approves_their_own_change` (theory: Manager requester, Org Admin who teaches the class course, user holding both roles; 403 `grade_change_not_allowed`); `A_manager_of_a_sibling_unit_cannot_see_or_decide_the_request` (404); `Client_b_admin_cannot_see_the_request`; `Approval_after_the_grade_moved_is_409_concurrency_conflict`; `Approved_change_is_a_new_event_and_refreshes_the_lock_snapshot`; `A_closed_grade_is_never_edited_in_place`; `Reject_needs_a_note`; `A_request_is_decided_once`; `Every_decision_writes_an_audit_row_without_values`; `Notifications_carry_no_grade_value`; table isolation test. **Steps 2–4** → **Step 5: Commit** `feat(grading): grade-change requests and approval after close`.

**Acceptance:** tests green; probe rows; QA3 review.

### Task 6: Promotion kernel — run, computed preview, overrides, counts — lane: BE3 · size L

**PRD:** FR-STU-07, FR-ORG-09, D78, D79, D83, D84, X-09. **Depends on:** Task 1.

**Files:** Create `src/Lms.Domain/YearEnd/PromotionRun.cs`, `PromotionDecision.cs`, `PromotionItem.cs`; `src/Lms.Infrastructure/YearEnd/Promotion/PromotionRunService.cs`, `PromotionPreviewQuery.cs`, `TargetClassSuggester.cs` (L9), `PromotionYearReader.cs` (site 3), `LevelMappingReader.cs` (site 4), configurations, migration `AddPromotion`; `src/Lms.Api/Features/YearEnd/PromotionEndpoints.cs` (create, list, get, rows, decisions, decisions/filter); test `tests/Lms.IntegrationTests/YearEnd/PromotionPreviewTests.cs`, `PromotionIsolationTests.cs`, `ProbeRows/M7Promotion.cs`.

**Interfaces:** L7, L9; source year must be Closed (409 `year_not_closed`); target Planned or Active; one open run per source unit (409 `promotion_in_progress`); blocking codes `NoLevelMapping`, `TargetClassMissing`, `AlreadyPlacedInTargetYear`, `TargetYearClosed`, `SourceYearNotClosed`; warnings for open grade-change and excuse requests, over capacity, pending future exit (L10).

- [ ] **Step 1: Write the failing tests** `Run_needs_a_closed_source_year`; `One_open_run_per_source_unit`; `Suggestion_comes_from_the_level_mapping_only` (D83); `Top_level_suggests_graduate`; `Target_class_is_suggested_by_unique_section_code_else_blocking`; `Overrides_are_the_only_stored_rows` (a draft of N students stores one run row + overrides); `Rows_are_server_paged_filtered_and_sorted`; `Decisions_put_refuses_more_than_200_rows`; `Filter_bulk_sets_the_outcome_for_the_matching_rows_only`; `Counts_match_the_rows_query`; `Warnings_never_block` (D84); `A_manager_of_a_sibling_unit_gets_404_on_every_promotion_route`; `Manager_preview_never_lists_another_units_students`; `Readers_return_ids_names_and_flags_only`; isolation tests for the three tables; `Preview_page_of_50000_is_index_backed` (query plan, Task 15 measures). **Steps 2–4** → **Step 5: Commit** `feat(year-end): promotion run, computed preview and overrides`.

**Acceptance:** tests green; probe rows (Manager of a sibling unit); QA2 review.

### Task 7: Promotion execute, failures, re-run and undo — lane: BE3 · size L

**PRD:** FR-STU-07, D12, D84. **Depends on:** Tasks 6, 4; Task 8's `StudentExit` service for Graduate and TransferOut (BE3 calls the interface; until Task 8 merges a test double stands in).

**Files:** Create `src/Lms.Infrastructure/YearEnd/Promotion/PromotionFreezer.cs`, `PromotionRunJob.cs` (`promotion-run:{runId}`, captured context), `PromotionUndoJob.cs`, `IYearActivityProbe.cs` + one probe per module (site 5); Modify `ClassStudent` (`Completed` status and `EndReason` values, data-only in `AddPromotion` if Task 6 has not merged, else a small follow-up migration); endpoints execute, undo, failures; test `tests/Lms.IntegrationTests/YearEnd/PromotionExecuteTests.cs`, `PromotionUndoTests.cs`, `tests/Lms.UnitTests/Architecture/EveryModuleHasAYearActivityProbeTests.cs`.

**Interfaces:** L7, L8, L10; `ConfirmStudentCount` must equal the preview total (422); execute 202; slices of 200 students with progress per slice; `Promotion.Finished` with the state.

- [ ] **Step 1: Write the failing tests** `Execute_needs_the_confirmed_student_count`; `Blocking_rows_refuse_execute_409_promotion_conflict`; `Each_student_is_processed_in_its_own_transaction`; `Killed_job_resumes_with_pending_items_only`; `Partial_failure_completes_the_rest_and_lists_failures` (D84); `Re_execute_processes_only_failed_items`; `Old_placements_are_completed_not_deleted`; `Graduate_and_transfer_out_write_student_exits`; `Job_runs_in_the_callers_scope_and_client`; `Undo_restores_only_done_items`; `Undo_after_the_window_is_409_promotion_undo_window_over` (server clock); `Undo_after_activity_is_409_promotion_undo_activity_exists` (theory over every probe); `Every_module_with_year_writes_has_an_activity_probe` (architecture); `Run_dto_carries_undo_until_and_server_now`; `Promotion_finished_names_no_student`; audit rows with counts. **Steps 2–4** → **Step 5: Commit** `feat(year-end): promotion execution job, failures, re-run and undo`.

**Acceptance:** tests green; a 50,000-student execution measured in Task 15; QA2 + QA3 review.

### Task 8: Lifecycle — withdraw, graduate, archive, re-admit, reasons, guardians — lane: BE4 · size L

**PRD:** FR-STU-08, FR-SEC-07, D25, D81, D82, D84. **Depends on:** Tasks 1, 2.

**Files:** Create `src/Lms.Domain/Students/StudentExit.cs`, `WithdrawalReason.cs`; `src/Lms.Infrastructure/Students/Lifecycle/StudentExitService.cs` (shared with Task 7), `StudentExitSweep.cs` (site 7, recurring per client at local midnight), `ArchiveQuery.cs`, `ReadmitService.cs`, configurations, migration `AddStudentExits`; Modify `GuardianVisibility` (site 6), `IsolationBypassGuardTests` / `GuardianAccessGuardTests` entries; `src/Lms.Api/Features/Students/LifecycleEndpoints.cs`, `WithdrawalReasonEndpoints.cs`; a `DevJobDefinition` for the sweep (E2E); test `tests/Lms.IntegrationTests/Students/LifecycleTests.cs`, `ArchiveTests.cs`, `GuardianAfterExitTests.cs`, `tests/Lms.UnitTests/Architecture/WithdrawalReasonIsStaffOnlyTests.cs`, `ProbeRows/M7Lifecycle.cs`.

**Interfaces:** L10, L11, L12; 409 `student_already_inactive`, 422 `effective_date_invalid`, 409 `readmit_class_full`, `readmit_year_closed`; the code is kept on re-admission (D25).

- [ ] **Step 1: Write the failing tests** `Withdraw_today_blocks_sign_in_and_hides_from_active_lists`; `Future_withdrawal_blocks_sign_in_from_the_effective_date_only` (sweep, client time zone); `Effective_date_more_than_a_year_ahead_is_422`; `Other_reason_needs_a_note`; `Counselor_may_withdraw_but_not_readmit` (D91); `History_and_grades_stay_readable_by_staff`; `Readmit_keeps_the_code_and_restores_sign_in`; `Readmit_into_a_full_class_is_409`; `Archive_searches_name_and_code_never_national_id`; `Archive_is_paged_and_index_backed`; `Reason_and_note_appear_in_no_student_or_guardian_dto` (architecture); `Guardian_reads_closed_years_after_exit_never_the_open_year`; `Guardian_account_stays_active_when_all_children_left`; `Writing_reasons_needs_ManageWithdrawalReasons`; `Deactivated_reason_is_hidden_from_new_withdrawals_and_kept_in_history`; `A_manager_of_a_sibling_unit_gets_404_on_withdraw_readmit_and_archive`; `Sweep_never_runs_in_platform_context`; isolation tests for both tables. **Steps 2–4** → **Step 5: Commit** `feat(students): withdrawal, graduation, archive and re-admission`.

**Acceptance:** tests green; probe rows; QA3 privacy review.

### Task 9: Open the next year — lane: BE2 · size M

**PRD:** FR-YEAR-05, FR-ORG-07/08, D22. **Depends on:** Task 1.

**Files:** Create `src/Lms.Domain/YearEnd/YearOpenRun.cs`, `src/Lms.Infrastructure/YearEnd/OpenNext/YearOpenPreviewQuery.cs`, `YearOpenJob.cs` (site 8), configuration, migration `AddYearOpenRuns`; `src/Lms.Api/Features/YearEnd/OpenNextYearEndpoints.cs`; test `tests/Lms.IntegrationTests/YearEnd/OpenNextYearTests.cs`, `ProbeRows/M7OpenNext.cs`.

**Interfaces:** L13; `copy_nothing_selected` (400), `year_overlap`, `active_year_exists` (existing); `Years.NextOpened`.

- [ ] **Step 1: Write the failing tests** `Preview_writes_nothing`; `Counts_match_the_preview`; `Terms_are_shifted_by_the_year_offset_and_stay_inside_the_year`; `No_students_and_no_teachers_are_copied`; `Template_applies_with_the_year_placeholder`; `Skipped_locked_settings_are_reported`; `Re_posting_the_same_request_returns_the_same_run`; `New_year_is_planned`; `Overlapping_dates_are_409_year_overlap`; `Org_admin_of_a_sibling_unit_gets_404`; `Job_never_writes_another_client`. **Steps 2–4** → **Step 5: Commit** `feat(year-end): open the next year from a copy or a template`.

**Acceptance:** tests green; probe rows; QA1 review.

### Task 10: Spike and ADR — SAML and LDAP libraries and the attack test list — lane: BE1 + QA3 · size S

**PRD:** FR-SEC-08, X-08, D85. **Depends on:** Task 2 (draft is enough).

**Files:** Create `docs/adr/00NN-saml-ldap-libraries.md` (candidates per protocol; licence, last release, CVE history, fit with `ExternalSchemeRegistry`; the chosen library; the attack corpus: signature stripping, wrapping, replay, unsolicited response, audience and recipient, skew, XXE; LDAP injection, plain bind refusal, certificate validation).

**Acceptance:** ADR merged with QA3's approval before Tasks 11 and 12 start code.

### Task 11: SAML 2.0 provider (SP-initiated) — lane: BE1 · size M

**PRD:** FR-SEC-08 part 2, D85. **Depends on:** Tasks 2, 10.

**Files:** Create `src/Lms.Infrastructure/Identity/Saml/SamlSchemeProvider.cs`, `SamlRequestStore.cs` (request id + RelayState), `SamlAssertionCache.cs` (one-time ids), `SamlMetadataImporter.cs`, `SpMetadataBuilder.cs`; Modify `ExternalSchemeRegistry`; endpoints test, metadata import, SP metadata, ACS; test host `FakeSamlIdp`; test `tests/Lms.IntegrationTests/Identity/SamlProviderTests.cs`, `SamlAttackTests.cs`.

- [ ] **Step 1: Write the failing tests** `Sp_initiated_login_links_on_first_login_by_email_or_national_id`; `Unsolicited_response_is_refused`; `Replayed_assertion_is_refused`; `Unsigned_or_wrongly_signed_response_or_assertion_is_refused`; the wrapping cases from the ADR corpus; `Wrong_audience_or_recipient_is_refused`; `Skew_outside_the_setting_is_refused`; `Xml_external_entities_are_not_resolved`; `Two_certificates_allow_rotation`; `Unmatched_user_gets_the_generic_answer`; `Metadata_url_goes_through_the_outbound_guard`; `Provider_dto_never_returns_a_secret`; `Only_the_client_root_admin_manages_providers`. **Steps 2–4** → **Step 5: Commit** `feat(identity): SAML 2.0 provider (SP-initiated)`.

**Acceptance:** tests green; QA3 security review.

### Task 12: LDAP provider (search-bind, TLS only) — lane: BE4 · size M (moved from BE2, L18)

**PRD:** FR-SEC-08 part 2, D85. **Depends on:** Tasks 2, 10.

**Files:** Create `src/Lms.Infrastructure/Identity/Ldap/LdapAuthenticator.cs`, `LdapFilterEscaper.cs`, `LdapConnectionFactory.cs` (LDAPS or StartTLS, certificate validation, optional CA bundle), the LDAP sign-in endpoint; test host `FakeLdapDirectory`; test `tests/Lms.IntegrationTests/Identity/LdapProviderTests.cs`, `tests/Lms.UnitTests/Identity/LdapFilterEscaperTests.cs`.

- [ ] **Step 1: Write the failing tests** `Plain_ldap_is_rejected_on_save_and_at_connect`; `Untrusted_certificate_is_refused`; `Search_filter_escapes_user_input` (injection corpus); `Wrong_user_and_wrong_password_give_the_same_answer`; `Password_never_appears_in_logs_urls_or_audit`; `Sign_in_is_rate_limited_per_user_and_ip`; `Search_has_a_timeout_and_a_size_limit`; `First_login_links_by_email_or_national_id`; `Bind_password_is_write_only`; `Test_connection_names_failed_checks_without_secrets` (422 `provider_test_failed`). **Steps 2–4** → **Step 5: Commit** `feat(identity): LDAP provider (search-bind over LDAPS or StartTLS)`.

**Acceptance:** tests green; QA3 security review.

### Task 13: Year-end checklist read model — lane: BE2 · size S

**PRD:** FR-YEAR-04, FR-STU-07, D86. **Depends on:** Tasks 2, 4, 7 (states of close and promotion).

**Files:** Create `src/Lms.Infrastructure/YearEnd/Checklist/YearEndChecklistQuery.cs`, `src/Lms.Api/Features/Me/YearEndChecklistEndpoint.cs`; test `tests/Lms.IntegrationTests/YearEnd/YearEndChecklistTests.cs`.

- [ ] **Step 1: Write the failing tests** `Checklist_is_empty_before_the_window` (`Years.EndChecklistDays`, client calendar); `Five_steps_reflect_the_year_state`; `Manager_sees_only_their_units_years`; `Teacher_gets_403`; `Nothing_is_stored`. **Steps 2–4** → **Step 5: Commit** `feat(year-end): year-end checklist read model`.

**Acceptance:** tests green; probe rows.

### Task 14: X-09 — 50,000-student seeder — lane: BE2 · size M

**PRD:** X-09, PRD §9.5. **Depends on:** Task 8 (exit shapes); grows as Tasks 5–9 merge.

**Files:** Create `src/Lms.Api/Dev/PerfSeed/PerfSeedCommand.cs` (`dotnet run --project src/Lms.Api -- seed-perf --students 50000 --seed 42`), bulk inserters per module; test `tests/Lms.IntegrationTests/Dev/PerfSeedGuardTests.cs`; doc `docs/perf/README.md`.

**Shape:** one client, a few schools, ~1,500 classes, ~10 class courses each, enrolments, guardians, staff, grade entries for a configurable share, a closed year ready for promotion; deterministic from the seed.

- [ ] **Step 1: Write the failing tests** `Seeder_refuses_outside_development` (environment guard); `Same_seed_gives_the_same_data` (small run); `Seeded_client_passes_the_isolation_checks`. **Steps 2–4** → **Step 5: Commit** `feat(perf): deterministic 50,000-student seeder`.

**Acceptance:** seeder documented; a 50,000 run completes on the perf runner.

### Task 15: X-09 — perf harness, measurements and tuning — lane: BE2 (harness) + owning lanes (fixes) · size L

**PRD:** X-09. **Depends on:** Task 14; Tasks 6–8 for their pages.

**Files:** Create `tests/Lms.PerfTests/` (xUnit, Testcontainers MySQL with the seeded client; p50/p95/p99 over repeated calls for students, staff, classes, enrolments, gradebook grid, attendance, promotion rows, archive, notifications, audit, Excel export), `.github/workflows/perf.yml` (nightly + `workflow_dispatch`, L17), `tests/Lms.IntegrationTests/Performance/QueryCountTests.cs` (no N+1 per list endpoint), `tests/Lms.IntegrationTests/Performance/NoUnpagedListTests.cs`; results `docs/perf/2026-xx-m7-results.md`.

- [ ] **Step 1:** harness and first measurement. **Step 2:** file one issue per page over budget against the owning lane (EXPLAIN review, composite indexes, keyset paging where offset degrades, projections, `AsNoTracking`, streaming export). **Step 3:** re-measure; record promotion preview and execution targets. **Step 4: Commit** `test(perf): X-09 harness and results`.

**Acceptance:** p95 < 2 s per list page at 50,000 students; promotion targets met; results in `docs/perf/`.

### Task 16: H1 — year-end API clients, fakes, job panel, testids file — lane: FE2 · size M · PR #815

**PRD:** FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06. **Depends on:** Task 1 (rebase #815 on it).

**Files:** `src/Lms.Client/Services/YearEnd/{IYearCloseApi,IGradeChangesApi,IPromotionApi,ILifecycleApi,INewYearApi}.cs` + HTTP + fakes (#808 §4.2 rules, L6–L11 codes, server clock), `Components/YearEnd/JobProgressPanel.razor` (run id in the URL, L7), `ConfirmEffectDialog.razor`, `YearChip.razor`, `ClientServices.cs`, resx; `docs/m7-e2e-testids.md` (#808 §6, agreed with QA1, L19).

**Tests:** #808 H1 list, plus `Fake_requester_cannot_decide_own_request`, `Fake_archive_has_no_national_id_search`, `Fake_undo_uses_server_now`, `Job_panel_resumes_from_the_run_id_in_the_url`.

### Task 17: H2 — close year and grade-change approval — lane: FE1 · size L · PR #818

**PRD:** FR-YEAR-04, FR-GRD-06. **Depends on:** Tasks 16, 3; real API Tasks 4, 5; M5 FE gradebook and attendance pages on `main`.

**Scope:** #808 H2 with L4 (no blocking list beyond "not Active"; warnings with links), D88 (the close dialog says forums and messaging become read-only; `Msg_ReadOnly_year_closed` and the forum locked notice in ar and en), L2 routes (create by class course, item, student, version), L6 (disabled decide button for the requester, reject note), `LockedNotice` on the M5 pages, "changed after close" marker; hooks per the testids file.

**Tests:** #808 H2 list, with `Close_dialog_shows_warnings_and_never_blocks_on_them` replacing the blocking test; `Page_renders_rtl_in_arabic`; scan tests (L19).

### Task 18: H3 — promotion set-up and preview — lane: FE2 · size L

**PRD:** FR-STU-07, FR-ORG-09, D83. **Depends on:** Tasks 16, 3; real API Task 6.

**Scope:** #808 H3 with L7 (overrides saved on the server, ≤ 200 per call, bulk by filter), L9 (blocking `TargetClassMissing` with the class select), D84 warnings; no client-side full load.

**Tests:** #808 H3 list plus `Decisions_are_sent_in_batches_of_at_most_200`.

### Task 19: H4 — promotion execute, progress, result, undo, history — lane: FE2 · size L

**PRD:** FR-STU-07, D12, D84. **Depends on:** Task 18; real API Task 7.

**Scope:** #808 H4 with L7 (run id URL, no `JobId`), L8 (clock time and countdown from `ServerNowUtc` / `UndoUntilUtc`, two reasons), L16 (one finished event with state); the placement history column in the student profile.

**Tests:** #808 H4 list.

### Task 20: H5 — withdrawal, graduation, archive, re-admission — lane: FE1 · size L · PR #823

**PRD:** FR-STU-08, FR-SEC-07, D25, D81, D82. **Depends on:** Tasks 16, 3; real API Task 8; Task 2 for the reasons page.

**Scope:** #808 H5 with L10 (future date up to one year, "sign-in stops on that date"), L11 (archive search by name or code only; the same blocked sign-in words), L12 (a small reasons editor under `Students.ManageWithdrawalReasons`, client root); no bulk withdrawal.

**Tests:** #808 H5 list, with `Archive_search_offers_name_and_code_only` and `Reasons_editor_needs_ManageWithdrawalReasons`.

### Task 21: H6 — open a new year from copy or template — lane: FE2 · size M

**PRD:** FR-YEAR-05, FR-ORG-07/08, D22. **Depends on:** Tasks 16, 3; real API Task 9.

**Scope:** #808 H6 with L13 (students and teachers never copied, both said in words; `active_year_exists` offers the close-year link), L7 (run id URL).

**Tests:** #808 H6 list plus `Copy_step_says_teachers_are_not_copied`.

### Task 22: H7 — SAML and LDAP set-up and sign-in — lane: FE1 · size M

**PRD:** FR-SEC-08 part 2, FR-SEC-07, D85. **Depends on:** Task 2; real API Tasks 11, 12. Last FE feature task.

**Scope:** #808 H7 with L14 (no plain LDAP option, no group mapping, "Needs attention" chip from the certificate expiry field, no notification).

**Tests:** #808 H7 list.

### Task 23: H8 — year-end navigation, home checklist, notifications — lane: FE1 · size S

**PRD:** FR-YEAR-04, FR-STU-07, D86. **Depends on:** Tasks 17–21; real API Task 13.

**Scope:** #808 H8 with L15 (five steps from the server) and L16 (the four #812 events in ar and en; no certificate event).

**Tests:** #808 H8 list with `Checklist_shows_five_steps_from_the_server`.

### Task 24: M7 isolation, permission and probe suite — lane: QA (QA2) · size M

**PRD:** §9.2, FR-SEC-05, X-05. **Depends on:** Task 1 (scaffolding); grows per BE merge.

**Files:** `tests/Lms.IntegrationTests/Security/M7Callers.cs` (the callers of the route table, incl. the Manager of a sibling unit, an Org Admin who teaches a class course, a dual-role user, a guardian of an exited child), `M7Routes.cs`, `ProbeRows/M7*.cs` (merged from the BE PRs), `M7EndpointMatrixTests.cs`, `M7IsolationTests.cs`; `Every_api_route_has_a_probe_row` (fails on a mapped route with no row, across M0–M7).

**Acceptance:** matrix green on `main`; every table and bypass site of this plan has its isolation test.

### Task 25: Security review X-08 — lane: QA (QA3) + owning lanes for fixes · size L

**PRD:** X-08, PRD §9.5. **Depends on:** Tasks 4–12 (checklist started with Task 1).

**Files:** `docs/security/m7-review.md` and the release-wide ASVS L2 checklist: authorization matrix (Task 24), isolation tables, over-posting on every request DTO, rate limits and lockout, headers (CSP, HSTS, cookies), uploads, SSRF via providers and webhooks, secret scanning and `dotnet list package --vulnerable` in CI, personal data in logs, the closed-year guard, separation of duties, leaver privacy, SAML and LDAP from the Task 10 corpus. Findings filed as `bug` + `sev:*` against the owning lane.

**Acceptance:** no open `sev:critical` or `sev:high`; checklist signed.

### Task 26: Accessibility pass X-10 — lane: QA (QA1) + FE1/FE2 for fixes + UX sign-off · size L

**PRD:** X-10, D87. **Depends on:** Tasks 17–23 and the M5/M6 pages on `main`.

**Scope:** WCAG 2.1 AA over every page added since M5 (M5 gradebook, timetable, attendance, excuses; M6 messaging, announcements, moderation, settings; M7): automated scan (axe in Playwright), the bUnit scan tests of L19, keyboard walks of close year, promotion run and withdrawal, a manual NVDA/VoiceOver checklist in ar and en, 360 px, reduced motion, forced colours, 44 px targets. Findings filed against FE1/FE2; the designer's sign-off is recorded here.

**Acceptance:** no open serious or critical a11y findings; `docs/accessibility/m7-pass.md` signed by QA1 and the designer.

### Task 27: PRD §9 E2E — year close → promotion → new year, ar and en — lane: QA (QA1) · size M

**PRD:** §9.3 scenario 5, FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06. **Depends on:** Tasks 4–9, 17–21.

**Scenario:** Org Admin closes the year (typed name; writes now refused); a teacher requests a change, a Manager approves, a requester cannot; the Manager runs the promotion in own units (preview, an override, execute, progress, result), undoes inside the window, re-executes; a student is withdrawn with a future date (dev sweep), then re-admitted with the same code; the guardian reads the closed year; the Org Admin opens the next year from a copy. Run in ar (RTL) and en, with the `e2e.lock`; undo checked through `data-until`, never by waiting.

**Acceptance:** green in CI in both languages.

### Task 28: PRD §9 release suite in ar and en — lane: QA (QA2) · size L

**PRD:** §9.1–§9.5. **Depends on:** Tasks 24, 27; M4a–M6 exit demos on `main`.

**Scope:** the full §9.3 suite (scenarios 1–4 from the earlier milestones re-run in both languages, plus Task 27), §9.1 the two sample taxonomies configured without code changes, §9.2 the isolation matrix (Task 24), §9.5 CI green, no open Critical/High, X-09 met (Task 15). The release checklist `docs/release/v1-acceptance.md` lists each §9 item with its evidence; FRs not implemented are listed as owner-deferred or flagged to the Lead.

**Acceptance:** every §9 item has evidence; the Lead tells the owner v1 is ready.

---

## Exit criteria (M7 done)
1. Tasks 27 and 28 green in CI in ar and en; Task 24 green on `main`; M4a–M6 exit demos still green.
2. `docs/security/m7-review.md` has no open `sev:critical` or `sev:high`; every bypass and system-context site above has its cross-client test.
3. X-09 met on the seeded 50,000-student client (`docs/perf/`); X-10 pass signed (`docs/accessibility/m7-pass.md`).
4. No write into a closed year (grades, attendance, assignments, quizzes, forums, class-scoped messaging; D88) except approved changes; the requester never decides; undo never after activity; the reason never reaches a student or guardian.
5. `docs/release/v1-acceptance.md` complete; `docs/STATUS.md` updated; owner told; the remaining owner questions below listed with their defaults.

## Execution notes for the Team Lead
- **Issues:** one per task (29: 2 Lead, 1 UX, 13 BE, 8 FE, 5 QA) on milestone "M7 Year-End & Release", labels `M7` + `lane:*` + `area:year-end` / `area:grading` / `area:students` / `area:identity` / `area:perf` / `area:security` / `area:a11y` (create the missing ones), on the "LMS v1" project; Tasks 16, 17 and 20 attach to the open PRs #815, #818 and #823; Tasks 3 to #814 and #816. Replace "Task N" with issue numbers.
- **Before the plan merges:** QA reviews it against #808, #822 and #812; then #808 and #822 close as superseded by this plan.
- **Order:** Task 1 (#812) merges now. Task 2 merges as soon as M6 #724 is on `main` (L1). Wave A (start now on Task 1): Tasks 6 (BE3), 9 (BE2), 16 (FE2, #815), 3 (UX), 24 scaffolding (QA2). Wave B (after Task 2): Tasks 8 (BE4), 10 (BE1 + QA3), 20 (FE1, #823). Wave C (after the M5 grading and attendance PRs): Task 4 (BE1), then 5 (BE2), 17 (FE1, #818); Task 4a (BE3) once Task 4 and M6 Task 10 are on `main`. Wave D: 7 (BE3), 11 (BE1), 12 (BE4), 14 (BE2), 18 and 19 (FE2), 21 (FE2). Wave E: 13, 15, 22, 23, 25, 26, 27, 28.
- **Critical paths:** close 1 → 4 → 5 → 17 → 27 (4 → 4a for messaging); promotion 1 → 6 → 7 → 18 → 19 → 27; lifecycle 1 → 2 → 8 → 20 → 27; SSO 2 → 10 → 11/12 → 22; release 14 → 15 and 24 → 28.
- **BE lanes (by folder, L18):** **BE1** `Infrastructure/YearEnd/Close`, the guard sweep, `Infrastructure/Identity/Saml`: 4 → 10 → 11. **BE2** `Infrastructure/Grading/GradeChanges`, `IGradebookWriter` change, `Infrastructure/YearEnd/{OpenNext,Checklist}`, `Api/Dev/PerfSeed`, `tests/Lms.PerfTests`: 9 → 5 → 14 → 13 → 15. **BE3** `Domain/YearEnd` promotion entities, `Infrastructure/YearEnd/Promotion`, activity probes, and the messaging guard on its own M6 code: 6 → 7 → 4a (4a may go before 7 when Task 4 lands first). **BE4** `Domain/Students` exits and reasons, `Infrastructure/Students/Lifecycle`, `GuardianVisibility`, `Infrastructure/Identity/Ldap`: 8 → 12. Keep each developer at most two open issues.
- **Shared BE files (serialize merges):** `Program.cs`, `IGradebookWriter` (Task 5 only), `PlacementGuard` (Task 4 only), `ClassStudent` (Task 7 only), `GuardianVisibility` (Task 8 only), `ExternalSchemeRegistry` (Task 11, then 12), M6 messaging handlers (Task 4a only, BE3), forum writers (Task 4 only), `IsolationBypassGuardTests.cs` (only this plan's sites), `ProbeRows/` (one file per PR), migrations, `Lms.Shared` (Lead only).
- **FE lanes:** **FE2** `Services/YearEnd`, `Components/YearEnd` (with the designer's #816), `Pages/Promotion`, `Pages/AcademicYears/NewYear*`: 16 → 18 → 19 → 21. **FE1** `Pages/AcademicYears/Close*`, `Pages/GradeChanges`, `LockedNotice` on M5 pages, `Pages/Students/{Lifecycle,Archive}*`, `Pages/Access/IdentityProvider*` (SAML/LDAP), `Pages/Authentication` LDAP form, `NavCatalog` year-end section, home checklist: 17 → 20 → 22 → 23. Shared FE files: resx (own prefix), `ClientServices.cs` (one line each), `NavCatalog.cs` (Task 23 only), `AcademicYearsPage` (Task 17 adds Close, Task 21 adds Open next: merge 17 first), `lms-stitch.css` (designer only).
- **QA:** **QA1** reviews BE2 and FE2 PRs; owns Tasks 26 and 27. **QA2** reviews BE3 (incl. Task 4a), BE4 and FE1 PRs; owns Tasks 24 and 28. **QA3** reviews BE1 and gives the security review on Tasks 4, 5, 7, 8, 11, 12; co-owns Task 10; owns Task 25.
- **Push discipline (§4.1):** batched reviews, one push per fix, drafts until locally green; the owner's OK to cancel draft-PR CI runs when the queue is saturated still applies; the perf workflow never runs per PR.
- **Deferral order if late:** Task 23's checklist card (the read model ships), then Task 22 SSO UI polish (the API ships behind the provider admin); never Tasks 4, 4a, 5, 7, 8, 24, 25, 27, 28.
- **Splits from DEV-PLAN §2 M7:** item 1 → Tasks 4, 4a, 5, 17; item 2 → 6, 7, 18, 19; item 3 → 8, 20; item 4 → 9, 21; item 5 → 10, 11, 12, 22; item 6 → 14, 15; item 7 → 25, 26; item 8 → 27, 28.

## PRD / DEV-PLAN inconsistencies found
- FR-STU-07 says "target class" per student; the level mapping gives a unit. The class rule is now D89; the FR line should reference it.
- FR-GRD-06 says "Manager approval"; D80 adds the Org Admin. The FR line should say "Manager or Org Admin, never the requester".
- D79 says closing "locks grades and attendance"; D88 extends it to assignments, quizzes, forums and messaging. FR-YEAR-04 should reference D88.
- FR-STU-08 has no permission split; D91 settles it (re-admission: Org Admin and Manager); the FR line should reference D91.
- DEV-PLAN M7 item 7 pairs accessibility and security; this plan gives them separate tasks and owners (25, 26).
- DEV-PLAN item 8 says "E2E suite in ar + en"; this plan splits the new year-end scenario (27) from the release-wide §9 run (28).

## Owner questions — answered
All four questions of revision 1 were answered by the owner on 2026-11-14 and recorded in the PRD; none remain open.
- **O1 Forums and messaging after close → D88:** locked, read-only (changes the default; L5, Tasks 4 and 4a).
- **O2 Target class in promotion → D89:** by section code; Repeat keeps level and section; every row editable (L9 unchanged).
- **O3 Teachers in the next year → D90:** not copied (L13 unchanged).
- **O4 Re-admission by the Counselor → D91:** no; Org Admin and Manager only, as in #812 (unchanged).
