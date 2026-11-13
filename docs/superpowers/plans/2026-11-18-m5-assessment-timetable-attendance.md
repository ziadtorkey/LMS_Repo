# M5 Assessment, Timetable & Attendance Implementation Plan

> **Status: DRAFT for review (Team Lead, 2026-11-18).** Merges the QA-approved backend proposal (PR #611, `be/m5-proposal`, BE4; QA3 approved at `855bc8e1`, re-approved at `0e918a6b`, plus BE1's live-session review on #611) and the QA-approved frontend proposal (PR #616, `fe/m5-fe-proposal`, FE2; QA3 approved at `1a671df4` with corrections) into one task list for **M5**. Owner decisions applied: **D58** (all #611 Q1–Q20 defaults), **D59** (Saudi grading, GPA and term presets as editable data), **D60** (rooms are free text), **D61** (excuse submitters setting, absence alert defaults). QA3's required items on #611 (1–11 and the re-review's 1–4) and on #616 (conflicts, accessibility corrections, Q-H6) are folded into the Decisions and the task text below. The Lead decisions that answer the proposals' "for the Lead" questions (#616 Q-L1..L4, Q-H1..H7, #611 Q11/Q14–Q16, BE1's items 1–11) are marked **Lead decision**. Team: Lead, BE1–BE4, FE1, FE2, QA1–QA3, UI/UX Designer. GitHub issues are created by the Lead after this plan is reviewed.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. In this project each task = one GitHub issue and one PR; the lane (Lead contract / BE1–BE4 / FE1–FE2 / QA1–QA3 / UX) is marked on each task.

**Goal:** A teacher runs the gradebook of a class course per term (auto items from assignments, quizzes and packages, manual items, categories with weights, overrides with reasons, a column import/export, publish now or on a schedule); students and guardians see published grades only, under a scale that follows the Saudi presets (D59) as editable data. An admin sets periods and working days, builds and publishes a class timetable with conflict checks (block or warn), handles substitutes by date; published slots generate attendance sessions; a teacher takes attendance on a phone in seconds, and confirms attendance proposed from a Zoom session. Guardians (and students, if the client allows) submit excuses; counselors approve, reject or escalate; guardians are told of absences and counselors of students over the threshold. All bilingual (ar RTL / en), isolated per client, subtree and relationship.

**Architecture:** Same modular monolith. Four feature areas: **Grading** (`Domain/Grading` extended, `Infrastructure/Grading`, `Features/Gradebook`, `Features/GradingScales`), **Timetable** (`Domain/Timetable`, `Infrastructure/Timetable`, `Features/Timetable`), **Attendance** (`Domain/Attendance`, `Infrastructure/Attendance`, `Features/Attendance`), **Excuses** (`Domain/Excuses`, `Infrastructure/Excuses`, `Features/Excuses`). One pure calculation engine (`GradebookCalculator`, no I/O) feeds every grade read; one published-only query helper (`PublishedGradeReads`) is the only path for student and guardian grade data; one writer (`IGradebookWriter`) is the only grade write path (lock seam for M7). Grade events (M4a outbox) are inputs, never edited. Attendance consumes the M4b seam `ILiveSessionAttendanceSink` (#544). Local calendar days come from one service (`IClientCalendar`: client time zone, working days, holidays).

**Tech Stack:** unchanged. ClosedXML (already in `Directory.Packages.props`) for `.xlsx` export/import; Hangfire recurring/delayed jobs through `IBackgroundJobs`; `TimeZoneInfo` with IANA ids. No new client packages.

**Spec:** `docs/PRD.md` FR-GRD-01..05 (FR-GRD-06 is M7; only its seam is built here), FR-TT-01..06, FR-ATT-01, FR-EXC-01..04, X-01, X-05, X-07, X-10, decisions D58–D61 (and D51 grade seam, D57 re-attempt grading). Master plan: `docs/DEV-PLAN.md` §2 M5 items 1–9. Proposals: `2026-11-17-m5-backend-proposal.md` (#611), `2026-11-17-m5-frontend-proposal.md` (#616). Previous plan: `2026-11-16-m4b-live-and-packages.md`.

## Scope

**In scope**
- Grading scales (percentage, letter, GPA band points), bands, inheritance, one default per client, Saudi presets as versioned seed templates (provisional until confirmed), weight templates; academic-year terms-per-year option (2 or 3) with a term-name template.
- Gradebook per ClassCourse and term: categories, weights, auto and manual items, entries, overrides, excused-from-item, calculation engine, grid, column import/export, publish states (hidden, published, scheduled), scheduled publish job, publish snapshots, notifications; student and guardian published-only views with a band legend; supervisor/manager reports (JSON + Excel); lock seam for M7.
- Client time zone setting and the client calendar service; periods per unit with override; working days; timetable slots (draft/published), conflict checks (teacher, class; Block/Warn), builder, views (class, teacher, student, guardian), Excel export, copy from previous term; dated exceptions (substitute, cancelled, moved).
- Attendance sessions generated from published slots (holidays and non-working days skipped), ad hoc sessions, live-session proposals with teacher confirmation; taking with mark-all-present; edit window; change history; summaries and reports; own and guardian reads.
- Absence/late notifications to guardians, excuse submission (setting-controlled submitters), counselor queue with approve/reject/escalate, attachment handling, threshold alerts.
- FE for all of it (two tracks), UX spec, QA probe suite, grade-leak security review, three E2E exit demos.

**Out of scope (v1 or later milestones)**
- Grade lock at year close and Manager-approved changes (FR-GRD-06, M7; D58); cumulative GPA across courses or credit hours (D58); PDF report cards (M7; D58); LTI grade items (D50, D58).
- Room list and room clash detection (D60); teacher availability, max periods per day or hours per week (D58).
- Email/SMS/push delivery and quiet hours (M6): M5 publishes in-app notification events only.
- Antivirus scanning of excuse attachments (a no-op scanner hook only, as packages S7).
- A full gradebook grid on phones (a student-first single-score view only; Lead decision 19).

## What M5 builds on (re-check on `origin/main` when each task starts)
- **Grading seam (M4a/M4b):** `GradeEvent` (append-only, revisioned, unique `(SourceKind, SourceId, StudentProfileId, Revision)`), `IGradeEvents.RecordAsync` / `GradeEventWriter`, `GradeSourceKind {Assignment, Quiz, Scorm, H5p}` (Scorm/H5p from #548/#606; unknown kinds must not crash the gradebook), D57 graded-attempt choice for packages (the event already reflects it).
- **Academic structure:** `AcademicYear`, `Term`, `CalendarEvent` with `CalendarEventKind {Holiday, Event}` (`CalendarEndpoints`), `PlacementGuard.EnsureWritableAsync`, `ClassCourse`, `ClassCourseTeacher` (`From` / `To` dates), `CourseEnrollment`, `GroupMember`, `StudentGuardian`.
- **Live sessions (M4b):** `LiveSession`, `LiveSessionParticipant(StudentId?, MatchState Matched|Guest, JoinedAtUtc, LeftAtUtc, DurationSeconds)`, `ILiveSessionAttendanceSink.SessionEndedAsync(...)` (no-op today, final shape and the "never throws" wrapper land in **#544**), `LessonModule.Target` (`Group` + `GroupId`).
- **Kernels:** `IBackgroundJobs` (`Enqueue`, `EnqueueAsSystem`, `AddOrUpdateRecurring`, `ScheduleAsSystem`), `ISystemContext`, `INotificationPublisher`, `SettingKeys` / `SettingCatalog` (unit inheritance, locks), `IFileStorage` + asset service, `AuditExporter` (formula-injection escaping), Imports module limits, `IsolationBypassGuardTests`, `BuiltInRoleDefaults.CurrentVersion = 7`.
- **FE:** `ClassCourseTabs`, `Pages/AcademicYears/*` (`YearDialog`, `TermsPanel`), Stitch settings pages with `lms-save-bar`, `ServerTable`, `FormFlush` (#536), `CloseOnEscapeKey`, `AssetUploader`, `download.js`, `NotificationText` + parity tests, `LiveSessionsList`, `LiveSessionPanel`.
- **Prerequisite:** M4b exit (M4b plan Tasks 22–23) green on `main` before the M5 exit demos; M5 code may start on any merged seam. Task 18 needs #544 merged.

## Decisions

### Owner decisions (restated)

| # | Decision | Source |
|---|---|---|
| D58 | All #611 Q1–Q20 defaults: grade lock at year close stays in M7 (M5 builds only the write seam); GPA in M5 = the band's points per course; a ClassCourse may pick a scale visible at its unit; periods per unit, a lower unit may override; missing grades normalised over graded categories (setting `Grades.MissingPolicy`: Normalise / Zero / HideUntilComplete); category average points-weighted (setting `Grades.CategoryMethod`); no teacher availability limits; holidays skipped by session generation; Zoom attendance **proposes** Present and the teacher confirms; guardians see published grades only and never titles of hidden items; Excel export only (PDF report cards M7); LTI out of v1; an event's term = the term whose dates contain `ScoredAtUtc`, else the class's current term (Q8); attendance link lives on the attendance side only (Q11); excuse reasons and escalation per Lead decisions 13–14 below. | PRD D58 |
| D59 | Grading scales, GPA and the academic-year structure follow Saudi standards, shipped as **editable data**: Ministry of Education school bands with Arabic and English descriptors; university 5-point and 4-point GPA scales; two or three terms per year as configured per client. Nothing in the engine depends on the numbers. | PRD D59, #611 §4.6 |
| D60 | Room = a free-text label on a slot; no room list, no room conflict (amends FR-TT-02/03 "room double-booked"). | PRD D60 |
| D61 | `Excuses.Submitters` per client (`GuardiansOnly` default, `GuardiansAndStudents`). Absence alert default **5 absences in 30 days**, a `Late` counts as **half**; both configurable per client. | PRD D61 |

### Lead decisions

| # | Decision | Answers |
|---|---|---|
| L1 | **Waves and tracks.** Three contract waves: **wave 1** = scales, weight templates, terms-per-year, gradebook, grid, publishing, grade reads, reports, the client time-zone setting; **wave 2** = periods, working days, timetable, exceptions, `IsSubstituteOn`; **wave 3** = attendance, live proposal, excuses, alerts. Waves 1 and 2 are opened together on day 1 (two PRs) so the two BE tracks start in parallel; wave 3 merges before Task 16 starts (target: end of week 1). Two parallel BE tracks: **gradebook** (BE1, BE2) and **timetable + attendance** (BE3, BE4). FE follows the same split (FE1 gradebook, FE2 timetable/attendance/excuses). Ad hoc attendance sessions do **not** move to wave 1: the take page waits for wave 3. | #616 Q-L1, #611 §9 |
| L2 | **Client time zone (QA #611 item 1).** New setting `Org.TimeZone` (IANA id, client level, not unit-overridable; default = the deployment's `Jobs:TimeZone`; provisioning of Saudi clients sets `Asia/Riyadh`). One service `IClientCalendar` (Task 13) answers "local today", "local date of an instant", "is working day" (`Timetable.WorkingDays` at the unit) and "is holiday" (`CalendarEvent` kind `Holiday` of the unit's year). Generation, `GenerateDaysAhead`, the edit window, excuse deadlines, threshold windows and publish-at displays all use it; no other code converts to local days. DST tests use a DST zone (`Europe/London`) even though `Asia/Riyadh` has none. | QA #611 (1), #611 A9 |
| L3 | **Auto items default to `Hidden` (QA #611 item 3).** An item auto-created from a grade event starts `Hidden`; the grid header chip and the empty state say "stays hidden until you publish". | QA #611 (3), QA #616 |
| L4 | **No writes on read (QA #611 item 4).** Items are created by an `IGradeEventObserver` that `GradeEventWriter.RecordAsync` calls in the same transaction (reviewed change to M4a code, Task 7), so every new event creates its item at once. A **backfill** for events recorded before M5 runs in the grid GET only for callers with `Grades.Manage`, never in a Closed year, and is idempotent on the unique `(GradebookId, SourceKind, SourceId)` (a unique-violation is swallowed and re-read). Students, guardians and reports never create rows. | QA #611 (4) |
| L5 | **Lock semantics now, lock button in M7 (QA #611 item 2).** `Gradebook.State = Locked` + `LockedAtUtc`. When `Locked`, every read returns the `GradeSnapshot(Kind = Lock)` values and grade events with `RecordedAtUtc > LockedAtUtc` are ignored and counted as "events after lock" (staff marker only); `IGradebookWriter` refuses writes with 409 `gradebook_locked`. M5 tests this with a test-only lock (Task 12); M7 adds the UI, approvals and `GradeChangeRequest`. | QA #611 (2), D58 |
| L6 | **Published-only helper is the only student/guardian path (A4 + QA #611 item 11).** `PublishedGradeReads` serves: own/child grades, totals and bands, band legend, class statistics (if any are ever shown to students: average, min, max, rank — **none are shown in M5**), the "newer score exists" marker (staff only, never student), search/autocomplete of items, notifications and error messages. Totals for students/guardians are computed by the engine from published items only. The staff "what the student sees" preview (`GET …/gradebook/students/{sid}/student-view`) calls the same helper and requires `Grades.Manage` + relationship; no student-reachable route renders it. A leak test per endpoint. | QA #611 (11), QA #616 |
| L7 | **Saudi presets (QA #611 re-review 1–2).** Provisioning creates per client **one plain default scale** ("Percentage", Kind Percentage, no band text) plus the Saudi presets as **templates** (`IsTemplate = true`, never `IsDefault`): school 5-band, university 5-point, university 4-point (numbers from #611 §4.6). Each seeded scale carries `Provisional = true` and `SeedKey` + `SeedVersion`. A copy made from a template stays `Provisional` until a unit admin **confirms** it (`POST …/confirm`) or edits a band. **Publishing** grades under a provisional scale is refused with 409 `scale_provisional` unless the request carries `acknowledgeProvisionalScale = true` (the dialog warns; the acknowledgement is audited). Seed changes bump `SeedVersion`: unedited copies show "update available" (admin applies), edited copies are never touched. | QA #611 re-review (1, 2), D59 |
| L8 | **Terms per year (QA #611 re-review 3).** Setting `AcademicYears.TermsPerYear` (2 or 3, client level, default **3** until Q22 is answered) and a term-name template (ar/en) per structure. Changing the structure of a year is allowed only while it is `Planned` and refused with 409 `terms_have_dependants` when any term has a gradebook, slot or attendance session. Existing terms are never deleted or renamed by the switch; the admin edits them. | QA #611 re-review (3), #616 Q-H2 |
| L9 | **Import/export security (QA #611 item 5).** Every export cell goes through the `AuditExporter` formula-injection escaping (shared helper `SpreadsheetCell.Safe`, moved, not copied); imports accept `.xlsx` only (macro and binary formats refused), Imports module limits (size `Files.MaxUploadMb`, 2 000 rows, 50 columns, 30 s budget, external links and defined names ignored); preview returns a token bound to (uploader, gradebook, item, file SHA-256) valid 15 min; apply re-validates against current data; both audited; event-derived scores never written. | QA #611 (5) |
| L10 | **Excuse attachments (QA #611 item 6, #616 Q-H6).** Allow-list `jpeg`, `png`, `webp`, `pdf`; size `Excuses.MaxAttachmentMb` (default 5); content sniffed server side; stored privately; downloadable only by the submitter, the student's linked guardians and staff with `Excuses.Review` in the student's unit subtree, through a signed URL valid 5 minutes, `Content-Disposition: attachment`, `nosniff`. Inline preview in the review drawer only for the raster types (jpeg/png/webp); PDFs download only (no in-app PDF viewer in v1). Scanner hook `IFileScanner` (no-op in v1, as packages S7). Retention: attachments of rejected or withdrawn excuses purged after `Excuses.AttachmentRetentionDays` (default 365) by a job; approved excuses keep them for the year (owner confirmation O2). | QA #611 (6), #616 Q-H6 |
| L11 | **Live-session attendance (BE1 review on #611, items 1–11).** (1) No display-name matching: only `Matched` participants (authenticated email, M4b Decision 7) are auto-proposed; `Guest` rows go to manual mapping. (2) Read `LiveSessionParticipant` rows; never call `GetParticipantsAsync` again. (3) The M5 sink implementation only enqueues an idempotent job `attendance-live:{sessionId}` and returns. (4) A **reconcile sweep** creates missing proposals for `Ended` sessions with no `AttendanceSession(LiveSessionId)`; the unique key on `LiveSessionId` makes it idempotent. (5) **Yes: `LiveSession.ParticipantsImportedAtUtc` (nullable) is added in #544**; M5 proposes only when it is set, otherwise the session shows "participants not available yet" and nobody is proposed Absent. (6) Presence = merged non-overlapping segments vs the scheduled length; `Late` from the first `JoinedAtUtc` vs `StartsAtUtc`. (7) Roster = the module's targeted students (group filter applied). (8) Manual mappings live in the attendance-owned `LiveAttendanceMapping`, never in `LiveSessionParticipant`. (9) `AttendanceSession.LiveSessionId` is a plain column (no FK cascade); a taken session stays valid after the live session is cancelled. (10) EXC-01 guardian notices fire on **teacher confirmation**, never on the proposal. (11) Hosts/staff and guests not adjacent to the roster are ignored; only unmatched guests are listed for mapping. | #611 BE1 comment, D58 Q11/Q12 |
| L12 | **Optimistic concurrency.** `GradeEntry`, `AttendanceRecord` (session-level `Version`), `TimetableSlot` carry a row version; a stale write answers 409 `concurrency_conflict` (new code) with the current value; the FE reloads the row and tells the user. | QA #611 (risks) |
| L13 | **Excuse reasons (Q14).** Fixed catalog `ExcuseReasons {Illness, Family, Official, Other}`; settings `Excuses.AllowedReasons` (subset, default all), `Excuses.AttachmentRequiredReasons` (default none), `Excuses.MaxDaysAfterAbsence` (default 5 school days, counted with `IClientCalendar`). | #611 Q14, D58 |
| L14 | **Escalation (Q15) and edit after window (Q16).** Escalate to any staff user holding `Excuses.Review` or `Excuses.Escalate` whose scope covers the student's unit (Manager by default in the picker). The reviewer cannot be the submitter (409 `excuse_reviewer_is_submitter`). `Attendance.EditWindowHours` default 48; after it, users with `Attendance.Edit` (Manager and Counselor by default) may edit with a required reason, audited through `AttendanceRecordChange`. | #611 Q15/Q16, QA answers |
| L15 | **Teacher assignment dates (QA #611 item 8).** `Grades.*` and `Attendance.Take` relationship checks use `ClassCourseTeacher` rows active on the **local date** of the request (`From <= today <= To`); a former teacher gets 404. `IsSubstituteOn(sessionId)` is valid only for the exception's date and only for `Attendance.Take` on that one session. | QA #611 (8, 10) |
| L16 | **Counselor and supervisor scope (QA #611 items 7, 9).** Counselor: `Attendance.View`, `Excuses.Review`, `Excuses.Escalate` by unit subtree, **no** `Grades.*`; Supervisor: `Grades.View`, `Grades.ViewReports`, `Attendance.View`, `Timetable.View` for their supervised courses only. Guardians: active `StudentGuardian` link; a miss is 404; a guardian of two children never sees one child's data under the other's id. | QA #611 (7, 9) |
| L17 | **Grid size and paging (#616 S2, Q-H5).** The grid pages by **50 students** (`page`, `pageSize` ≤ 100) with all items as columns; index `(ClassCourseId, TermId)` on gradebooks and `(GradebookId, StudentProfileId)` on entries; budget test: 40 students × 60 items computed in < 300 ms server time on the CI database. The take page shows up to 200 rows with a search box, no virtualisation. | #616 S2, Q-H5, QA #611 |
| L18 | **Client never computes (Q-L3).** The grid response carries `isValid`, `weightsTotal`, per-cell state, category results, final, band, `computedAtUtc`, `serverNowUtc`; an entry write returns the recomputed row. The weights chip sums only the integers being typed, as a preview. `MyGradesPage` shows **no cross-course "overall result"** (D58: band points per course only). | #616 Q-L3, QA #616 |
| L19 | **Phone gradebook (Q-H4).** Under 600 px the grid is a student-first list for correcting single scores; entering a whole class on a phone is not planned. One layout is rendered at a time (no CSS-hidden twin); unsaved edits survive the breakpoint change. | #616 Q-H4, QA #616 |
| L20 | **Digits (Q-H3).** Western digits in grades, dates, times and counts in both languages (the product's current behaviour); recorded in `docs/DESIGN.md` by Task 21. The owner may revisit (O3). | #616 Q-H3 |
| L21 | **`canSubmit` (Q-L2).** The child's/own attendance read returns `canSubmitExcuse` resolved by the server from `Excuses.Submitters` and the relationship; the client never reads the setting. | #616 Q-L2, D61 |
| L22 | **Tabs (Q-L4).** FE1 adds **both** `ClassCourseTabs` entries (Gradebook: `Grades.Manage` or `Grades.View`; Attendance: `Attendance.Take` or `Attendance.View`) in Task 22, the Attendance tab pointing at `ClassCourseAttendanceTab.razor`, a stub that FE2 owns and fills in Task 33. `YearDialog`'s terms option is FE1's (Task 30). | #616 Q-L4 |
| L23 | **Provisional chip (Q-H1).** Admin pages only. Students and guardians see the band legend as configured; a provisional scale cannot be published under silently (L7). | #616 Q-H1, QA #616 |
| L24 | **Grid keyboard (QA #616 accessibility 1–6).** Enter or F2 toggles edit; `Shift+F10` / the context-menu key opens the cell menu (not F2); Tab moves right **only while editing** and leaves the grid in navigation mode; `scroll-padding` so the focused cell is never under the sticky header/column (WCAG 2.4.11), tested in both directions; focus outline ≥ 3:1 against selected/hover/override backgrounds in light and dark and visible in forced-colors mode; an `aria-describedby` hint "Press Enter to edit" and announcements on entering/leaving edit; grouped headers use `colgroup`/`colspan` with `scope`; saves go to one polite live region, a rejected value is announced once assertively. | QA #616 |
| L25 | **Attendance shortcuts and local draft (QA #616).** Shortcut keys are digits `1`–`4` (matched by `event.code`, shown on the buttons, active only while a row is focused); the browser draft stores only session id, student ids, statuses and the session `Version`, expires after 12 h, is cleared on save and sign-out, and never overwrites a newer server state. | QA #616 |
| L26 | **Notifications.** New events `Grades.Published` (grouped: one per student per publish batch), `Attendance.Absent` (grouped per guardian per session, on Taken/confirmed only), `Excuses.Decided`, `Excuses.Escalated`, `Attendance.ThresholdCrossed`. No grade values or hidden item titles in any notification, log or error; a resource test fails on `{points}`-style placeholders in grade notification texts. | #611 §8, #616 risks |
| L27 | **Exports carry the scale legend.** Grade exports and the report Excel add a "Scale" sheet (name, bands, ar/en descriptors). The printed transcript question (Q24) applies to M7 PDFs only. | #611 Q24 (default) |

## Global Constraints
- **Every M0–M4b Global Constraint still applies** (`X-Lms-Context`, Guid v7, utf8mb4, default culture `ar`, explicit permissions per built-in role, `UnitAccess.CheckUnitAsync` / `RelationshipAccess.CheckClassCourseAsync` with 404 on a miss, ProblemDetails `code` from `ProblemCodes`, `// ISOLATION-BYPASS: <reason>` + allow-list entry, no new `DbSet` properties, two `Program.cs` lines per module, one migration per PR with a working `Down()`, `[Audited]` + an isolation test per business entity, probe rows per route in the PR that adds the route, FE API interface + fake per module, ar/en resource parity, RTL screenshots, `PlacementGuard.EnsureWritableAsync` on every write, `serverNowUtc` in every timed response).
- **Isolation shapes (all `IOrgUnitScoped`, repather-covered):** unit fields copied from the ClassCourse's class for `Gradebook`, `GradeCategory`, `GradeItem`, `GradeEntry`, `GradeSnapshot`, `TimetableSlot`, `TimetableException`, `AttendanceSession`, `AttendanceRecord`, `AttendanceRecordChange`, `LiveAttendanceMapping`; from the owning unit for `GradingScale`, `GradingScaleBand`, `WeightTemplate`, `WeightTemplateItem`, `Period`; from the student's home unit for `AbsenceExcuse`, `AbsenceExcuseSession`, `AttendanceAlert`.
- **Privacy:** grades and attendance are minors' data: no values in logs, notifications or audit summaries beyond ids; exports audited; attachments per L10.
- **Audit (`[Audited]`):** `GradingScale`, `WeightTemplate`, `Gradebook`, `GradeCategory`, `GradeItem` (publish changes), `GradeEntry` (overrides), `GradeSnapshot`, `Period`, `TimetableSlot` (publish), `TimetableException`, `AttendanceRecordChange`, `AbsenceExcuse` (decisions). Exports, imports and provisional-scale acknowledgements are audit events.
- **Migrations:** `AddGradingScales` (4), `AddTermStructure` (5, only if a column is needed; settings otherwise), `AddGradebook` (7), `AddGradeSnapshots` (11), `AddPeriods` (13), `AddTimetable` (14), `AddTimetableExceptions` (15), `AddAttendance` (16), `AddLiveAttendanceMappings` (18), `AddAbsenceExcuses` (19), `AddAttendanceAlerts` (20). Whoever merges second regenerates on top of `main`.
- **FE:** `IGradebookApi`, `IGradingScalesApi`, `ITimetableApi`, `IAttendanceApi`, `IExcusesApi`, each with HTTP implementation + fake (`Lms:UseFakeApi=true`) mimicking the rules the UI relies on (published-only, edit window by server clock, Block vs Warn, excuse state machine). `docs/DESIGN.md` + the M5 spec (Task 21) + the lms-ui skill; key prefixes `Grd_*`, `Scale_*`, `Tt_*`, `Att_*`, `Exc_*`, `Error_<code>`, `Notif_*`; files under ~300 lines.
- **E2E:** one stack at a time (`e2e.lock`, port 5132).

## Isolation bypass and system-context sites

**Bypass sites** read across the caller's scope with `IgnoreQueryFilters`, carry `// ISOLATION-BYPASS: <reason>` and an `IsolationBypassGuardTests` entry. **System-context sites** have no user: they resolve the organization from a trusted source and run inside `ISystemContext` for that organization only. Anything not listed is a plan change reviewed by QA.

| # | Site | Kind | What crosses / where the organization comes from | Task | Cross-client test |
|---|---|---|---|---|---|
| 1 | Scale and weight-template resolution from ancestor units | Bypass | name, kind, bands only, of scales/templates at ancestors of the caller's unit, same `OrganizationId` | 4 | `Ancestor_scales_are_read_only_within_the_client`; `Client_b_scales_never_resolve_for_client_a` |
| 2 | Term of a gradebook (term defined at the year's unit) | Bypass | `Term` id, dates, names; same `OrganizationId` | 7 | `Term_lookup_never_crosses_clients` |
| 3 | Teacher's own timetable across classes/schools | Bypass | slots where `TeacherUserId` = caller (or substitute exceptions for the caller), same `OrganizationId` | 14, 15 | `Teacher_timetable_returns_only_own_slots_in_own_client`; probe rows: teacher of another client, teacher of a sibling school |
| 4 | `AttendanceSessionGenerator` (recurring + on publish) | System context | platform list of organizations with published slots (allow-listed list query), each run in its own context; every created row carries that organization | 16 | `Generator_for_client_a_leaves_client_b_untouched`; `Generator_never_writes_in_platform_context` |
| 5 | `ScheduledPublishJob` | System context | per organization, flips due items | 11 | `Scheduled_publish_runs_each_client_in_its_own_context` |
| 6 | Live attendance job + reconcile sweep | System context | organization id from the sink payload (re-checked against the `LiveSession` row under that context); sweep loops organizations | 18 | `Live_attendance_job_with_a_foreign_org_payload_does_nothing`; `Reconcile_sweep_runs_each_client_in_its_own_context` |
| 7 | `AttendanceThresholdJob` | System context | per organization | 20 | `Threshold_job_counts_only_own_client_records` |
| 8 | `ExcuseAttachmentRetentionJob` | System context | per organization | 19 | `Retention_job_purges_only_own_client_files` |
| 9 | Seed provisioning of Saudi presets (reconciler) | System context | per organization at provisioning / seed-version bump | 4 | `Preset_reconciler_never_touches_edited_copies_or_other_clients` |

## Route, permission and relationship table (Task 40 tests every row)

"Rel" = relationship: Teacher only own ClassCourses with an assignment active today (L15); Supervisor own supervised courses; Student own; Guardian own active children; Counselor/Manager by unit subtree. A relationship miss is **404**; a missing permission is **403**. Exact routes are fixed by the wave contracts; this is the oracle.

| Route family | Permission | Rel / extra | Wrong caller |
|---|---|---|---|
| `GET/POST/PUT/DELETE /api/org-units/{id}/grading-scales[/{sid}]`, `…/{sid}/confirm`, `…/{sid}/default`, `…/presets` | `Grades.ConfigureScales` (reads: `Grades.View`) | `UnitAccess`; bypass site 1 for ancestors | teacher 403; other school admin 404 |
| `…/org-units/{id}/weight-templates[/{tid}]` | `Grades.ConfigureScales` | `UnitAccess` | teacher 403 |
| `GET /api/class-courses/{id}/gradebook?termId=&page=` | `Grades.Manage` or `Grades.View` | Rel; backfill only with `Manage`, never in a closed year | student/guardian/counselor 403; other teacher 404 |
| `PUT …/gradebook/config`, `…/categories`, `…/items[/{iid}]`, `…/entries` | `Grades.Manage` | Rel; `PlacementGuard`; `IGradebookWriter` (lock); concurrency | supervisor 403; other teacher 404 |
| `POST …/gradebook/items/publish` (ids, mode, at, acknowledgeProvisionalScale) | `Grades.Publish` | Rel; L7 | student 403; other teacher 404 |
| `GET …/gradebook/students/{sid}/student-view` | `Grades.Manage` | Rel; student enrolled | student 403 |
| `GET …/gradebook/items/{iid}/export`, `POST …/import/preview`, `POST …/import/apply` | `Grades.Manage` | Rel; L9 | other teacher 404 |
| `GET /api/me/grades?termId=` | `Grades.ViewOwn` | caller is a student; published only | guardian/staff 403 |
| `GET /api/students/{sid}/grades?termId=` | `Grades.ViewOwn` | active guardian link; published only | other guardian 404 |
| `GET /api/grade-reports/{student|class|course}` + `/export` | `Grades.ViewReports` | Supervisor courses; Manager unit scope | teacher 403; counselor 403 |
| `PUT /api/academic-years/{id}/term-structure` | `AcademicYears.Manage` (existing) | Planned only; L8 | 409 `terms_have_dependants` |
| `…/org-units/{id}/periods[/{pid}]` | `Timetable.Configure` | `UnitAccess` | teacher 403 |
| `GET/PUT /api/classes/{id}/timetable?termId=`, `…/slots[/{sid}]`, `…/validate`, `…/publish`, `…/copy-term`, `…/export` | `Timetable.Manage` (GET: `Timetable.View`) | `UnitAccess`; drafts only with `Manage`; concurrency | teacher write 403; other school 404 |
| `POST/PUT/DELETE /api/timetable-slots/{id}/exceptions[/{eid}]` | `Timetable.Manage` | `UnitAccess` | teacher 403 |
| `GET /api/me/timetable?from=&to=` | `Timetable.ViewOwn` | own slots (teacher, bypass 3) or own class (student) | — |
| `GET /api/students/{sid}/timetable` | `Timetable.ViewOwn` | guardian link | other guardian 404 |
| `GET /api/attendance/sessions?date=`; `POST /api/class-courses/{id}/attendance-sessions` (ad hoc) | `Attendance.Take` | teacher of record or substitute (`IsSubstituteOn`) | other teacher 404; counselor POST 403 |
| `GET/PUT /api/attendance-sessions/{id}/records` | `Attendance.Take` (inside window) / `Attendance.Edit` (after, reason required) | Rel or `IsSubstituteOn`; future session 409 | substitute on another session 404; teacher after window 409 `attendance_edit_window_closed` |
| `GET /api/attendance-sessions/{id}/history` | `Attendance.Take` or `Attendance.Edit` or `Attendance.View` | Rel / subtree | student 403 |
| `POST /api/attendance-sessions/{id}/confirm`, `PUT …/live-mappings` | `Attendance.Take` | Rel; `Source = Live` | other teacher 404 |
| `GET /api/attendance-reports/{student|class}` + `/export` | `Attendance.View` | Supervisor courses; Counselor/Manager subtree | teacher 403 |
| `GET /api/me/attendance`, `GET /api/students/{sid}/attendance` | `Attendance.ViewOwn` | own / guardian link; returns `canSubmitExcuse` | other guardian 404 |
| `GET /api/students/{sid}/excuse-policy`, `…/excusable-sessions?from=&to=`; `POST /api/excuses`; `GET /api/me/excuses`; `POST /api/excuses/{id}/withdraw`; `GET /api/excuses/{id}/attachment-url` | `Excuses.Submit` | guardian link or self + `Excuses.Submitters`; policy | student under `GuardiansOnly` 403 `excuse_submitter_not_allowed`; other guardian 404 |
| `GET /api/excuses/review`, `POST /api/excuses/{id}/approve|reject|escalate`, `GET /api/excuses/{id}/escalation-targets` | `Excuses.Review` (escalate: `Excuses.Escalate`) | subtree; reviewer ≠ submitter | teacher 403; other school counselor 404 |
| `GET /api/attendance/alerts` | `Excuses.Review` or `Attendance.View` (Counselor) | subtree | teacher 403 |
| Settings `Grades.*`, `Timetable.*`, `Attendance.*`, `Excuses.*`, `Org.TimeZone`, `AcademicYears.TermsPerYear` | `Settings.Update` | `UnitAccess` (client-level keys only at the root) | teacher 403 |

**Role matrix (wave contracts; `BuiltInRoleDefaults` 8 → 9 → 10):** OrgAdmin + Manager all M5 permissions; Supervisor `Grades.View`, `Grades.ViewReports`, `Timetable.View`, `Attendance.View`; Teacher `Grades.View`, `Grades.Manage`, `Grades.Publish`, `Timetable.View`, `Timetable.ViewOwn`, `Attendance.Take` (own class courses); Counselor `Attendance.View`, `Attendance.Edit`, `Excuses.Review`, `Excuses.Escalate`, `Timetable.View`; Student `Grades.ViewOwn`, `Timetable.ViewOwn`, `Attendance.ViewOwn`, `Excuses.Submit` (effective only under `GuardiansAndStudents`); Guardian `Grades.ViewOwn`, `Timetable.ViewOwn`, `Attendance.ViewOwn`, `Excuses.Submit`.

## Review Focus
1. **Grade leakage:** no unpublished score, total, title, count, marker or rank reaches a student or guardian through any route, notification, export, error or audit detail (Tasks 9, 11, 29, 40, 41).
2. **One engine:** grid, student view, reports and exports agree; the client never computes (Tasks 6, 8, 9, 24).
3. **Relationship and dates:** former teachers, substitutes outside their date, other-school counselors, guardians of other children, supervisors outside their courses all get 404 (Tasks 15, 17, 19, 40).
4. **Jobs per client:** generation, publish, live attendance, alerts and retention run per organization and never in a platform context (Tasks 11, 16, 18, 19, 20).
5. **Local days:** every "today", window and deadline goes through `IClientCalendar` (Tasks 13, 16, 17, 19, 20).
6. **Attachments and spreadsheets:** L9 and L10 hold end to end (Tasks 10, 19, 41).

---

### Task 1: Contract — wave 1: grading scales, gradebook, publishing, grade reads, time zone — lane: Lead contract

**PRD:** FR-GRD-01..05, D58, D59. **Depends on:** M4b wave contracts merged. Opened on day 1 together with Task 2; merges before Tasks 4–12 and 22–30.

**Files:**
- Modify: `src/Lms.Shared/Authorization/Permissions.cs`, `BuiltInRoles.cs`, `RelationshipRules.cs`, `src/Lms.Shared/ProblemCodes.cs`, `src/Lms.Shared/Auditing/AuditCatalog.cs`, `src/Lms.Shared/Notifications/NotificationEvents.cs`, `src/Lms.Shared/Configuration/SettingKeys.cs`, `SettingCatalog.cs`, `src/Lms.Shared/PageRoutes.cs`, `src/Lms.Infrastructure/Access/BuiltInRoleDefaults.cs` (version 8), `src/Lms.Client/Resources/SharedResource.{ar,en}.resx`
- Create: `src/Lms.Shared/Grading/GradingCatalogs.cs`, `GradingScaleDtos.cs`, `GradebookDtos.cs`, `GradeReadDtos.cs`, `GradeReportDtos.cs`, `src/Lms.Shared/Routes.Grading.cs`; `src/Lms.Shared/AcademicYears/TermStructureDtos.cs`
- Test: `tests/Lms.UnitTests/Contracts/GradingContractTests.cs` (new), `BuiltInRoleMatrixTests.cs`, `AuthorizationContractTests.cs`, `tests/Lms.UnitTests/Access/BuiltInRoleDefaultsTests.cs`, `tests/Lms.IntegrationTests/Access/BuiltInRoleReconcilerTests.cs`

**Interfaces:**
- Permissions `Grades.{View, Manage, Publish, ConfigureScales, ViewReports, ViewOwn}`; matrix per the role table; `RelationshipRules` for teacher-assignment dates (L15) and supervisor courses.
- Catalogs: `ScaleKinds {Percentage, Letter, Gpa}`, `PublishStates {Hidden, Published, Scheduled}`, `GradeCellStates {Event, Manual, Override, NewerScoreExists, Excused, Empty}`, `MissingPolicies {Normalise, Zero, HideUntilComplete}`, `CategoryMethods {PointsWeighted, PercentAverage}`, `RoundingModes {HalfUp}`, `GradebookStates {Open, Locked}`, `PublishModes {Now, Schedule, Hide}`, `GradingLimits` (decimal places 0–4, weights 0–100, max points 0.01–1000, reason 1–500).
- DTOs (contract needs 1–6 of #616 §5): `GradingScaleDto` (+ `Provisional`, `SeedKey`, `SeedVersion`, `UpdateAvailable`, `InheritedFromUnitId`), `GradingScaleBandDto`, `WeightTemplateDto`, `GradebookDto {Config, Categories, Items, Rows (paged), IsValid, WeightsTotal, State, ComputedAtUtc, ServerNowUtc}`, `GradeCellDto {ItemId, Points?, State, Reason?}`, `GradeRowDto {Student, Cells, CategoryResults, Final?, Band?, Version}`, `UpsertEntriesRequest` (with `Version`) → `GradeRowDto`, `PublishItemsRequest {ItemIds, Mode, AtUtc?, AcknowledgeProvisionalScale}` → `PublishResultDto {AffectedStudents, NotificationsQueued}`, `StudentGradesDto` (published only, per course, band, `BandLegendDto`), `GradeImportPreviewDto {Token, Rows, UnknownCodes, OutOfRange, Overwrites, Errors}`, `GradeReportDto` (+ `ComputedAtUtc`), `TermStructureDto {TermsPerYear, TermNames}`.
- ProblemCodes: `gradebook_locked`, `scale_provisional`, `scale_bands_invalid`, `scale_in_use`, `weights_invalid`, `grade_out_of_range`, `grade_override_reason_required`, `grade_import_invalid`, `grade_import_expired`, `terms_have_dependants`, `concurrency_conflict`.
- Settings: `Org.TimeZone` (IANA, client level), `Grades.MissingPolicy` (Normalise), `Grades.CategoryMethod` (PointsWeighted), `Grades.DecimalPlaces` (2), `AcademicYears.TermsPerYear` (3). Events `Grades.Published` (params Course). AuditCatalog: `GradingScale`, `WeightTemplate`, `Gradebook`, `GradeCategory`, `GradeItem`, `GradeEntry`, `GradeSnapshot`. ar/en labels for every code, event, setting and catalog value.
- Route permissions as XML doc comments on `Routes.Grading` constants (route table rows above).

- [ ] **Step 1: Write the failing tests** `Grading_permissions_follow_the_matrix`, `Defaults_version_is_8`, `Every_grading_code_event_and_setting_has_ar_and_en_labels`, `Every_grading_route_has_a_permission_doc_comment`, `Grade_notification_texts_have_no_value_placeholders`. **Step 2–4** FAIL → implement → PASS; 0 warnings. **Step 5: Commit** `feat(contracts): M5 wave 1 - grading, gradebook and publishing`.

**Acceptance:** contract tests green; QA approves the matrix; no route without a DTO and a route-table row.

### Task 2: Contract — wave 2: periods, timetable, exceptions — lane: Lead contract

**PRD:** FR-TT-01..05, D60. **Depends on:** Task 1 opened (same day; merges after it for the defaults version). Merges before Tasks 13–15 and 31–32.

**Files:** Modify the Task 1 set (`BuiltInRoleDefaults` version 9); Create `src/Lms.Shared/Timetable/TimetableCatalogs.cs`, `PeriodDtos.cs`, `TimetableDtos.cs`, `src/Lms.Shared/Routes.Timetable.cs`; test `tests/Lms.UnitTests/Contracts/TimetableContractTests.cs`.

**Interfaces:** Permissions `Timetable.{View, Manage, Configure, ViewOwn}`; `RelationshipRules.IsSubstituteOn` (L15). Catalogs `SlotStates {Draft, Published}`, `ConflictModes {Block, Warn}`, `ConflictKinds {TeacherDoubleBooked, ClassDoubleBooked}`, `ExceptionKinds {Substitute, Cancelled, Moved}`, `TimetableLimits` (room 0–100 chars, note 0–500). DTOs per #616 §5 item 7: `PeriodDto`, `SlotDto {…, RoomName?, State, Version, Badges}`, `SlotConflictDto {Kind, SlotId, CourseName, TeacherName}`, `SaveSlotResultDto {Slot, Warnings}`, `CopyTermResultDto {Copied, Skipped[{Reason, …}]}`, `TimetableExceptionDto`, `TimetableViewDto {From, To, Days, Periods, Cards}`. ProblemCodes `timetable_conflict` (with clashes), `period_overlap`, `timetable_not_published`, `exception_date_invalid`. Settings `Timetable.WorkingDays` (Sun–Thu default), `Timetable.ConflictMode` (Block). AuditCatalog `Period`, `TimetableSlot`, `TimetableException`.

- [ ] **Steps 1–5** as Task 1 (`Timetable_permissions_follow_the_matrix`, `Defaults_version_is_9`, label parity, route doc comments). **Commit** `feat(contracts): M5 wave 2 - periods, timetable and exceptions`.

**Acceptance:** as Task 1; the room is a plain optional string (D60), no room conflict kind.

### Task 3: Contract — wave 3: attendance, live proposal, excuses, alerts — lane: Lead contract

**PRD:** FR-TT-06, FR-ATT-01, FR-EXC-01..04, D58, D61. **Depends on:** Task 2 merged; #544 sink shape merged. Merges before Tasks 16–20 and 33–38 (target end of week 1).

**Files:** Modify the Task 1 set (`BuiltInRoleDefaults` version 10); Create `src/Lms.Shared/Attendance/AttendanceCatalogs.cs`, `AttendanceDtos.cs`, `src/Lms.Shared/Excuses/ExcuseCatalogs.cs`, `ExcuseDtos.cs`, `src/Lms.Shared/Routes.Attendance.cs`, `Routes.Excuses.cs`; test `tests/Lms.UnitTests/Contracts/AttendanceContractTests.cs`, `ExcuseContractTests.cs`.

**Interfaces:** Permissions `Attendance.{Take, Edit, View, ViewOwn}`, `Excuses.{Submit, Review, Escalate}`. Catalogs `AttendanceStatuses {Present, Absent, Late, Excused}`, `AttendanceSources {Timetable, AdHoc, Live}`, `AttendanceSessionStates {Open, Taken}`, `ExcuseStatuses {Submitted, Approved, Rejected, Escalated, Withdrawn}`, `ExcuseReasons {Illness, Family, Official, Other}`, `ExcuseSubmitters {GuardiansOnly, GuardiansAndStudents}`. DTOs per #616 §5 items 8–10: `AttendanceSessionListItemDto {State, Source, MarkedCount, RosterCount, EditableUntilUtc, IsSubstitute, Version, ServerNowUtc}`, `TakeAttendanceRequest {DefaultStatus?, Records[{StudentId, Status, Note?}], Reason?, Version}`, `AttendanceRecordDto` (+ live fields `JoinedMinutes?`, `JoinedAtUtc?`, `Proposed`), `UnmatchedParticipantDto`, `LiveMappingRequest`, `AttendanceChangeDto`, `AttendanceSummaryDto`, `OwnAttendanceDto` (+ `CanSubmitExcuse`), `ExcusePolicyDto`, `ExcusableSessionDto`, `SubmitExcuseRequest`, `ExcuseDto` (+ history), `ExcuseReviewItemDto` (+ recent absences), `EscalationTargetDto`, `AttendanceAlertDto {Absences, Lates, WindowDays, Score, AlertedAtUtc}`. ProblemCodes `attendance_edit_window_closed`, `attendance_session_future`, `attendance_unmarked_rows`, `attendance_reason_required`, `excuse_out_of_policy` (with reason), `excuse_submitter_not_allowed`, `excuse_reviewer_is_submitter`, `excuse_comment_required`, `excuse_attachment_invalid`, `excuse_not_pending`. Settings `Attendance.GenerateDaysAhead` (7), `Attendance.EditWindowHours` (48), `Attendance.LiveMinPercent` (50), `Attendance.LateAfterMinutes` (10), `Attendance.AbsentThreshold` (5), `Attendance.ThresholdWindowDays` (30), `Attendance.LateWeightPercent` (50), `Excuses.Submitters` (GuardiansOnly), `Excuses.MaxDaysAfterAbsence` (5), `Excuses.AllowedReasons`, `Excuses.AttachmentRequiredReasons`, `Excuses.MaxAttachmentMb` (5), `Excuses.AttachmentRetentionDays` (365). Events `Attendance.Absent`, `Excuses.Decided`, `Excuses.Escalated`, `Attendance.ThresholdCrossed`. AuditCatalog `AttendanceRecordChange`, `AbsenceExcuse`.

- [ ] **Steps 1–5** as Task 1 (`Defaults_version_is_10`, matrix incl. `Counselor_has_no_grade_permissions`). **Commit** `feat(contracts): M5 wave 3 - attendance, excuses and alerts`.

**Acceptance:** as Task 1; QA confirms the counselor and guardian rows.

---

### Task 4: Grading scales, bands, weight templates, Saudi preset seed — lane: BE1 · size M

**PRD:** FR-GRD-02, FR-GRD-03, D58 (Q3), D59. **Depends on:** Task 1.

**Files:**
- Create: `src/Lms.Domain/Grading/GradingScale.cs`, `GradingScaleBand.cs`, `WeightTemplate.cs`, `WeightTemplateItem.cs`, `ScaleBandValidator.cs`; `src/Lms.Infrastructure/Grading/Scales/GradingScaleService.cs`, `ScaleResolver.cs` (bypass site 1), `SaudiPresetSeed.cs` (seed data with `SeedKey`/`SeedVersion` and `NeedsVerification` notes), `PresetReconciler.cs` (site 9), configurations, migration `AddGradingScales`; `src/Lms.Api/Features/GradingScales/GradingScalesModule.cs`, `GradingScaleEndpoints.cs`, `WeightTemplateEndpoints.cs`, `GradingScaleValidators.cs`
- Test: `tests/Lms.UnitTests/Grading/ScaleBandValidatorTests.cs`; `tests/Lms.IntegrationTests/Grading/GradingScaleTests.cs`, `PresetSeedTests.cs`, `GradingScaleIsolationTests.cs`; `Security/ProbeRows/GradingScaleProbeRows.cs`

**Interfaces:** CRUD per route table; bands gap-free and non-overlapping over 0–100 (`scale_bands_invalid` with the gaps); one default per client; ancestors' scales visible (site 1); copy-on-use for templates; L7 provisioning, `confirm`, `UpdateAvailable`; editing a scale used by a gradebook keeps existing gradebooks on their own copy (`scale_in_use` on delete); weight templates with items summing to 100 (warning, not refusal, as categories).

- [ ] **Step 1: Write the failing tests** `Bands_must_cover_0_to_100_without_gaps_or_overlaps`; `One_default_per_client`; `Unit_sees_ancestor_scales_but_not_sibling_scales`; `Ancestor_scales_are_read_only_within_the_client`; `Client_b_scales_never_resolve_for_client_a`; `Provisioning_creates_a_plain_default_and_three_provisional_templates`; `Saudi_templates_are_never_the_default`; `Copy_of_a_template_stays_provisional_until_confirmed_or_edited`; `Seed_version_bump_marks_unedited_copies_update_available`; `Seed_version_bump_never_touches_edited_copies`; `Preset_reconciler_never_touches_edited_copies_or_other_clients`; `Scale_in_use_cannot_be_deleted`; `Weight_template_apply_copies_values`; `Teacher_cannot_configure_scales`; isolation tests for `GradingScale`, `GradingScaleBand`, `WeightTemplate`, `WeightTemplateItem`. **Step 2–4** FAIL → implement → PASS. **Step 5: Commit** `feat(grading): grading scales, weight templates and Saudi presets`.

**Acceptance:** tests green; probe rows; seed numbers only in `SaudiPresetSeed.cs` with a `NeedsVerification` comment per scale referencing Q21–Q23.

### Task 5: Academic-year term structure (two or three terms) — lane: BE2 · size S

**PRD:** D59, FR-YEAR (existing). **Depends on:** Task 1.

**Files:** Modify `src/Lms.Domain/AcademicYears/AcademicYear.cs` (structure change guard), `src/Lms.Api/Features/AcademicYears/AcademicYearEndpoints.cs`; Create `src/Lms.Infrastructure/AcademicYears/TermTemplate.cs`, `TermStructureService.cs`, `ITermDependants.cs` (implemented by gradebook, timetable and attendance modules as they land; until then returns false). Test `tests/Lms.IntegrationTests/AcademicYears/TermStructureTests.cs`.

**Interfaces:** L8. `GET` returns `TermStructureDto` with the template names (ar/en) for 2 and 3 terms; `PUT …/term-structure` creates missing terms from the template on a Planned year, never deletes or renames existing ones; 409 `terms_have_dependants`.

- [ ] **Step 1: Tests** `New_year_uses_terms_per_year_setting`; `Switching_structure_on_a_planned_year_adds_template_terms`; `Switching_never_deletes_or_renames_existing_terms`; `Switching_with_dependants_is_409`; `Switching_an_active_year_is_refused`; `Template_names_have_ar_and_en`. **Step 2–5** → commit `feat(years): two or three terms per year with a term template`.

**Acceptance:** tests green; probe row for the new route.

### Task 6: Gradebook calculation engine — lane: BE2 · size M

**PRD:** FR-GRD-01, FR-GRD-03, D58 (Q6, Q7). **Depends on:** Task 1. Runs in parallel with Task 7 (pure domain, no entities).

**Files:** Create `src/Lms.Domain/Grading/Calculation/GradebookCalculator.cs`, `CalculationInput.cs` (records: items, scores, entries, categories, scale bands, rules), `CalculationResult.cs`, `Rounding.cs`. Test `tests/Lms.UnitTests/Grading/GradebookCalculatorTests.cs`, `GradebookCalculatorPropertyTests.cs`, `GradebookGoldenFileTests.cs` + `tests/Lms.UnitTests/Grading/Golden/*.json` (hand-computed gradebooks).

**Interfaces:** #611 §4.1: item score = latest revision unless override; excused drops the item; category = points-weighted or percent average; final = weighted sum normalised over graded categories (or Zero / HideUntilComplete); half-up rounding once at the end; band from the scale; `publishedOnly` flag filters items before computing (used by L6); invalid weights compute with `IsValid = false`; unknown `GradeSourceKind` values are accepted.

- [ ] **Step 1: Tests** `Weights_summing_to_100_give_a_result_within_0_and_100`; `Permuting_items_never_changes_the_result`; `A_regrade_revision_replaces_its_predecessor`; `Override_wins_over_the_event`; `Excused_items_never_lower_the_result`; `Missing_policy_normalise_zero_and_hide`; `Category_method_points_weighted_vs_percent_average`; `Rounding_is_half_up_once_at_the_end`; `Published_only_ignores_hidden_and_scheduled_items`; `Invalid_weights_compute_with_a_warning`; `Unknown_source_kind_does_not_throw`; `Band_lookup_uses_min_percent_inclusive`; golden files (3 gradebooks incl. one Saudi school band and one 5-point GPA). **Step 2–5** → commit `feat(grading): gradebook calculation engine`.

**Acceptance:** property and golden tests green; no I/O in the engine (architecture test).

### Task 7: Gradebook model, writer, auto items — lane: BE1 · size M

**PRD:** FR-GRD-01, FR-GRD-03. **Depends on:** Task 4 (scales). Uses Task 6's input records once merged (stub until then).

**Files:** Create `src/Lms.Domain/Grading/Gradebook.cs`, `GradeCategory.cs`, `GradeItem.cs`, `GradeEntry.cs`, `IGradebookWriter.cs`, `IGradeEventObserver.cs`; `src/Lms.Infrastructure/Grading/Gradebooks/GradebookWriter.cs`, `GradebookItemsObserver.cs`, `GradebookBackfill.cs`, `EventTermResolver.cs` (D58 Q8; bypass site 2), configurations, migration `AddGradebook`; Modify `src/Lms.Infrastructure/Grading/GradeEventWriter.cs` (calls observers in the same transaction; reviewed M4a change). Test `tests/Lms.IntegrationTests/Grading/GradebookModelTests.cs`, `AutoItemTests.cs`, `GradebookIsolationTests.cs`.

**Interfaces:** gradebook unique per `(ClassCourse, Term)`, created on first teacher use or first event; L3, L4; `IGradebookWriter` the only write path (checks `PlacementGuard`, `State`, row version); `EventTermResolver` per D58 Q8; `Uncategorised` category weight 0; `ITermDependants` implementation.

- [ ] **Step 1: Tests** `Event_creates_its_item_in_the_same_transaction`; `Auto_item_defaults_to_hidden`; `Auto_item_goes_to_uncategorised`; `Backfill_runs_only_for_manage_callers`; `Backfill_never_runs_in_a_closed_year`; `Concurrent_backfill_is_idempotent_on_unique_violation`; `Student_read_never_creates_rows`; `Event_after_term_end_goes_to_the_current_term`; `Term_lookup_never_crosses_clients`; `Writer_refuses_closed_class`; `Unknown_source_kind_creates_an_item_with_a_generic_title`; isolation tests for `Gradebook`, `GradeCategory`, `GradeItem`, `GradeEntry`. **Step 2–5** → commit `feat(grading): gradebook model, writer and auto items`.

**Acceptance:** tests green; M4a grade-event tests still green.

### Task 8: Gradebook grid, configuration, categories, items, entries — lane: BE1 · size L

**PRD:** FR-GRD-01, FR-GRD-03, FR-GRD-04. **Depends on:** Tasks 6, 7.

**Files:** Create `src/Lms.Api/Features/Gradebook/GradebookModule.cs`, `GradebookEndpoints.cs`, `GradebookConfigEndpoints.cs`, `GradeEntryEndpoints.cs`, `GradebookValidators.cs`; `src/Lms.Infrastructure/Grading/Gradebooks/GradebookQuery.cs` (paged, L17), `GradeRowBuilder.cs`. Test `tests/Lms.IntegrationTests/Grading/GradebookGridTests.cs`, `GradeEntryTests.cs`, `GradebookPerformanceTests.cs`; `Security/ProbeRows/GradebookProbeRows.cs`.

**Interfaces:** grid GET (staff, all items, L18 fields, `NewerScoreExists` marker staff only); config PUT (scale from visible scales, D58 Q3; decimals; method; missing policy); categories PUT (weights, order; `weights_invalid` only for out-of-range values); items CRUD (manual items, move category, delete manual only); entries PUT (manual score, override with required reason, excuse from item, clear override; returns recomputed rows; L12); "keep or drop override" action on a newer score; teacher assignment dates (L15).

- [ ] **Step 1: Tests** `Grid_returns_server_computed_cells_and_validity`; `Grid_pages_students_by_50`; `Entry_write_returns_the_recomputed_row`; `Override_requires_a_reason_and_is_audited`; `Regrade_after_override_shows_newer_score_exists`; `Keep_or_drop_override`; `Out_of_range_points_are_422`; `Stale_version_is_409_concurrency_conflict`; `Event_scores_are_read_only`; `Former_teacher_gets_404`; `Supervisor_can_read_but_not_write`; `Other_teacher_gets_404`; `Grid_of_40_students_and_60_items_computes_under_budget`. **Step 2–5** → commit `feat(grading): gradebook grid and editing`.

**Acceptance:** tests green; probe rows for every gradebook route.

### Task 9: Published-only reads, student and guardian grades, staff preview, reports — lane: BE2 · size M

**PRD:** FR-GRD-04, FR-GRD-05, D58 (Q2, Q18). **Depends on:** Tasks 6, 7.

**Files:** Create `src/Lms.Infrastructure/Grading/Reads/PublishedGradeReads.cs` (L6), `BandLegendReader.cs`, `GradeReportService.cs`; `src/Lms.Api/Features/Gradebook/StudentGradeEndpoints.cs`, `StudentViewPreviewEndpoint.cs`, `GradeReportEndpoints.cs`. Test `tests/Lms.IntegrationTests/Grading/PublishedGradeReadsTests.cs`, `GradeLeakTests.cs`, `GradeReportTests.cs`.

**Interfaces:** `/api/me/grades`, `/api/students/{sid}/grades` (active guardian link, 404 on a miss), per-course totals and band from published items only, band legend; no hidden item titles or counts; staff preview through the same helper; reports per student/class/course with `ComputedAtUtc`, Supervisor courses and Manager scope; architecture test: student/guardian endpoints reference only `PublishedGradeReads`.

- [ ] **Step 1: Tests** `Student_sees_only_published_items_and_totals_from_them`; `Hidden_item_title_never_appears_in_any_student_response`; `Scheduled_item_is_hidden_until_due`; `Guardian_sees_only_own_childs_published_grades`; `Guardian_of_two_children_cannot_read_one_under_the_other_id`; `Inactive_guardian_link_is_404`; `Newer_score_marker_never_reaches_students`; `Gpa_shows_band_points_per_course_only`; `Band_legend_is_returned_in_both_languages`; `Staff_preview_equals_the_student_view`; `Student_cannot_call_the_staff_preview`; `Supervisor_reports_only_own_courses`; `Counselor_cannot_read_grades`; `Student_and_guardian_endpoints_use_only_the_published_helper` (architecture). **Step 2–5** → commit `feat(grading): published-only grade reads and reports`.

**Acceptance:** tests green; probe rows; leak tests listed in `docs/security/m5-review.md` (Task 41).

### Task 10: Column import and export, report Excel — lane: BE2 · size M

**PRD:** FR-GRD-04. **Depends on:** Tasks 8, 9.

**Files:** Create `src/Lms.Infrastructure/Spreadsheets/SpreadsheetCell.cs` (moved escaping from `AuditExporter`, which then calls it), `src/Lms.Infrastructure/Grading/Exchange/GradeColumnExporter.cs`, `GradeColumnImporter.cs`, `ImportPreviewToken.cs`, `GradeReportExcel.cs` (with the "Scale" sheet, L27); `src/Lms.Api/Features/Gradebook/GradeExchangeEndpoints.cs`. Test `tests/Lms.UnitTests/Spreadsheets/SpreadsheetCellTests.cs`; `tests/Lms.IntegrationTests/Grading/GradeExchangeTests.cs`.

**Interfaces:** L9 in full; export of one item column keyed by student number; preview (counts, unknown codes, out of range, overwrites, per-row errors) and apply (re-validated, audited); event-derived scores never touched.

- [ ] **Step 1: Tests** `Export_cells_escape_formula_injection`; `Audit_export_still_escapes_after_the_move`; `Import_accepts_xlsx_only_and_refuses_macro_files`; `Import_respects_size_row_column_and_time_limits`; `Import_ignores_external_links`; `Preview_token_is_bound_to_uploader_gradebook_item_and_hash`; `Expired_or_foreign_preview_token_is_refused`; `Apply_revalidates_against_current_data`; `Import_never_touches_event_scores`; `Import_and_export_are_audited`; `Report_excel_has_the_scale_sheet`. **Step 2–5** → commit `feat(grading): grade column import and export`.

**Acceptance:** tests green; QA3 security review of this PR.

### Task 11: Publishing, scheduled publish job, snapshots, notifications — lane: BE1 · size M

**PRD:** FR-GRD-05, D58, D59. **Depends on:** Task 8, Task 9.

**Files:** Create `src/Lms.Domain/Grading/GradeSnapshot.cs`; `src/Lms.Infrastructure/Grading/Publishing/GradePublisher.cs`, `ScheduledPublishJob.cs` (site 5), `GradePublishedNotifier.cs`; migration `AddGradeSnapshots`; `src/Lms.Api/Features/Gradebook/PublishEndpoints.cs`. Test `tests/Lms.IntegrationTests/Grading/PublishingTests.cs`, `ScheduledPublishTests.cs`, `GradeSnapshotIsolationTests.cs`.

**Interfaces:** `PublishItemsRequest` (now, schedule in the future by server clock, hide); L7 provisional acknowledgement; un-publish allowed and audited; result counts; snapshot `Kind = Publish` per student per batch; one `Grades.Published` notification per student (and linked guardians) per batch, course name only; job every minute, idempotent, per client.

- [ ] **Step 1: Tests** `Publish_now_makes_items_visible_to_students`; `Schedule_in_the_past_is_refused`; `Scheduled_item_is_published_by_the_job_once`; `Scheduled_publish_runs_each_client_in_its_own_context`; `Publish_under_a_provisional_scale_is_409_without_acknowledgement`; `Acknowledged_provisional_publish_is_audited`; `Unpublish_hides_again_and_is_audited`; `One_notification_per_student_per_batch`; `Notification_contains_no_grade_value_or_item_title`; `Publish_writes_snapshots`; isolation test for `GradeSnapshot`. **Step 2–5** → commit `feat(grading): grade publishing, schedule and notifications`.

**Acceptance:** tests green; probe rows; `Grades.Published` delivered in ar and en.

### Task 12: Lock seam verification — lane: BE2 · size S

**PRD:** FR-GRD-06 (seam only), D58. **Depends on:** Tasks 8, 11.

**Files:** Modify `GradebookWriter.cs`, `GradebookQuery.cs`, `PublishedGradeReads.cs` (lock-aware reads, L5); Create `tests/Lms.IntegrationTests/Grading/GradebookLockSeamTests.cs` (test-only lock helper in `tests/…/Grading/TestLock.cs`).

- [ ] **Step 1: Tests** `Locked_gradebook_refuses_every_write_with_gradebook_locked`; `Locked_reads_return_the_lock_snapshot`; `Event_recorded_after_lock_does_not_change_locked_results`; `Events_after_lock_are_counted_for_staff_only`; `Import_and_publish_refuse_when_locked`. **Step 2–5** → commit `test(grading): lock seam for M7`.

**Acceptance:** tests green; no lock UI or endpoint (M7).

### Task 13: Periods, working days, client time zone, client calendar — lane: BE3 · size M

**PRD:** FR-TT-01, D58 (Q4, Q10). **Depends on:** Task 2 (and `Org.TimeZone` from Task 1).

**Files:** Create `src/Lms.Domain/Timetable/Period.cs`, `src/Lms.Domain/Calendar/IClientCalendar.cs`; `src/Lms.Infrastructure/Calendar/ClientCalendar.cs`, `src/Lms.Infrastructure/Timetable/PeriodService.cs`, configurations, migration `AddPeriods`; `src/Lms.Api/Features/Timetable/TimetableModule.cs`, `PeriodEndpoints.cs`. Test `tests/Lms.UnitTests/Calendar/ClientCalendarTests.cs`; `tests/Lms.IntegrationTests/Timetable/PeriodTests.cs`, `PeriodIsolationTests.cs`; `Security/ProbeRows/PeriodProbeRows.cs`.

**Interfaces:** periods per unit with override by a lower unit (D58 Q4), `period_overlap`, times left in local wall time; `IClientCalendar` per L2 (`LocalToday(orgId)`, `LocalDate(orgId, instant)`, `IsWorkingDay(unitId, date)`, `IsHoliday(unitId, date)`, `AddSchoolDays`).

- [ ] **Step 1: Tests** `Periods_inherit_and_a_lower_unit_overrides`; `Overlapping_periods_are_refused`; `Local_today_uses_the_client_time_zone`; `Missing_client_time_zone_falls_back_to_jobs_time_zone`; `Working_days_come_from_the_unit_setting`; `Holidays_come_from_the_calendar`; `Dst_change_keeps_local_dates` (Europe/London); `Add_school_days_skips_weekends_and_holidays`; `Teacher_cannot_configure_periods`; isolation test for `Period`. **Step 2–5** → commit `feat(timetable): periods, working days and client calendar`.

**Acceptance:** tests green; probe rows; architecture test that only `ClientCalendar` converts instants to local school days in M5 modules.

### Task 14: Timetable slots, builder, conflicts, publish, views, export, copy-term — lane: BE3 · size L

**PRD:** FR-TT-02, FR-TT-03, FR-TT-04, D60. **Depends on:** Task 13.

**Files:** Create `src/Lms.Domain/Timetable/TimetableSlot.cs`, `ConflictChecker.cs`; `src/Lms.Infrastructure/Timetable/TimetableService.cs`, `TimetableViews.cs` (bypass site 3), `TimetablePublisher.cs` (queues generation through an interface implemented in Task 16), `TimetableExporter.cs` (uses `SpreadsheetCell`), `CopyTermService.cs`; migration `AddTimetable`; `src/Lms.Api/Features/Timetable/TimetableEndpoints.cs`, `TimetableViewEndpoints.cs`. Test `tests/Lms.UnitTests/Timetable/ConflictCheckerTests.cs`; `tests/Lms.IntegrationTests/Timetable/TimetableBuilderTests.cs`, `TimetableViewTests.cs`, `CopyTermTests.cs`, `TimetableIsolationTests.cs`; `Security/ProbeRows/TimetableProbeRows.cs`.

**Interfaces:** slots per route table; unique active `(ClassCourse, Term, Day, Period)`; teacher and class conflicts (no room check, D60), Block → 409 `timetable_conflict` with clashes, Warn → saved with warnings; single `PUT` move; draft/publish; views (class, own teacher, own student, guardian child) over a week range with exception badges once Task 15 lands; Excel export; copy-term into Draft with skipped list; `ITermDependants`.

- [ ] **Step 1: Tests** `Teacher_double_booking_blocks_in_block_mode`; `Class_double_booking_blocks_in_block_mode`; `Warn_mode_saves_and_returns_warnings`; `Room_text_is_never_checked_for_conflicts`; `Move_is_a_single_put_with_version`; `Drafts_are_visible_only_with_manage`; `Publish_queues_session_generation`; `Teacher_timetable_returns_only_own_slots_in_own_client`; `Guardian_sees_only_own_childs_class`; `Copy_term_creates_drafts_and_lists_skipped_rows`; `Export_escapes_cells`; `Other_school_admin_gets_404`; isolation test for `TimetableSlot`. **Step 2–5** → commit `feat(timetable): slots, builder, conflicts, views and copy`.

**Acceptance:** tests green; probe rows incl. bypass site 3 rows (teacher of another client, of a sibling school).

### Task 15: Timetable exceptions and substitutes — lane: BE4 · size M

**PRD:** FR-TT-05. **Depends on:** Task 14.

**Files:** Create `src/Lms.Domain/Timetable/TimetableException.cs`; `src/Lms.Infrastructure/Timetable/ExceptionService.cs`, `src/Lms.Infrastructure/Access/SubstituteRelationship.cs` (`IsSubstituteOn`); migration `AddTimetableExceptions`; `src/Lms.Api/Features/Timetable/ExceptionEndpoints.cs`. Test `tests/Lms.IntegrationTests/Timetable/ExceptionTests.cs`, `SubstituteAccessTests.cs`, `TimetableExceptionIsolationTests.cs`.

**Interfaces:** one exception per `(slot, date)`; date must be a working day within the term and the slot's weekday (`exception_date_invalid`); Substitute/Cancelled/Moved; base slot never changes; views apply exceptions; `IsSubstituteOn(sessionId)` per L15 (consumed by Task 17); substitute sees the slot in their own timetable for that date.

- [ ] **Step 1: Tests** `Exception_never_changes_the_base_slot`; `Exception_on_a_wrong_weekday_or_holiday_is_refused`; `Substitute_appears_in_own_timetable_for_that_date_only`; `Cancelled_and_moved_show_in_views`; `Substitute_rule_holds_only_on_the_exception_date`; `Substitute_cannot_reach_other_sessions_of_the_class`; isolation test for `TimetableException`. **Step 2–5** → commit `feat(timetable): dated exceptions and substitutes`.

**Acceptance:** tests green; probe rows.

### Task 16: Attendance session generation and ad hoc sessions — lane: BE4 · size M

**PRD:** FR-TT-06, FR-ATT-01, D58 (Q10). **Depends on:** Tasks 3, 14, 15.

**Files:** Create `src/Lms.Domain/Attendance/AttendanceSession.cs`, `AttendanceRecord.cs`, `AttendanceRecordChange.cs`; `src/Lms.Infrastructure/Attendance/AttendanceSessionGenerator.cs` (site 4), `AdHocSessionService.cs`, `AttendanceRoster.cs`, configurations, migration `AddAttendance`; `src/Lms.Api/Features/Attendance/AttendanceModule.cs`, `AttendanceSessionEndpoints.cs`. Test `tests/Lms.IntegrationTests/Attendance/SessionGenerationTests.cs`, `AdHocSessionTests.cs`, `AttendanceIsolationTests.cs`.

**Interfaces:** A6; daily recurring + on publish; `GenerateDaysAhead` from local today; skip non-working days and holidays; apply exceptions (cancelled → none, moved → new period, substitute recorded); unique `(SlotId, Date)`; backfill from today for late slots; never generate in the past; ad hoc session for a ClassCourse; roster = active enrollments on the date; the sessions list for a user/date incl. substitute sessions (`IsSubstitute`).

- [ ] **Step 1: Tests** `Generator_creates_sessions_for_the_next_days_only`; `Generator_skips_weekends_and_holidays`; `Generator_applies_exceptions`; `Generator_is_idempotent`; `Generator_for_client_a_leaves_client_b_untouched`; `Generator_never_writes_in_platform_context`; `Generator_uses_the_client_local_date_across_dst`; `Slot_published_late_backfills_from_today`; `Ad_hoc_session_for_own_class_course`; `Session_list_includes_substitute_sessions`; isolation tests for `AttendanceSession`, `AttendanceRecord`, `AttendanceRecordChange`. **Step 2–5** → commit `feat(attendance): session generation and ad hoc sessions`.

**Acceptance:** tests green; probe rows.

### Task 17: Taking attendance, edit window, history, summaries, own/guardian reads, absence notices — lane: BE3 · size L

**PRD:** FR-ATT-01, FR-EXC-01. **Depends on:** Task 16.

**Files:** Create `src/Lms.Infrastructure/Attendance/AttendanceTaker.cs`, `EditWindow.cs`, `AttendanceSummaries.cs`, `OwnAttendanceReads.cs`, `AbsenceNotifier.cs`; `src/Lms.Api/Features/Attendance/TakeAttendanceEndpoints.cs`, `AttendanceHistoryEndpoint.cs`, `OwnAttendanceEndpoints.cs`. Test `tests/Lms.IntegrationTests/Attendance/TakeAttendanceTests.cs`, `EditWindowTests.cs`, `OwnAttendanceTests.cs`, `AbsenceNotificationTests.cs`.

**Interfaces:** take with default + exceptions (one call, L12 version); future session 409; unmarked rows refused on a Taken session; edit window (L14) with reason and change history; summaries per student/class over a date range; own/guardian reads with `CanSubmitExcuse` (L21); `Attendance.Absent` grouped per guardian per session, only when `Taken` (and for live sessions on confirmation, Task 18); relationship incl. substitute and assignment dates.

- [ ] **Step 1: Tests** `Mark_all_present_with_two_exceptions_in_one_call`; `Future_session_cannot_be_taken`; `Taken_session_cannot_have_unmarked_rows`; `Teacher_edit_after_window_is_409`; `Counselor_and_manager_edit_after_window_with_reason`; `Every_change_is_recorded_in_history`; `Stale_version_is_409`; `Substitute_takes_only_their_session`; `Former_teacher_gets_404`; `Absence_notice_grouped_per_guardian_per_session`; `Absence_notice_contains_no_note_text`; `Guardian_reads_only_own_child`; `Can_submit_excuse_follows_the_submitters_setting`; `Summaries_count_statuses_over_a_range`. **Step 2–5** → commit `feat(attendance): taking, edit window, summaries and absence notices`.

**Acceptance:** tests green; probe rows; `Attendance.Absent` in ar and en.

### Task 18: Live-session attendance proposal — lane: BE4 · size M

**PRD:** FR-TT-06, FR-ATT-01, D58 (Q11, Q12). **Depends on:** Task 17, #544 merged (sink shape, never-throws wrapper, `ParticipantsImportedAtUtc`).

**Files:** Create `src/Lms.Domain/Attendance/LiveAttendanceMapping.cs`; `src/Lms.Infrastructure/Attendance/Live/LiveAttendanceSink.cs` (replaces the no-op registration), `LiveAttendanceJob.cs`, `LiveAttendanceReconcileJob.cs` (site 6), `PresenceCalculator.cs` (merged segments); migration `AddLiveAttendanceMappings`; `src/Lms.Api/Features/Attendance/LiveProposalEndpoints.cs`. Test `tests/Lms.UnitTests/Attendance/PresenceCalculatorTests.cs`; `tests/Lms.IntegrationTests/Attendance/LiveProposalTests.cs`, `LiveAttendanceMappingIsolationTests.cs`.

**Interfaces:** L11 items 1–11 in full: sink enqueues `attendance-live:{sessionId}`; proposal only when participants imported; Present/Late/Absent by merged presence and first join; roster = targeted students; unmatched guests listed; mapping stored attendance-side; confirm flips to `Taken` and only then notifies guardians.

- [ ] **Step 1: Tests** `Sink_only_enqueues_and_returns`; `Proposal_is_created_once_per_live_session`; `Reconcile_sweep_creates_a_missed_proposal`; `Reconcile_sweep_runs_each_client_in_its_own_context`; `Live_attendance_job_with_a_foreign_org_payload_does_nothing`; `No_proposal_until_participants_are_imported`; `Only_matched_participants_are_proposed_present`; `Guests_are_listed_for_mapping_never_auto_attached`; `Presence_merges_overlapping_segments`; `Late_uses_the_first_join`; `Roster_follows_the_group_target`; `Hosts_and_staff_are_ignored`; `Mapping_is_stored_on_the_attendance_side`; `Cancelled_live_session_keeps_taken_attendance`; `Guardians_are_notified_on_confirm_not_on_proposal`; isolation test for `LiveAttendanceMapping`. **Step 2–5** → commit `feat(attendance): live-session attendance proposal`.

**Acceptance:** tests green; probe rows; M4b live-session tests still green.

### Task 19: Excuse workflow — lane: BE4 · size L

**PRD:** FR-EXC-02, FR-EXC-03, D61. **Depends on:** Task 17.

**Files:** Create `src/Lms.Domain/Excuses/AbsenceExcuse.cs`, `AbsenceExcuseSession.cs`, `ExcuseStateMachine.cs`; `src/Lms.Infrastructure/Excuses/ExcuseService.cs`, `ExcusePolicy.cs`, `ExcuseAttachmentStore.cs` (L10, `IFileScanner` no-op), `ExcuseAttachmentRetentionJob.cs` (site 8), `ExcuseNotifier.cs`; migration `AddAbsenceExcuses`; `src/Lms.Api/Features/Excuses/ExcusesModule.cs`, `ExcuseSubmitEndpoints.cs`, `ExcuseReviewEndpoints.cs`. Test `tests/Lms.UnitTests/Excuses/ExcuseStateMachineTests.cs`; `tests/Lms.IntegrationTests/Excuses/ExcuseSubmitTests.cs`, `ExcuseReviewTests.cs`, `ExcuseAttachmentTests.cs`, `ExcuseIsolationTests.cs`; `Security/ProbeRows/ExcuseProbeRows.cs`.

**Interfaces:** #611 §6; policy (L13, deadline in school days via `IClientCalendar`); covered sessions frozen at submission (Absent/Late only); submitters per D61 (`excuse_submitter_not_allowed`); queue by subtree; approve (one transaction: records → Excused with change rows, notification); reject (comment required); escalate (L14 targets); withdraw while Submitted; reviewer ≠ submitter; attachments L10; notifications `Excuses.Decided`, `Excuses.Escalated`.

- [ ] **Step 1: Tests** `Guardian_submits_for_own_child`; `Student_submit_is_refused_under_guardians_only`; `Student_submits_under_guardians_and_students`; `Out_of_policy_deadline_is_409_with_reason`; `Disallowed_reason_is_refused`; `Required_attachment_is_enforced_per_reason`; `Only_absent_or_late_sessions_are_covered`; `Approve_flips_records_to_excused_in_one_transaction`; `Reject_requires_a_comment_and_keeps_absent`; `Escalate_to_an_allowed_target_only`; `Reviewer_cannot_be_the_submitter`; `Other_school_counselor_gets_404`; `Attachment_type_and_size_are_checked_by_content`; `Attachment_url_is_short_lived_and_only_for_allowed_readers`; `Guardian_of_another_child_cannot_get_the_attachment`; `Retention_job_purges_only_own_client_files`; isolation tests for `AbsenceExcuse`, `AbsenceExcuseSession`. **Step 2–5** → commit `feat(excuses): excuse submission, review, escalation and attachments`.

**Acceptance:** tests green; probe rows; QA3 security review of the attachment path.

### Task 20: Threshold alerts and attendance reports — lane: BE3 · size S

**PRD:** FR-EXC-04, FR-ATT-01, D61. **Depends on:** Task 17 (Task 19 for the "excused never counts" test).

**Files:** Create `src/Lms.Domain/Attendance/AttendanceAlert.cs`; `src/Lms.Infrastructure/Attendance/AttendanceThresholdJob.cs` (site 7), `AttendanceReportService.cs`, `AttendanceReportExcel.cs`; migration `AddAttendanceAlerts`; `src/Lms.Api/Features/Attendance/AlertEndpoints.cs`, `AttendanceReportEndpoints.cs`. Test `tests/Lms.IntegrationTests/Attendance/ThresholdAlertTests.cs`, `AttendanceReportTests.cs`, `AttendanceAlertIsolationTests.cs`.

**Interfaces:** score = absences + lates × `LateWeightPercent` over `ThresholdWindowDays` (local days); alert once per crossing (marker resets when the score drops below); Counselors of the student's unit notified (`Attendance.ThresholdCrossed`); alerts list with formula inputs; reports per student/class with Excel export (escaped).

- [ ] **Step 1: Tests** `Five_absences_in_30_days_alert_once`; `Late_counts_as_half`; `Excused_records_never_count`; `Alert_repeats_only_after_dropping_below_and_crossing_again`; `Threshold_job_counts_only_own_client_records`; `Alerts_list_shows_the_formula_inputs`; `Reports_respect_supervisor_and_counselor_scope`; `Report_export_escapes_cells`; isolation test for `AttendanceAlert`. **Step 2–5** → commit `feat(attendance): threshold alerts and attendance reports`.

**Acceptance:** tests green; probe rows.

---

### Task 21: M5 design spec on the Stitch system — lane: UX (UI/UX Designer)

**PRD:** X-01, X-10. **Depends on:** Task 1 (names). Starts at once. **Gate:** FE pages and dialogs of Tasks 23–39 do not start before this spec merges, unless the Lead waives the gate in writing on the issue; client/fake tasks (22, and the API parts of 31 and 33) do not wait.

**Files:** Create `docs/design/m5-assessment-timetable-attendance.md` (patterns on the Serene Academic Emerald tokens of `docs/design/stitch/serene_academic_emerald/`: gradebook grid with sticky header/column, cell states with text forms, override flag, category group headers, weights chip, publish dialog incl. provisional warning; student-first list at 360 px; scale band editor + coverage strip + provisional chip; weekly timetable grid (mirrored days, LTR times), slot card, exception badges, conflict chips; attendance take row with four labelled buttons and digit hints, sticky summary bar, proposal banner; excuse stepper, review drawer, status chips; alerts list); `docs/design/mockups/m5.html` (static mock in the style of `m4b.html`); screenshots in `docs/screenshots/ux-m5/` (ar/en, 1440/360, light/dark); Modify `docs/DESIGN.md` (new patterns index, digits rule L20, focus-not-obscured rule L24, forced-colors note); lms-ui skill notes if new components appear.

**Acceptance:** FE1/FE2 confirm the spec covers their tasks; colour is never the only signal in any state; the Lead approves; no code. **Commit** `docs(design): M5 gradebook, timetable, attendance and excuse patterns`.

### Task 22: Grading clients, fakes, tabs — lane: FE1 · size M (#616 H1)

**PRD:** FR-GRD-01, FR-GRD-02. **Depends on:** Task 1.

**Files:** Create `src/Lms.Client/Services/Grading/IGradebookApi.cs`, `GradebookApi.cs`, `FakeGradebookApi.cs`, `IGradingScalesApi.cs`, `GradingScalesApi.cs`, `FakeGradingScalesApi.cs`; `src/Lms.Client/Pages/Attendance/ClassCourseAttendanceTab.razor` (stub, FE2 owns after merge, L22); Modify `src/Lms.Client/Pages/Forums/ClassCourseTabs.razor` (Gradebook and Attendance tabs), `ClientServices.cs`, `NavCatalog.cs`, resx (`Grd_*`, `Scale_*`, `Error_*` of wave 1). Test `tests/Lms.Client.Tests/Grading/FakeGradebookApiTests.cs`, `ClassCourseTabsTests.cs` (extended).

**Tests:** `Fake_returns_published_only_for_students`, `Fake_auto_items_default_to_hidden`, `Fake_enforces_version_conflicts`, `Fake_engine_is_marked_as_demo_only`, `Tabs_depend_on_grade_and_attendance_permissions`, `Wave_1_codes_have_both_languages`. **Commit** `feat(client): grading API clients and class course tabs`.

### Task 23: Grading scales and weight templates admin — lane: FE1 · size L (#616 H2)

**PRD:** FR-GRD-02, FR-GRD-03, D59. **Depends on:** Tasks 21, 22; real API after Task 4.

**Files:** Create `src/Lms.Client/Pages/Grading/GradingScalesPage.razor`, `GradingScaleDialog.razor`, `BandEditor.razor`, `BandCoverageStrip.razor`, `PresetMenu.razor`, `WeightTemplatesPage.razor`, `src/Lms.Client/Components/Grading/CategoryWeightsEditor.razor`. Test `tests/Lms.Client.Tests/Grading/GradingScalesPageTests.cs`, `GradingScaleDialogTests.cs`, `WeightTemplatesPageTests.cs`.

**Interfaces:** #616 §3.3 with L7/L23: presets from server data only, provisional chip (admin pages only), Confirm action, "update available"; no band numbers or Arabic descriptors in client code or resources.

**Tests:** `Coverage_strip_shows_gaps_and_overlaps_as_text`, `Presets_come_from_the_server`, `Provisional_chip_and_confirm_action`, `Update_available_is_offered_for_unedited_copies`, `Make_default_asks_for_confirmation`, `Scale_in_use_message_on_save`, `Weights_chip_previews_the_typed_total`, `No_band_numbers_in_client_resources` (grep test), `Teacher_sees_no_scale_admin_entry`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): grading scales and weight templates admin`.

### Task 24: Gradebook grid — read and render (with spike S1) — lane: FE1 · size L (#616 H3)

**PRD:** FR-GRD-01, FR-GRD-04. **Depends on:** Tasks 21, 22; real API after Task 8.

**Files:** Create `src/Lms.Client/Pages/Gradebook/GradebookPage.razor`, `src/Lms.Client/Components/Gradebook/GradebookGrid.razor`, `GradeRow.razor` (`ShouldRender` guard), `GradeCell.razor`, `ItemHeader.razor`, `StudentFirstList.razor`, `StudentDetailDrawer.razor`, `TermPicker.razor`; `docs/spikes/2026-11-18-m5-grid.md` (S1 result: 60×25 render and edit cost, roving tabindex). Test `tests/Lms.Client.Tests/Gradebook/GradebookGridTests.cs`, `StudentFirstListTests.cs`.

**Interfaces:** #616 §3.1 layout, cell states with text forms, L17 paging, L18 (no client math), L19 one layout, locked banner, empty states (auto items "stay hidden until published"), student drawer with the staff preview (L6).

**Tests:** `Final_comes_from_the_response`, `Cell_states_have_text_alternatives`, `Grid_uses_role_grid_with_colgroup_headers`, `Pages_students_by_50`, `Locked_gradebook_is_read_only_with_a_banner`, `Empty_states_for_no_students_and_no_items`, `Auto_item_header_says_hidden_until_published`, `Only_one_layout_is_rendered_below_600px`, `Student_drawer_preview_uses_the_staff_read`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): gradebook grid`.

### Task 25: Gradebook grid editing and keyboard model — lane: FE1 · size L (#616 H4)

**PRD:** FR-GRD-04. **Depends on:** Task 24.

**Files:** Create `src/Lms.Client/wwwroot/js/gradebook-grid.js` (focus only), `src/Lms.Client/Components/Gradebook/GridKeyboard.cs`, `CellEditor.razor`, `CellMenu.razor`, `OverrideReasonDialog.razor`, `GridLiveRegion.razor`; Modify `GradeCell.razor`, `StudentFirstList.razor`. Test `tests/Lms.Client.Tests/Gradebook/GridKeyboardTests.cs`, `GridEditingTests.cs`.

**Interfaces:** L24 in full; optimistic edit replaced by the server row; override reason required; newer-score keep/drop; excuse from item; revert + text error on failure; `concurrency_conflict` reloads the row; `FormFlush` before navigation/publish; edits survive the 600 px breakpoint.

**Tests:** `Arrows_home_end_and_ctrl_home_move_focus`, `Enter_or_f2_toggles_edit`, `Shift_f10_opens_the_cell_menu`, `Tab_moves_right_only_while_editing`, `Tab_leaves_the_grid_in_navigation_mode`, `Esc_cancels_the_edit`, `Edit_mode_is_announced_and_hinted`, `Rejected_value_is_announced_assertively_once`, `Focused_cell_is_not_obscured_by_sticky_headers` (both directions), `Override_requires_a_reason`, `Newer_score_keep_or_drop`, `Failed_save_reverts_and_shows_text`, `Concurrency_conflict_reloads_the_row`, `Edits_survive_crossing_600px`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): gradebook inline editing and keyboard model`.

### Task 26: Categories, weights and item management — lane: FE1 · size M (#616 H5)

**PRD:** FR-GRD-03. **Depends on:** Task 24.

**Files:** Create `src/Lms.Client/Components/Gradebook/GradebookConfigPanel.razor`, `ItemDialog.razor`, `ApplyTemplateDialog.razor`, `UncategorisedAlert.razor`. Test `tests/Lms.Client.Tests/Gradebook/GradebookConfigTests.cs`.

**Tests:** `Weights_chip_shows_server_validity_after_save`, `Categories_reorder_by_buttons_and_drag`, `Apply_template_confirms_values_are_copied`, `Uncategorised_alert_offers_to_move_items`, `Manual_item_can_be_deleted_and_auto_item_cannot`, `Scale_picker_lists_visible_scales`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): gradebook categories, weights and items`.

### Task 27: Publish flow — lane: FE1 · size M (#616 H6)

**PRD:** FR-GRD-05, D59. **Depends on:** Task 24; real API after Task 11.

**Files:** Create `src/Lms.Client/Components/Gradebook/PublishItemsDialog.razor`, `PublishStateChip.razor`; Modify `NotificationText` + resx (`Notif_Grades_Published`). Test `tests/Lms.Client.Tests/Gradebook/PublishItemsDialogTests.cs`.

**Tests:** `Publish_now_schedule_and_hide_per_item`, `Schedule_must_be_in_the_future_by_server_time`, `Confirm_text_says_who_sees_what`, `Provisional_scale_warning_requires_acknowledgement`, `Result_shows_notifications_queued`, `Unpublish_says_it_is_audited`, `Focus_returns_to_the_publish_button`, `Notification_types_have_both_languages`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): grade publishing`.

### Task 28: Column import and export — lane: FE1 · size M (#616 H7)

**PRD:** FR-GRD-04. **Depends on:** Task 25; real API after Task 10.

**Files:** Create `src/Lms.Client/Components/Gradebook/GradeColumnImportDialog.razor`, `ImportPreviewTable.razor`. Test `tests/Lms.Client.Tests/Gradebook/GradeColumnImportTests.cs`.

**Tests:** `Accepts_xlsx_only_before_upload`, `Preview_shows_counts_unknown_codes_out_of_range_and_overwrites`, `Preview_says_event_scores_are_untouched`, `Apply_uses_the_preview_token`, `Expired_preview_asks_to_upload_again`, `Export_downloads_the_server_file`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): grade column import and export`.

### Task 29: Student and guardian grades — lane: FE1 · size M (#616 H8)

**PRD:** FR-GRD-04, FR-GRD-05, D58. **Depends on:** Task 22; real API after Task 9.

**Files:** Create `src/Lms.Client/Pages/Me/MyGradesPage.razor`, `src/Lms.Client/Pages/Guardians/ChildGradesPage.razor`, `src/Lms.Client/Components/Grading/CourseGradesCard.razor`, `BandLegend.razor`. Test `tests/Lms.Client.Tests/Grading/MyGradesPageTests.cs`, `ChildGradesPageTests.cs`.

**Interfaces:** #616 §3.2 without the cross-course overall result (L18); per-course total "based on published grades", band points labelled per course; no provisional chip (L23).

**Tests:** `Hidden_item_in_a_fake_response_is_never_rendered`, `No_overall_result_across_courses`, `Course_total_is_labelled_published_only`, `Band_legend_in_the_user_language`, `Guardian_page_shows_the_child_name`, `Notification_opens_the_grades_page`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): student and guardian grades`.

### Task 30: Grade settings group, year term option, grade reports — lane: FE1 · size M (#616 H17a, H18a)

**PRD:** FR-GRD-04, D58, D59. **Depends on:** Tasks 5, 9, 22.

**Files:** Create `src/Lms.Client/Pages/Settings/GradeSettingsGroup.razor`, `ClientTimeZoneSetting.razor`, `src/Lms.Client/Pages/Grading/GradeReportsPage.razor`; Modify `src/Lms.Client/Pages/AcademicYears/YearDialog.razor`, `TermsPanel.razor`. Test `tests/Lms.Client.Tests/Grading/GradeSettingsTests.cs`, `GradeReportsPageTests.cs`, `tests/Lms.Client.Tests/AcademicYears/YearDialogTests.cs` (extended).

**Tests:** `Grade_settings_use_the_save_bar`, `Time_zone_picker_lists_iana_zones`, `Terms_option_previews_template_names`, `Terms_switch_shows_dependants_conflict`, `Client_has_no_default_term_count`, `Reports_show_the_as_of_time`, `Report_export_downloads`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): grade settings, term structure and grade reports`.

### Task 31: Timetable set-up and builder (with spike S3) — lane: FE2 · size L (#616 H9)

**PRD:** FR-TT-01..03, D60. **Depends on:** Tasks 2, 21; real API after Tasks 13, 14.

**Files:** Create `src/Lms.Client/Services/Timetable/ITimetableApi.cs`, `TimetableApi.cs`, `FakeTimetableApi.cs`; `src/Lms.Client/Pages/Timetable/TimetableSetupPage.razor`, `PeriodDialog.razor`, `WorkingDaysEditor.razor`, `TimetableBuilderPage.razor`, `TimetableGrid.razor`, `SlotCard.razor`, `SlotDialog.razor`, `MoveToMenu.razor`, `ConflictsPanel.razor`, `CopyTermDialog.razor`; `wwwroot/js/timetable-drag.js`. Test `tests/Lms.Client.Tests/Timetable/*`.

**Interfaces:** #616 §3.4; conflicts shown from the server answer only; keyboard move (`M`, arrows, Enter, Esc) = menu move = drag; touch drag checked at 360 px (S3); room plain text.

**Tests:** `Fake_enforces_block_and_warn`, `Periods_validate_order_and_overlap`, `Block_mode_refusal_highlights_the_clash_with_text`, `Warn_mode_shows_chips_on_both_cards`, `Keyboard_move_equals_menu_move`, `Grid_mirrors_days_in_rtl_and_keeps_times_ltr`, `Publish_says_sessions_will_be_generated`, `Copy_term_lists_skipped_rows`, `Room_is_free_text`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): timetable set-up and builder`.

### Task 32: Timetable views, exceptions and export — lane: FE2 · size M (#616 H10)

**PRD:** FR-TT-04, FR-TT-05. **Depends on:** Task 31; real API after Task 15.

**Files:** Create `src/Lms.Client/Components/Timetable/TimetableView.razor`, `WeekNavigator.razor`, `DayList.razor`, `ExceptionBadge.razor`, `src/Lms.Client/Pages/Timetable/TimetableExceptionDialog.razor`, `MyTimetablePage.razor`, `ChildTimetablePage.razor`, `ClassTimetablePage.razor`. Test `tests/Lms.Client.Tests/Timetable/TimetableViewTests.cs`, `TimetableExceptionDialogTests.cs`.

**Tests:** `Four_entry_points_render_the_same_component`, `Day_list_below_600px`, `Exception_badge_never_alters_the_base_card`, `Substitute_sees_the_slot_on_that_date_only`, `Live_session_slot_shows_the_join_entry`, `Week_range_uses_iso_dates_ltr`, `Export_downloads`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): timetable views, exceptions and export`.

### Task 33: Attendance clients, today's sessions, history drawer — lane: FE2 · size M (#616 H11)

**PRD:** FR-ATT-01. **Depends on:** Task 3; real API after Task 16.

**Files:** Create `src/Lms.Client/Services/Attendance/IAttendanceApi.cs`, `AttendanceApi.cs`, `FakeAttendanceApi.cs`, `src/Lms.Client/Services/Excuses/IExcusesApi.cs`, `ExcusesApi.cs`, `FakeExcusesApi.cs`; `src/Lms.Client/Pages/Attendance/AttendanceTodayPage.razor`, `AttendanceSessionCard.razor`, `AttendanceHistoryDrawer.razor`; fill `ClassCourseAttendanceTab.razor`. Test `tests/Lms.Client.Tests/Attendance/*`.

**Tests:** `Fake_edit_window_uses_the_server_clock`, `Fake_excuse_state_machine`, `Cards_show_state_progress_and_one_primary_action`, `Edit_window_closed_chip_and_edit_only_with_permission`, `Substitute_sessions_are_listed_with_a_banner`, `Proposed_from_live_session_chip`, `History_drawer_lists_changes`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): attendance clients and today's sessions`.

### Task 34: Attendance taking page (with spike S4) — lane: FE2 · size L (#616 H12)

**PRD:** FR-ATT-01. **Depends on:** Task 33; real API after Task 17.

**Files:** Create `src/Lms.Client/Pages/Attendance/AttendanceTakePage.razor`, `AttendanceRow.razor`, `StatusSegmented.razor`, `AttendanceSummaryBar.razor`, `AttendanceDraftStore.cs`, `wwwroot/js/attendance-keys.js`. Test `tests/Lms.Client.Tests/Attendance/AttendanceTakePageTests.cs`, `AttendanceDraftStoreTests.cs`.

**Interfaces:** #616 §3.5 with L25; mark all present first; four labelled 44 px buttons; radiogroup per row; "Next unmarked"; sticky summary with safe area; reason required after the window; search for large rosters (L17).

**Tests:** `Mark_all_present_then_one_exception_sends_default_plus_exception`, `Digit_keys_1_to_4_set_the_focused_row_only`, `Buttons_show_text_and_digit_hints`, `Unmarked_rows_cannot_be_saved_on_a_taken_session`, `Future_session_is_disabled_with_the_reason`, `Reason_required_after_the_window`, `Draft_stores_only_ids_and_statuses`, `Draft_expires_and_is_cleared_on_save_and_sign_out`, `Stale_draft_never_overwrites_a_newer_server_state`, `Save_failure_keeps_marks_and_offers_retry`, `Concurrency_conflict_reloads`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): attendance taking`.

### Task 35: Live-session attendance proposal — lane: FE2 · size M (#616 H13)

**PRD:** FR-TT-06, D58. **Depends on:** Task 34; real API after Task 18.

**Files:** Create `src/Lms.Client/Components/Attendance/LiveProposalBanner.razor`, `UnmatchedParticipants.razor`, `ProposalSourceHint.razor`; Modify `AttendanceTakePage.razor`, `src/Lms.Client/Pages/LiveSessions/LiveSessionsList.razor` (one row action, FE1 owns the file: FE2 edits only the agreed slot after FE1's OK). Test `tests/Lms.Client.Tests/Attendance/LiveProposalTests.cs`.

**Tests:** `Proposal_banner_and_confirm_replace_save`, `Source_hint_has_text_for_assistive_technology`, `Unmatched_guests_can_be_mapped_or_skipped`, `Participants_not_available_yet_shows_the_warning`, `Row_action_opens_attendance_from_the_live_session`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): live-session attendance proposal`.

### Task 36: Own and guardian attendance, excuse submission and tracking — lane: FE2 · size L (#616 H14)

**PRD:** FR-EXC-02, FR-ATT-01, D61. **Depends on:** Task 33; real API after Tasks 17, 19.

**Files:** Create `src/Lms.Client/Pages/Me/MyAttendancePage.razor`, `src/Lms.Client/Pages/Guardians/ChildAttendancePage.razor`, `src/Lms.Client/Pages/Excuses/ExcuseSubmitDialog.razor`, `ExcuseSessionsChecklist.razor`, `MyExcusesPage.razor`, `ExcuseStatusChip.razor`. Test `tests/Lms.Client.Tests/Excuses/ExcuseSubmitDialogTests.cs`, `MyExcusesPageTests.cs`, `tests/Lms.Client.Tests/Attendance/OwnAttendanceTests.cs`.

**Tests:** `Action_follows_can_submit_excuse`, `Student_under_guardians_only_sees_the_guardian_sentence`, `Policy_hint_uses_server_values`, `Out_of_policy_codes_map_to_field_sentences`, `Attachment_types_and_size_checked_before_upload`, `Checklist_reloads_on_date_change`, `Stepper_on_360px`, `Withdraw_only_while_submitted`, `Notification_types_have_both_languages`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): attendance views and excuse submission`.

### Task 37: Counselor review queue — lane: FE2 · size L (#616 H15)

**PRD:** FR-EXC-03. **Depends on:** Task 36; real API after Task 19.

**Files:** Create `src/Lms.Client/Pages/Excuses/ExcuseReviewPage.razor`, `ExcuseReviewDrawer.razor`, `EscalateDialog.razor`, `BulkApproveDialog.razor`, `AttachmentPreview.razor`. Test `tests/Lms.Client.Tests/Excuses/ExcuseReviewTests.cs`.

**Interfaces:** #616 §3.7 with L10 (raster inline only, PDFs download, never inline HTML), L14 targets.

**Tests:** `Queue_filters_default_to_submitted`, `Reject_requires_a_comment_before_the_call`, `Escalate_lists_server_targets`, `Reviewer_is_submitter_message`, `Bulk_approve_confirm_lists_the_count`, `No_bulk_reject`, `Pdf_attachment_is_download_only`, `Image_preview_uses_the_signed_url`, `Approval_says_n_records_become_excused`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): counselor excuse review`.

### Task 38: Alerts page, timetable/attendance/excuse settings, attendance reports — lane: FE2 · size M (#616 H16, H17b, H18b)

**PRD:** FR-EXC-01, FR-EXC-04, FR-ATT-01, D61. **Depends on:** Task 37; real API after Task 20.

**Files:** Create `src/Lms.Client/Pages/Attendance/AttendanceAlertsPage.razor`, `AttendanceReportsPage.razor`, `src/Lms.Client/Pages/Settings/TimetableSettingsGroup.razor`, `AttendanceSettingsGroup.razor`, `ExcuseSettingsGroup.razor`; resx (`Notif_Attendance_*`, `Notif_Excuses_*`). Test `tests/Lms.Client.Tests/Attendance/AttendanceAlertsPageTests.cs`, `AttendanceReportsPageTests.cs`, `tests/Lms.Client.Tests/Settings/M5SettingsGroupsTests.cs`.

**Tests:** `Alert_rows_show_the_formula_inputs`, `Threshold_settings_default_5_in_30_with_half_late`, `Submitters_setting_has_two_options`, `Settings_use_the_save_bar`, `Reports_show_the_as_of_time_and_export`, `Notification_types_have_both_languages`, `Page_renders_rtl_in_arabic`. **Commit** `feat(client): attendance alerts, settings and reports`.

### Task 39: M5 FE hardening — accessibility, 360 px, RTL sweep, E2E hooks — lane: FE1 (+ FE2 for its pages) + UI/UX Designer review · size M

**PRD:** X-01, X-10. **Depends on:** Tasks 22–38.

**Files:** all M5 pages; `docs/screenshots/pr<N>/`; Create `tests/Lms.Client.Tests/M5AccessibilityTests.cs`.

**Interfaces:** named controls, `aria-live` regions, focus return after every dialog/drawer, reduced motion, forced colors, 44 px targets on attendance and timetable add controls, labelled stack cards at 360 px; `data-testid`s agreed with QA1/QA2 for Tasks 42–44; light + dark screenshots in ar and en; designer sign-off against Task 21. **Tests:** `Every_m5_page_has_a_stacked_layout_class_below_600px`, `Every_m5_dialog_returns_focus`, `Every_m5_icon_button_has_a_label`, `M5_resources_have_ar_en_parity`. **Commit** `fix(client): M5 accessibility and responsive sweep`.

---

### Task 40: M5 isolation and permission probe suite — lane: QA (QA2)

**PRD:** §4.2.3, FR-SEC-05, X-05. **Depends on:** Task 1 (first PR right after Task 4 merges; grows per BE merge, as M4b Task 20).

**Files:** Create `tests/Lms.IntegrationTests/Security/M5Callers.cs` (teacher A/B, former teacher, substitute on date D, supervisor of course X, counselor of school 1 / school 2, student 1/2, guardian of child 1 / of children 1 and 2 / inactive link, School-2 admin, client-B admin, anonymous), `M5Routes.cs`, `ProbeRows/M5*.cs`, `M5EndpointMatrixTests.cs`, `M5IsolationTests.cs`, `M5RelationshipProbeTests.cs`.

**Interfaces:** the route table is the oracle (every row × caller); deny rows of L16 (`Counselor_cannot_read_grades_or_gradebooks`, `Supervisor_sees_only_own_courses`); L15 (`Former_teacher_is_404_after_the_end_date`, `Substitute_is_404_outside_the_date`); guardian rows (other child id 404, inactive link 404); `Every_M5_api_route_is_in_the_probe_matrix`; an isolation test per M5 table; every cross-client test of "Isolation bypass and system-context sites" re-run end to end; `IsolationBypassGuardTests` allow-list contains exactly the M5 entries of that table; mutation checks: removing the relationship check in an attendance or gradebook route fails a probe. **Commit** `test(security): M5 endpoint matrix, relationship and isolation probes`.

### Task 41: Security review — grade-leak paths, spreadsheets, attachments — lane: QA (QA3)

**PRD:** FR-GRD-05, X-05, PRD §9.2. **Depends on:** Tasks 9, 10, 11, 12, 17, 19, 29 (grows as they merge; checklist started with wave 1).

**Files:** Create `docs/security/m5-review.md` (checklist: published-only helper, every student/guardian surface — responses, notifications, exports, errors, audit details, search, statistics, markers — lock reads, import/export L9, attachments L10, relationship dates, jobs per client, local days); `tests/Lms.IntegrationTests/Security/GradeLeakAttackTests.cs` (hidden/scheduled items via every student and guardian route, `?include=`/paging/sort parameters, error bodies of 404/409/422, notification payloads, audit detail rows read by a non-staff caller, preview route as a student, guardian with two children swapping ids; a **mutation check** that fails if a student endpoint calls the staff grid query instead of `PublishedGradeReads`); `SpreadsheetAttackTests.cs` (formula payloads in names/titles/notes, macro files, external links, oversized sheets, preview token replay across users and files); `ExcuseAttachmentAttackTests.cs` (polyglot and type-confused files, HTML/SVG uploads, URL reuse after expiry, another guardian, another school's counselor).

- [ ] **Steps 1–5** → commit `test(security): M5 grade-leak, spreadsheet and attachment review`. Findings filed as `bug` + `sev:*` against the owning lane.

### Task 42: E2E exit demo 1 — weights, manual score, publish; student and guardian see it; hidden item invisible — lane: QA (QA1)

**PRD:** FR-GRD-01..05, DEV-PLAN M5 exit (gradebook). **Depends on:** Tasks 4, 7, 8, 9, 11, 22–27, 29, 39.

**Files:** Create `tests/Lms.E2ETests/M5/GradebookTests.cs`, page objects `GradebookPage`, `PublishItemsDialog`, `MyGradesPage`, `ChildGradesPage`.

**Scenarios (`[Theory]` `"ar"` / `"en"`; Arabic asserts `dir="rtl"`):** `Teacher_sets_weights_enters_a_manual_score_and_publishes`; `Student_sees_the_published_item_and_total`; `Guardian_sees_the_same_published_grade`; `Unpublished_item_is_invisible_to_student_and_guardian`; `Quiz_score_appears_as_a_hidden_auto_item`; `Scheduled_item_appears_after_the_time`; plus a **keyboard and screen-reader pass of the grid** (`Grid_is_operable_by_keyboard_only`: arrows, Enter, Tab, Esc, Shift+F10, focus never obscured; accessibility tree assertions for cell names and states).

- [ ] **Steps 1–5** (one E2E stack at a time, `e2e.lock`). **Commit** `test(e2e): M5 gradebook and publishing`.

### Task 43: E2E exit demo 2 — timetable built and published with a conflict; sessions generated; attendance taken at 360 px — lane: QA (QA2)

**PRD:** FR-TT-01..06, FR-ATT-01, DEV-PLAN M5 exit (timetable, attendance). **Depends on:** Tasks 13–17, 31–34, 39.

**Files:** Create `tests/Lms.E2ETests/M5/TimetableAttendanceTests.cs`, page objects `TimetableBuilderPage`, `AttendanceTodayPage`, `AttendanceTakePage`.

**Scenarios (ar + en):** `Admin_sets_periods_and_working_days`; `Admin_builds_a_timetable_and_block_mode_refuses_a_teacher_clash`; `Publishing_generates_sessions_for_the_teacher`; `Substitute_takes_attendance_on_the_exception_date`; `Teacher_takes_attendance_at_360px_with_two_exceptions_in_under_30_seconds`; `Teacher_cannot_edit_after_the_window_and_counselor_can_with_a_reason`; `Guardian_sees_the_absence_and_the_notice`.

- [ ] **Steps 1–5** as Task 42. **Commit** `test(e2e): M5 timetable and attendance`.

### Task 44: E2E exit demo 3 — excuse end to end, threshold alert, live-session proposal — lane: QA (QA1)

**PRD:** FR-EXC-01..04, FR-TT-06, DEV-PLAN M5 exit (excuses). **Depends on:** Tasks 18–20, 35–39; `FakeMeetingProvider` and `POST /api/dev/zoom/events` (M4b).

**Files:** Create `tests/Lms.E2ETests/M5/ExcuseTests.cs`, `LiveAttendanceTests.cs`, page objects `ExcuseSubmitDialog`, `ExcuseReviewPage`, `AttendanceAlertsPage`.

**Scenarios (ar + en):** `Guardian_submits_an_excuse_with_an_attachment`; `Counselor_approves_and_the_record_shows_excused`; `Counselor_rejects_with_a_comment_and_guardian_sees_it`; `Counselor_escalates_to_a_manager_who_decides`; `Student_cannot_submit_under_guardians_only`; `Fifth_absence_alerts_the_counselor_and_an_excused_one_does_not_count`; `Live_session_end_proposes_attendance_and_teacher_confirms`; `Guardian_is_notified_only_after_confirmation`.

- [ ] **Steps 1–5** as Task 42. **Commit** `test(e2e): M5 excuses, alerts and live attendance`.

---

## Exit criteria (M5 done)
1. Tasks 42, 43 and 44 green in CI in ar and en; Tasks 40 and 41 green on `main`; M4a and M4b exit demos still green.
2. `docs/security/m5-review.md` has no open `sev:critical` or `sev:high`; every bypass and system-context site is one listed here with its cross-client test green.
3. No grade value, hidden item title or attachment URL in logs, notifications or audit summaries (tests in Tasks 9, 11, 19, 41).
4. The Saudi presets ship as provisional templates; Q21–Q23 answered (seed updated, `SeedVersion` bumped) or still visibly provisional with the owner informed.
5. `docs/STATUS.md` updated; owner told M5 is done; the remaining owner questions below are listed with their defaults.

## Execution notes for the Team Lead
- **Issues:** one per task (44: 3 Lead, 17 BE, 1 UX, 18 FE, 5 QA) on milestone "M5 Assessment, Timetable & Attendance", labels `M5` + `lane:*` + `area:grading` / `area:timetable` / `area:attendance` / `area:excuses` (create them), on the "LMS v1" project; replace "Task N" in Depends-on lines with issue numbers.
- **Order:** day 1: Tasks 1 and 2 opened, Task 21 starts, Task 40 scaffolding. After Task 1: Tasks 4, 5, 6, 22. After Task 2: Task 13 (BE3), then 14; BE4 starts Task 15 when 14 merges (until then BE4 helps QA2 with probe rows and reviews the #544 sink shape). Task 3 merges by the end of week 1; Tasks 16–20 follow.
- **Critical paths:** gradebook 1 → 4 → 7 → 8 → 11 → 27 → 42 (Task 6 alongside 7); timetable/attendance 2 → 13 → 14 → 15 → 16 → 17 → 19 → 37 → 44.
- **BE lanes (split by folder):** **BE1** — `Domain/Grading` scales/gradebook entities, `Infrastructure/Grading/Scales`, `Gradebooks`, `Publishing`, `Features/GradingScales`, `Features/Gradebook` (grid, config, entries, publish endpoints): 4 → 7 → 8 → 11. **BE2** — `Domain/Grading/Calculation`, `Infrastructure/Grading/Reads`, `Exchange`, `Infrastructure/Spreadsheets`, `Infrastructure/AcademicYears/Term*`, student/report/exchange endpoints: 5 → 6 → 9 → 10 → 12. **BE3** — `Domain/Timetable` (periods, slots), `Domain/Calendar`, `Infrastructure/Calendar`, `Infrastructure/Timetable` (except exceptions), `Infrastructure/Attendance` (taking, summaries, own reads, alerts, reports), `Features/Timetable` (except exceptions), `Features/Attendance` take/own/alert/report endpoints: 13 → 14 → 17 → 20. **BE4** — exceptions and `SubstituteRelationship`, `Domain/Attendance` session entities, generator and ad hoc, `Infrastructure/Attendance/Live`, `Domain/Excuses`, `Infrastructure/Excuses`, `Features/Excuses`, session and live endpoints: 15 → 16 → 19 → 18.
- **Shared BE files (serialize merges):** `Program.cs`, `GradeEventWriter.cs` (Task 7 only), `AuditExporter.cs` (Task 10 only), `ProbeRows/` (one file per PR), `IsolationBypassGuardTests.cs` (only the entries of the sites table), migrations, `Lms.Shared` (Lead only), `ITermDependants` registrations (one line each).
- **FE lanes:** **FE1** — `Services/Grading`, `Pages/Grading`, `Pages/Gradebook`, `Components/Gradebook`, `Components/Grading`, `Pages/Me/MyGradesPage`, `Pages/Guardians/ChildGradesPage`, `YearDialog`/`TermsPanel`, `ClassCourseTabs` (Task 22), `LiveSessionsList.razor` owner: 22 → 23/24 → 25 → 26 → 27 → 28 → 29 → 30 → 39. **FE2** — `Services/Timetable`, `Services/Attendance`, `Services/Excuses`, `Pages/Timetable`, `Pages/Attendance` (incl. `ClassCourseAttendanceTab` after Task 22), `Pages/Excuses`, `Components/Timetable`, `Components/Attendance`, M5 settings groups except grades: 31 → 32 → 33 → 34 → 35 → 36 → 37 → 38, then its pages in 39. Shared FE files: resx (own prefix), `ClientServices.cs` (one line each), `NavCatalog.cs` (one entry each), `NotificationText` (own events).
- **UI/UX Designer:** Task 21 first (gate), then design review on Tasks 23–38 and the Task 39 sign-off.
- **QA:** **QA1** reviews BE1, BE2 and FE1 PRs; owns Tasks 42 and 44. **QA2** reviews BE3, BE4 and FE2 PRs; owns Tasks 40 and 43. **QA3** gives the security review on Tasks 9, 10, 11, 12, 18, 19, 29, 37 and owns Task 41. Every review checks the Review Focus items, the route table, the sites table, resource parity, RTL screenshots and the one-E2E-stack lock.
- **#544 follow-up (M4b):** BE1 adds `LiveSession.ParticipantsImportedAtUtc` and the never-throws sink wrapper there (L11 item 5, approved); Task 18 depends on it.
- **Deferral order if late:** Tasks 30 and 38 (settings groups and reports UI) first, then Task 28; never Tasks 9, 40, 41.
- **Splits from DEV-PLAN §2 M5:** item 1 → Tasks 4, 23; item 2 → 6, 7, 26; item 3 → 8, 9, 10, 24, 25, 28, 29, 30; item 4 → 11, 12, 27; item 5 → 13; item 6 → 5, 14, 31, 32; item 7 → 15, 32; item 8 → 16, 17, 18, 33, 34, 35; item 9 → 19, 20, 36, 37, 38.

## PRD / DEV-PLAN inconsistencies found
- FR-TT-02/03 still list a room and "room double-booked"; D60 makes the room free text with no clash check.
- FR-GRD-01 lists LTI grade items; D50/D58 put LTI out of v1.
- FR-GRD-03 "recalculates on every change" is met by compute-on-read (one engine) plus snapshots at publish; nothing stores recalculated totals.
- #611 A9 assumed a per-client time-zone setting that does not exist; L2 adds `Org.TimeZone`.

## Remaining owner questions (not covered by D58–D61; defaults in force until answered)
- **Q21 School grade bands (D59):** the official Ministry of Education bands and Arabic/English descriptors, and whether early primary grades (1–3) use skills/mastery descriptors instead of percentages. Default: the provisional 5-band seed (90/80/70/60), visibly provisional.
- **Q22 Term structure 2026-27 (D59):** two or three terms this year, and may it change inside a year? Default: `AcademicYears.TermsPerYear` = 3, switchable per client while a year is Planned.
- **Q23 University scale (D59):** the exact letter/point table (5-point or 4-point) of the pilot university client. Default: both provisional templates shipped; the client picks.
- **Q24 Transcript legend:** must a printed report card (M7 PDF) carry the scale legend? Default: Excel exports carry a "Scale" sheet (L27); the PDF answer is needed by M7.
- **O1 Time zone:** confirm every client runs in `Asia/Riyadh` (no DST), or name others. Default: per-client `Org.TimeZone`, Saudi clients `Asia/Riyadh` (L2).
- **O2 Excuse attachments (health data of minors):** retention period and whether antivirus scanning is required in v1. Default: rejected/withdrawn attachments purged after 365 days, approved kept for the year; no-op scanner hook (L10).
- **O3 Digits in Arabic UI:** confirm Western digits for grades, dates and counts. Default: Western digits (L20).
