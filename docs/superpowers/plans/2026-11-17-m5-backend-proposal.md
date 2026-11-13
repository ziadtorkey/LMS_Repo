# M5 Backend Proposal: Gradebook, Timetable, Attendance, Excuses

| | |
|---|---|
| Status | **Revised after the owner round (PRD D58-D61)**: all defaults proposed for Q1-Q20 are accepted (D58); D59 adds Saudi grading presets (new section 4.6); D60 rooms are free text; D61 sets the excuse and alert defaults. The task breakdown in section 9 is the basis of the M5 plan. |
| Author | Backend developer 4, 2026-11-17 |
| Scope | DEV-PLAN M5 items 1-9; PRD FR-GRD-01..05, FR-TT-01..06, FR-ATT-01, FR-EXC-01..04. **FR-GRD-06** (grade lock at year close) belongs to M7 per DEV-PLAN, but its data hooks are cheap now: section 4.5. |
| Modelled on | `2026-11-15-m4b-backend-proposal.md` (#427) |
| Verified against | `main` at the time of writing: `GradeEvent`, `Term`, `ClassCourse`, `StudentGuardian`, `IMeetingProvider.GetParticipantsAsync`, `NotificationEvents`. Anything not verified is marked **Assumption**. |

## 1. What M5 builds on

| Seam | Where | M5 use |
|---|---|---|
| Grade events | `Lms.Domain/Grading/GradeEvent` (append-only, unit-owned, unique per `(SourceKind, SourceId, StudentProfileId, Revision)`; Points, MaxPoints, ScoredAtUtc); `IGradeEvents.RecordAsync`; `GradeSourceKind {Assignment, Quiz}`; `Scorm = 3`, `H5p = 4` are added by T10 (#548) | The gradebook **reads** events; it never changes them. The latest revision per source and student is the current score (ADR 0004). Items are created from events on first sight. |
| Terms and years | `Term` (`AcademicYear`, name, start, end, unit-scoped), `AcademicYear.Close()` (FR-YEAR-04 is M7), `PlacementGuard` (class Active and year not Closed) | A gradebook is per ClassCourse **and term**. Closed years are read-only already; locks (4.5) reuse the same guard. |
| Class course, enrollment | `ClassCourse`, `CourseEnrollment` (Active/Dropped, history kept), `GroupMember` | Rosters for the grid, for attendance sessions and for "own data only". |
| Guardians | `StudentGuardian` (link), guardian portal endpoints (M2/M3) | Guardian views of published grades, timetable, attendance and excuses; excuse submission. |
| Live sessions | `LiveSession`, `IMeetingProvider.GetParticipantsAsync(externalMeetingId)` returning `MeetingParticipant(DisplayName, Email, JoinedAtUtc, LeftAtUtc, DurationSeconds)` (M4b T5/T6) | Attendance from live sessions (section 6.3). |
| Jobs | `IBackgroundJobs.Enqueue / EnqueueAsSystem / AddOrUpdateRecurring` (+ delayed enqueue from M4b) | Session generation, publish-at, threshold alerts, live-attendance import. |
| Notifications | `INotificationPublisher`, `NotificationEvents` (`Assignments.Graded`, `Live.Reminder`, ...) | New events in wave contracts (4.7). |
| Settings and flags | `SettingKeys`, `FeatureFlags`, unit inheritance with locks | Scale defaults, edit windows, conflict policy, excuse policy, thresholds. |
| Files | `IFileStorage` | Excuse attachments. |
| Audit | `[Audited]`, `AuditCatalog` | Grade overrides, publish, lock changes, excuse decisions. |
| Roles | built-in Manager, Supervisor, Counselor, Teacher, Student, Guardian (M1) | Section 5. |

## 2. Decisions this proposal assumes (confirm in review)

| # | Decision |
|---|---|
| A1 | **Everything is unit-scoped like M4a.** Rows copy the unit fields of their ClassCourse (grade items, entries, categories, slots, sessions, records, excuses) or of the unit that owns them (scales, periods, weight templates). Each table gets an isolation test and probe rows in the PR that adds it. |
| A2 | **The calculation engine is a pure, deterministic domain service** (`GradebookCalculator`) with no I/O: input = items, entries, categories, scale, rules; output = per-student category results, final percentage, scale band. It runs on read (and on demand for exports); results are **not** stored except at publish/lock time (`GradeSnapshot`, 4.5). One engine feeds the grid, the student view and the reports, so they cannot disagree. |
| A3 | **Grade events are inputs, not rows the teacher edits.** A teacher override is a `GradeEntry` with `IsOverride = true` and a reason; it wins over the event until removed; every override is audited. A regrade (new event revision) after an override raises a "newer score exists" marker in the grid, it never silently replaces the override. |
| A4 | **Publishing is per grade item** (FR-GRD-05): `Hidden`, `Published`, `Scheduled(at)`. Students and guardians never receive an unpublished score, not even through totals: a student's final grade is computed **from published items only** for them, from all items for staff. This rule lives in one query helper used by every student/guardian read and has its own test. |
| A5 | **Timetable slots reference ClassCourse + teacher + weekday + period (+ room) inside a term**; `Draft` and `Published` states. Publishing a slot set is a transaction that also queues attendance-session generation. |
| A6 | **Attendance sessions are materialised by a job** (not computed on the fly), one per `(slot, date)` with a unique key, so records, excuses and reports have a stable id. Ad hoc and live-sourced sessions use the same table with a different `Source`. |
| A7 | **A substitute or temporary change is a dated exception** (`TimetableException`), never an edit of the base slot (FR-TT-05); generation and every view apply exceptions. |
| A8 | **Excuse = a request over one or more absence sessions of one student**; approval flips matching `Absent` records to `Excused` (history kept in `AttendanceRecordChange`); rejection leaves them `Absent`. The workflow is a small state machine, section 7. |
| A9 | **Dates and times follow the existing convention**: UTC instants in the database, local calendar days/periods resolved with the client's time zone setting (the same setting the recurring jobs use). |
| A10 | **No new DbSet properties**; all sets through `db.Set<T>()`. One migration per PR; wave PRs by the Lead touch `Lms.Shared` only. |

## 3. Entities

### 3.1 Gradebook (FR-GRD-01..05)
| Entity | Key fields | Notes |
|---|---|---|
| `GradingScale` | owner unit, `NameAr/NameEn`, `Kind` (Percentage, Letter, Gpa), `IsDefault` (one per client) | Inherited down the tree like settings; a unit may define its own. Template = a scale flagged `IsTemplate` (copy-on-use). |
| `GradingScaleBand` | scale, `MinPercent`, `LabelAr/LabelEn`, `GpaPoints?` | Letter/GPA ranges, validated gap-free 0-100. |
| `WeightTemplate`, `WeightTemplateItem` | owner unit, name; category name + weight | Reusable across the client (FR-GRD-03). Applying copies values; later template edits do not change existing gradebooks. |
| `Gradebook` | ClassCourse, Term, `ScaleId`, `RoundingMode`, `DecimalPlaces`, `State` (Open, Locked) | Unique per `(ClassCourse, Term)`; created lazily on first use. |
| `GradeCategory` | gradebook, name (ar/en), `Weight` (0-100), order | Weights total 100 for the gradebook to be "valid"; an invalid gradebook computes with a warning, it does not block entering grades. |
| `GradeItem` | gradebook, category, title (ar/en), `SourceKind?` / `SourceId?` (null = manual), `MaxPoints`, `PublishState`, `PublishAtUtc?`, order | Auto-created per source on the first `GradeEvent`; manual items for paper tests. Unique per `(gradebook, SourceKind, SourceId)`. |
| `GradeEntry` | item, student, `Points?`, `IsExcusedFromItem`, `IsOverride`, `Reason?`, `ByUserId`, `AtUtc` | Only for manual scores and overrides. Event-derived scores are read from `GradeEvent`, not copied. |
| `GradeSnapshot` | gradebook, student, published/locked totals as JSON, `AtUtc`, `Kind` (Publish, Lock) | Frozen results for the student/guardian view history and for FR-GRD-06. |

### 3.2 Timetable (FR-TT-*)
| Entity | Key fields | Notes |
|---|---|---|
| `Period` | owner unit, name (ar/en), `StartTime`, `EndTime`, order | Per unit (school); inherits like settings (Q4). |
| working days | setting `Timetable.WorkingDays` (bitmask or list), per unit | Not a table (A10). |
| `TimetableSlot` | ClassCourse, Term, `TeacherUserId`, `DayOfWeek`, `PeriodId`, `RoomName?` (free text, D60), `State` (Draft, Published) | Unique active key per `(ClassCourse, Term, Day, Period)`; conflict checks (teacher and class; the room is free text and is **not** checked, D60) are queries, not constraints, because "warn" mode must save. |
| `TimetableException` | slot, `Date`, `Kind` (Substitute, Cancelled, Moved), `SubstituteUserId?`, `NewPeriodId?`, `Note` | One per `(slot, date)`. |

### 3.3 Attendance (FR-ATT-01)
| Entity | Key fields | Notes |
|---|---|---|
| `AttendanceSession` | ClassCourse, `Date`, `SlotId?`, `PeriodId?`, `Source` (Timetable, AdHoc, Live), `LiveSessionId?`, `State` (Open, Taken), `TakenByUserId?`, `TakenAtUtc?` | Unique `(SlotId, Date)` when from a slot; one per live session. Roster is the enrollments active on the date. |
| `AttendanceRecord` | session, student, `Status` (Present, Absent, Late, Excused), `Note?` | Unique `(session, student)`; created when taking attendance. |
| `AttendanceRecordChange` | record, from/to, by, at, reason | Append-only history (edit window, excuse approval). |

### 3.4 Excuses (FR-EXC-*)
| Entity | Key fields | Notes |
|---|---|---|
| `AbsenceExcuse` | student, `SubmittedByUserId` (guardian or student), reason code, `ReasonText`, `DateFrom/DateTo` or session list, `AttachmentFileId?`, `Status` (Submitted, Approved, Rejected, Escalated), `ReviewerUserId?`, `DecisionComment?`, `EscalatedToUserId?`, timestamps | Unit fields of the student's home unit (the school), so the counselor queue is a unit-subtree query. |
| `AbsenceExcuseSession` | excuse, `AttendanceSessionId` | The sessions the excuse covers (computed from the date range at submission, frozen). |
| excuse policy | settings: max days after absence, allowed reason codes, attachment required, edit window | Not a table (A10). |

### 3.5 Locks (hooks for FR-GRD-06, built in M7)
`Gradebook.State = Locked` plus `GradeChangeRequest` (item, student, proposed points, reason, requested by, `Status`, decided by) are **designed now, built in M7**: M5 only guarantees every grade write goes through one `IGradebookWriter` that checks `State`, so M7 adds the lock without touching callers.

### 3.6 Isolation notes
All tables above implement `IOrgUnitScoped` with unit fields copied from their parent (ClassCourse for gradebook/slot/session rows; the owning unit for scales, periods, templates, rooms, excuses). Reads that must cross the caller's scope follow the existing pattern (`PlacementGuard`, `LibraryQueries`): **bypass sites** are limited to (1) scales and weight templates defined at ancestor units, read by name/bands only, client matched; (2) the term of a gradebook (defined at the year's unit), client matched; (3) a teacher's own timetable across classes (teacher view): rows of the caller's own `TeacherUserId` in their client, and the system-context recurring generation job, which resolves the organization first and then works inside that organization's context. Each gets an `// ISOLATION-BYPASS:` marker, an allow-list entry and a cross-client test.

## 4. Gradebook design

### 4.1 Calculation (A2)
- Item score = latest `GradeEvent` revision per `(source, student)` unless an override exists; manual items read `GradeEntry`. Percentage = `Points / MaxPoints`.
- Category result = points-weighted average of its items **or** simple average of item percentages (setting `Grades.CategoryMethod`, Q7).
- Final = sum(category result x weight) / sum(weights of categories that have at least one graded item for the student) ("normalise over graded categories", setting `Grades.MissingPolicy`: normalise, count as zero, or hide until complete; Q6).
- Excused-from-item entries drop the item for that student. Rounding: half-up to `DecimalPlaces` (default 2), applied once at the end (setting, per gradebook).
- The scale maps the final percentage to a band (letter, GPA points).
- Property tests in `Lms.UnitTests`: weights that sum to 100 give a result within [0,100]; permuting items never changes the result; a regrade revision replaces its predecessor; excused items never lower the result.

### 4.2 Auto items
A job-free approach: `GradebookItems.EnsureForEventAsync` is called when the grade-event reader loads a gradebook (and by the grid endpoint), creating any missing `GradeItem` for sources with events in that ClassCourse and term, in the default category ("Uncategorised", weight 0) until the teacher moves them. **Term assignment of an event:** by `ScoredAtUtc` within the term dates, else the term that is current for the class (Q8).

### 4.3 Grid, import and export (FR-GRD-04)
`GET /api/class-courses/{id}/gradebook?termId=` returns items, students and computed cells for staff (all items). Inline edit = `PUT .../entries`. Excel export uses a column of one item; **import** reads one item column keyed by student code with a preview (counts, unknown codes, out-of-range points) and an apply step, both audited; it never touches event-derived scores. The Excel library is whatever the Imports module already uses (**Assumption**: ClosedXML or equivalent; confirm in Task 3).

### 4.4 Student, guardian and reports
Student/guardian endpoints return **published items only** (A4) and the totals computed from them; guardian access goes through `StudentGuardian`. Supervisor reports (per student, class, course) are staff endpoints with the relationship rule for Supervisors (their courses) and unit scope for Managers. Reports return JSON; PDF/Excel rendering is a client/export task, not a new engine.

### 4.5 Publishing and notifications (FR-GRD-05)
`PublishState` transitions are audited. `Scheduled` items are published by a recurring job (every minute, idempotent, per client in system context) that flips due items and enqueues `Grades.Published` notifications grouped per student and course (one notification per student per publish batch, not per item). Publishing writes a `GradeSnapshot(Kind = Publish)` for history. Locking at year close (M7) writes `Kind = Lock` snapshots and sets `Gradebook.State = Locked`; after that `IGradebookWriter` refuses direct edits and routes them to `GradeChangeRequest` (Manager approval, audited).

### 4.6 Saudi presets and term structure (D59)

D59 requires grading, GPA and academic years to follow Saudi school standards, shipped as **editable data**, not code. The presets below are seed rows of `GradingScale` / `GradingScaleBand` (flagged `IsTemplate`, ar/en names and descriptors) created for every client at provisioning (the built-in role reconciler pattern: add-only, never overwriting a client's edits); every band can be edited or the scale copied. Terms need no new code: `Term` rows per `AcademicYear` already allow any number of terms, so "two or three terms per client" is a **provisioning option** of the academic-year setup (setting `AcademicYears.TermsPerYear` = 2 or 3, default for new years, editable per year while Planned) plus a term template that lays out the term names (ar/en) for each structure.

**What was verified (research done 2026-11-14) and what was not.**

| Item | Finding | Confidence |
|---|---|---|
| Term structure | The Ministry of Education's own announcement ([moe.gov.sa, 22 Jun 2024](https://www.moe.gov.sa/ar/mediacenter/MOEnews/Pages/news1-22062024.aspx)): the **three-term system continues** in general education for 2024-25; the ministry will decide **between two and three terms** later; the school year must have **at least 180 school days**; the 2024-25 terms started 18 Aug 2024, 17 Nov 2024 and 2 Mar 2025 and ended 26 Jun 2025. | **Official source, but dated 2024.** News reports found afterwards disagree on whether the two-term system returns (one outlet reports a cabinet decision to return to two terms from the 1447 school year; most others show three terms in 2025-26). **The current (2026-27) structure was not confirmed: ask the owner, and keep the per-client 2 or 3 option.** |
| School grade bands | **No official Ministry of Education regulation text was reachable** (the student assessment regulation, "لائحة تقويم الطالب", is published by the ministry and the evaluation commission but was not found by search). Secondary sites list a five-band descriptive scale for schools: ممتاز / Excellent 90-100, جيد جدا / Very Good 80-89, جيد / Good 70-79, مقبول / Acceptable 60-69, راسب / Fail below 60. | **Unverified.** Provisional seed only. Not confirmed: whether primary grades 1-3 use skills-based descriptors (mastery levels) instead of percentages; whether bands differ between stages; the pass mark per subject. |
| University scales | Saudi public universities publish their own regulations; the sources found agree that most use a **5-point** GPA (older public universities) and others a **4-point** one, with letters A+ to F and A+ starting at 95. No single national table was found, and the KFUPM regulation PDF could not be read by the tool. | **Unverified**; the point values below are the common pattern, not a citation. |

**Provisional seed data (every row marked `NeedsVerification` in the seed notes until the owner or the ministry document confirms it):**

- **School, percentage bands:** ممتاز / Excellent 90-100; جيد جدا / Very Good 80 to under 90; جيد / Good 70 to under 80; مقبول / Acceptable 60 to under 70; راسب / Fail 0 to under 60.
- **University, 5-point GPA:** A+ 95-100 = 5.00; A 90 to under 95 = 4.75; B+ 85 to under 90 = 4.50; B 80 to under 85 = 4.00; C+ 75 to under 80 = 3.50; C 70 to under 75 = 3.00; D+ 65 to under 70 = 2.50; D 60 to under 65 = 2.00; F under 60 = 1.00.
- **University, 4-point GPA:** the same ranges with A+ = 4.00; A = 3.75; B+ = 3.50; B = 3.00; C+ = 2.50; C = 2.00; D+ = 1.50; D = 1.00; F = 0.00.
- A school-group client defaults to the school scale, a university client to the 5-point scale; both can switch and edit (FR-GRD-02: one default per client).

**Open points the owner or the ministry must settle before the seed ships (new questions Q21-Q24, section 10).** Until they are answered the seed is a clearly labelled provisional template; nothing in the engine depends on the specific numbers.

## 5. Timetable and attendance design

### 5.1 Builder and conflicts (FR-TT-02/03)
Conflict validation is a query over `TimetableSlot` (+ exceptions for a date) for the same term: teacher double-booked and class double-booked (same class, same day and period, any ClassCourse). Rooms are free text (D60), so there is no room conflict check. Setting `Timetable.ConflictMode` = `Block` or `Warn` (per unit): `Block` answers 409 `timetable_conflict` with the clashing slot ids; `Warn` saves and returns the warnings in the response. Moving a slot in the grid is a single `PUT` (drag-and-drop is FE). A teacher's availability or hours-per-week limits are **out of scope** (Q9).

### 5.2 Views, export, copy (FR-TT-04)
Views are filtered reads of published slots (drafts only for staff with `Timetable.Manage`): class, teacher (own data), student (own class), guardian (own children). Export to Excel renders the same grid. **Copy from previous term** copies slots of a source term to a target term as `Draft`, skipping rows whose ClassCourse, teacher or period no longer exists and returning a skipped list.

### 5.3 Substitutes (FR-TT-05)
`TimetableException` per date; the substitute becomes the "teacher of record" for that session only (the attendance session records `TakenByUserId`; the substitute needs permission to take attendance for that one session: **relationship rule extension** `IsSubstituteOn(sessionId)`, section 8). Cancelled/Moved exceptions suppress or relocate session generation.

### 5.4 Generation (FR-TT-06, FR-ATT-01)
Recurring job `AttendanceSessionGenerator` (daily, plus on publish): for each client, resolves the organization, then for published slots in current terms creates sessions for the next `Attendance.GenerateDaysAhead` days (default 7), applying exceptions and skipping non-working days and **holidays** (Q10: the calendar module from M3 `CalendarEndpoints` exists; **Assumption**: it exposes day types, confirm in Task 1). Idempotent by the unique key; late-created slots backfill from today. A slot hosting a live session (FR-TT-06) links `LiveSession.AttendanceSessionId` (nullable column on a table owned by M4b: **Lead decides who adds it**, Q11).

### 5.5 Taking attendance (FR-ATT-01)
`GET .../attendance-sessions?date=` lists a teacher's sessions. `PUT .../attendance-sessions/{id}/records` takes the whole roster in one call with "mark all present" as a client shortcut (server accepts a default status plus exceptions). **Edit window**: setting `Attendance.EditWindowHours` (default 48): after it only a Manager/Counselor edit, audited through `AttendanceRecordChange`. Summaries per student and class (counts and percentages per status over a date range) are computed reads; a student/guardian reads only their own (A4-like helper, own-data rule).

### 5.6 Live sessions (the T6 participant seam)
After a live session ends, a job calls `GetParticipantsAsync`, matches participants to the roster by email then by normalised display name (the same matcher M4b uses for the join list), and **proposes** an `AttendanceSession(Source = Live)` with `Present` (joined, total duration >= `Attendance.LiveMinMinutes`, default 50% of the scheduled length), `Late` (joined after `Attendance.LateAfterMinutes`), and `Absent` for the rest. The teacher **confirms** (state `Open` until taken); nothing is final without a person (D54-style recording caution, Q12). Unmatched participants are listed for manual mapping, not discarded.

## 6. Excuse workflow (FR-EXC-01..04)

```
Absent record -> (notify guardians, EXC-01) -> Submitted -> Approved (records -> Excused)
                                                     |-> Rejected (records stay Absent)
                                                     |-> Escalated -> Approved | Rejected  (by the Manager/other staff)
```
- **Submit** (guardian for own children, or the student): reason code from the allowed list, dates or specific sessions, optional/mandatory attachment per policy, within `Excuses.MaxDaysAfterAbsence`. Out-of-policy submissions are rejected with 409 `excuse_out_of_policy` and a reason code. An excuse covers only sessions where the student is `Absent` or `Late` at submission; later absences need a new excuse.
- **Who may submit (D61):** per-client setting `Excuses.Submitters` (`GuardiansOnly` default, `GuardiansAndStudents`).
- **Review queue** for the Counselor (subtree of their unit): list with filters, approve, reject (comment required), escalate (to a Manager or named staff). The reviewer cannot be the submitter.
- **Approval** is one transaction: excuse -> `Approved`, each covered record -> `Excused` with an `AttendanceRecordChange`, decision notification to the submitter.
- **Tracking** for guardian/student: status, comment, history (own excuses only).
- **EXC-01** absence/late notification: published when attendance is taken (per student record with Absent/Late, grouped per guardian per session batch). **EXC-04** threshold alert: recurring job counts `Absent` (excluding `Excused`) per student over `Attendance.ThresholdWindowDays`; at `Attendance.AbsentThreshold` (default **5 absent sessions in 30 days**, a `Late` counting as **half** an absence, D61) it notifies the Counselors of the student's unit once per crossing (a marker row `AttendanceAlert` prevents repeats until the count drops below and crosses again).
- Counselor reads of absence data are a **relationship** rule: a counselor sees students of their unit subtree; it does not need class-course relationships.

## 7. Endpoints (route families; exact table is the Lead's wave contract)

| Area | Routes (summary) | Permission / rule |
|---|---|---|
| Scales | CRUD under `/api/org-units/{id}/grading-scales`, set default | `Grades.ConfigureScales`, unit scope |
| Weight templates | CRUD under `/api/org-units/{id}/weight-templates` | `Grades.ConfigureScales` |
| Gradebook | GET/PUT config, categories, items, entries, import preview/apply, export per ClassCourse+term | `Grades.Manage` + class-course relationship (teacher/supervisor) |
| Publish | `PUT .../items/{id}/publish` (state, at) | `Grades.Publish` + relationship |
| Student/guardian grades | `GET /api/me/grades`, `GET /api/students/{id}/grades` | own data / `StudentGuardian`; published only |
| Reports | per student, class, course | `Grades.ViewReports`, scope/relationship |
| Periods | CRUD under `/api/org-units/{id}/periods`; working days via settings | `Timetable.Configure` |
| Timetable | GET/PUT slots per class and term, validate, publish, copy-term, exceptions, export | `Timetable.Manage`, unit scope |
| Timetable views | class, teacher (own), student (own), guardian (own children) | read rules per view |
| Attendance | sessions list/create ad hoc, take records, edit, summaries, live proposal confirm | `Attendance.Take` + relationship (teacher of record or substitute) ; `Attendance.Edit` after window |
| Own attendance | `GET /api/me/attendance`, `GET /api/students/{id}/attendance` | own / `StudentGuardian` |
| Excuses | submit, list own, withdraw; queue, approve, reject, escalate | submit: guardian/student; `Excuses.Review` for the queue |

Every route gets probe rows in the PR that adds it; staff routes use `RequirePermission` plus `RelationshipAccess.CheckClassCourseAsync` where a ClassCourse is in scope, and a miss is 404. Student/guardian routes never trust a student id in the URL without the guardian link check.

## 8. Permissions, flags, relationships, audit

- **Permissions (wave 1 adds the catalog):** `Grades.View`, `Grades.Manage`, `Grades.Publish`, `Grades.ConfigureScales`, `Grades.ViewReports`, `Timetable.View`, `Timetable.Manage`, `Timetable.Configure`, `Attendance.Take`, `Attendance.Edit`, `Attendance.View`, `Excuses.Submit`, `Excuses.Review`, `Excuses.Escalate`. Defaults: Teacher gets Grades.Manage/Publish and Attendance.Take for own class courses; Supervisor gets reports; Manager gets all and timetable configure/manage; Counselor gets Attendance.View and Excuses.Review; Student/Guardian get the own-data reads and Excuses.Submit (guardian) / Excuses.Submit (student, subject to Q13).
- **Relationship rules:** teacher-of-record or `IsSubstituteOn(session)` for attendance; teacher/supervisor for gradebook; guardian link for children; counselor/manager by unit subtree for excuses.
- **Flags:** none new (M5 is core). A per-client switch for guardian excuse submission is a setting, not a flag (Q13).
- **Audit (`AuditCatalog`):** `GradeEntry` overrides, `GradeItem` publish changes, `GradeSnapshot`, `Gradebook` config, `TimetableSlot` publish, `AttendanceRecordChange`, `AbsenceExcuse` decisions.
- **Privacy:** grades and attendance are student data of minors: no values in logs, no grade text in notification bodies beyond item title and course (the notification says "a new grade is published", the student opens it in the app); exports are audited.

## 9. Tasks (one issue and one PR each; lanes BE/FE/QA; sizes S <= 1 day, M 2-3 days, L 4-6 days)

| # | Task | Lane | Size | Depends on |
|---|---|---|---|---|
| 1 | **Wave 1 contract**: gradebook, scales, weights, timetable and attendance DTOs, routes, permissions, settings, notification events, problem codes, audit names (the Lead splits into waves 1-3 below) | Lead | M | M4 merged |
| 2 | **Grading scales and weight templates** (`GradingScale`, bands, `WeightTemplate`; inheritance; default per client; migration `AddGradingScales`) | BE | M | 1 |
| 3 | **Gradebook model and calculation engine** (`Gradebook`, categories, items, entries; `GradebookCalculator` with property tests; `EnsureForEventAsync`; migration `AddGradebook`; `IGradebookWriter`) | BE | L | 2 |
| 4 | **Grid, import/export, student and guardian views, reports** | BE | L | 3 |
| 5 | **Publishing, scheduled publish job, snapshots, notifications** | BE | M | 4 |
| 6 | **Periods and working days** (migration `AddPeriods`; rooms are free text, D60) | BE | S | 1 |
| 7 | **Timetable slots, builder endpoints, conflicts, views, export, copy-term** (migration `AddTimetable`) | BE | L | 6 |
| 8 | **Exceptions and substitutes** (+ `IsSubstituteOn` relationship rule; migration `AddTimetableExceptions`) | BE | M | 7 |
| 9 | **Attendance sessions: generator job, ad hoc, taking, edit window, summaries, own/guardian reads** (migration `AddAttendance`) | BE | L | 7, 8 |
| 10 | **Live-session attendance proposal** (participant matcher reuse, confirm flow) | BE | M | 9, M4b T6 |
| 11 | **Absence notifications and the excuse workflow** (entities, policy, state machine, queue, escalation, approval transaction; migration `AddAbsenceExcuses`) | BE | L | 9 |
| 12 | **Threshold alerts and attendance reports** | BE | S | 11 |
| 13 | **Grade-lock hooks verification** (`IGradebookWriter` refuses when `Locked`; test only, M7 builds the lock) | BE | S | 3 |
| 14 | **Probes and the M5 security checklist** (cross-client rows for every table, own-data tests for student/guardian, unpublished-grade leak tests, excuse authorization matrix) | BE + QA | M | 5, 11 |
| FE | Gradebook grid and config, student/guardian grades, timetable builder and views, attendance taking, excuse submission and queue | FE | per FE proposal | contracts of each wave |

**Waves (Lead contract PRs, each merged before the BE tasks that use it):** wave 1 = scales, weight templates, gradebook, grid, publishing (Tasks 2-5); wave 2 = periods, timetable, exceptions (6-8); wave 3 = attendance, live proposal, excuses, thresholds (9-12). Tasks 6-8 can run in parallel with 2-5 on a different BE; 9 waits for 7/8.

**Critical path:** 1 -> 2 -> 3 -> 4 -> 5 (about 12-14 BE days) in parallel with 1 -> 6 -> 7 -> 8 -> 9 -> 11 (about 14-16 BE days); QA tasks overlap.

## 10. Owner questions (answered in the D58 round: all proposed defaults accepted; Q5 became D60, Q13 and Q17 became D61; Q21-Q24 are new from D59)

1. **Q1 Lock scope.** Is FR-GRD-06 (lock at year close, Manager-approved changes) really M7, or must M5 ship a manual "lock this term's gradebook" button? (Proposal: M7; M5 only keeps the write seam.)
2. **Q2 GPA.** Is GPA a display of the scale band's points only, or a cumulative GPA across courses and terms (needs credit hours)? (Proposal: band points only in M5.)
3. **Q3 Scale per course.** May a ClassCourse pick a scale other than the unit default? (Proposal: yes, from the scales visible at its unit.)
4. **Q4 Periods per unit or per level.** One period set per school, or different sets per grade level? (Proposal: per unit; a unit below may override.)
5. **Q5 Rooms.** Do you want room conflicts (needs a `Room` list) or a free-text room name only?
6. **Q6 Missing grades.** When a student has no score for an item: ignore it (normalise over graded items), count it as zero, or hide the final until complete? (Proposal: setting with `normalise` default.)
7. **Q7 Category average.** Points-weighted or average of percentages? (Proposal: points-weighted default, setting per gradebook.)
8. **Q8 Term of an event.** An assignment scored after the term ended: which term? (Proposal: the term of the item's class course current at `ScoredAtUtc`, else the last term.)
9. **Q9 Teacher limits.** Teacher availability, maximum periods per day and hours per week: required for v1? (Proposal: no.)
10. **Q10 Holidays.** Should session generation skip public/school holidays from the calendar? (Proposal: yes, using the M3 calendar day types.)
11. **Q11 Live link.** May M5 add a nullable `AttendanceSessionId` to the M4b live-session table, or should the link live in the attendance session (`LiveSessionId`) only? (Proposal: attendance side only; no change to M4b.)
12. **Q12 Live attendance trust.** Is Zoom participation enough to propose "Present", with the teacher confirming, or may it be applied automatically? (Proposal: propose, teacher confirms.)
13. **Q13 Excuse submitters.** Can students submit their own excuses, or guardians only? Per client setting?
14. **Q14 Excuse reasons.** Fixed list of reason codes (illness, family, official, other) or configurable per client? Mandatory attachment for which reasons?
15. **Q15 Escalation target.** Escalate to the Manager only, or to any staff member the Counselor picks (the PRD allows "another staff member")?
16. **Q16 Attendance edit window.** Default 48 hours acceptable? Who may edit afterwards: Manager only, or Counselor too?
17. **Q17 Threshold.** Defaults for the absence alert (for example 5 absent sessions in 30 days) and whether late counts as a fraction.
18. **Q18 Guardian visibility of drafts.** Confirm guardians see published grades only, never item titles of hidden items.
19. **Q19 Export formats.** Excel only (PRD), or PDF report cards in v1? (Proposal: Excel only; PDF is M7.)
20. **Q20 LTI items.** FR-GRD-01 mentions LTI grade items: out of v1 with FR-STD-03, confirmed?
21. **Q21 School bands (new, D59).** The official Ministry of Education grade bands and descriptors, and whether early primary grades use descriptors instead of percentages (section 4.6).
22. **Q22 Term structure (new, D59).** The official 2026-27 structure (two or three terms) and whether it may change inside a year.
23. **Q23 University scale (new, D59).** Which 5-point or 4-point table applies to the pilot university client, with its exact letter and point table.
24. **Q24 Transcript legend (new).** Must a printed grade report carry the scale legend?

## 11. Risks

| Risk | Mitigation |
|---|---|
| **Calculation disagreements** between grid, student view and reports | One pure engine (A2), property tests, golden-file tests with hand-computed gradebooks from the owner's real examples (ask in Q6/Q7). |
| **Leaking unpublished grades** through totals, ranks, notifications, exports or cache | A4 helper used by every student/guardian read, a dedicated leak test per endpoint, no grade values in notifications, exports audited. |
| **Event revisions and overrides** confuse teachers | Override marker plus "newer score exists" flag (A3); regrade never silently replaces an override. |
| **Session-generation job** misses or duplicates (time zones, DST, slot edits after generation) | Unique `(slot, date)` key, idempotent job, generation only forward from today, a repair endpoint for the Manager; time-zone tests across a DST change. |
| **Cross-class timetable reads** break isolation (teacher teaching in two schools of one client) | Teacher views are bypass site 3 with a cross-client test; the rows are filtered by the caller's own user id and client. |
| **Substitute permission** widens attendance access | Substitution is per session and date, expires, and is covered by a probe row and a test that a substitute cannot edit other sessions. |
| **Excuse approval changes history** after reports were read | `AttendanceRecordChange` keeps both values; reports state the "as of" time. |
| **Saudi preset accuracy (D59)**: the official bands were not found; wrong bands would mis-grade students | Seeds are editable, flagged provisional, and the engine takes bands as data; Q21-Q23 must be answered before the seed ships. |
| **Scope**: M5 is the largest milestone so far (about 14 BE tasks) | Three waves, two BE tracks in parallel (gradebook, timetable-attendance), FE starts each area after its wave. |
| **Notification volume** (per-session absence notices to guardians) | Group per guardian per session batch; quiet hours and channel preferences come with M6, so M5 sends in-app events only. |
| **Live attendance matching** errors (shared accounts, nicknames) | Propose-then-confirm (Q12), unmatched list, manual mapping persisted per live session. |

## 12. Notes found while writing

- `GradeSourceKind` only has Assignment and Quiz on `main`; Scorm and H5p arrive with T10 (#548). The gradebook must treat unknown kinds as "auto item with the source title unknown" until the kinds are mapped, so a new source never crashes it.
- `LessonModule` completion rules and gradebook items are independent: a module's `Scored` completion does not create a grade item; only `GradeEvent` rows do.
- **Assumption** to verify in Task 1: that term dates always cover a class course's active period; if a class course has no term, the gradebook is created for the year's current term (Q8 covers the edge).
