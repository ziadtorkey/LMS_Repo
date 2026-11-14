# M5 security review checklist: grade leaks, spreadsheets, excuse attachments, substitutes, cross-client timetable reads

For QA3 (M5 Task 41, plan #621). Derived from the M5 plan (`docs/superpowers/plans/2026-11-18-m5-assessment-timetable-attendance.md`: owner decisions D58-D65, Lead decisions L1-L27, the bypass/system-context table and the route/permission/relationship oracle), the QA reviews of #611 and #616, and modelled on `docs/security/m4b-review.md` (#550). Grows as the tasks merge; the wave-1 rows (section 1, 2) can start with Tasks 9-11.

How to use: every row names **what to check**, **how** (an automated test to write or run, or a manual probe) and the **expected result**. Tick a row only with evidence (test name, request/response, or screenshot) in the PR or issue. A row marked **blocked by** depends on a task or an owner answer that has not landed; list it as blocked, do not skip it. Findings are filed as `bug` + `sev:*` against the owning lane (BE1/BE2 gradebook, BE3/BE4 timetable and attendance, FE1/FE2 pages).

Conventions: "student" = a student account reading their own data; "guardian" = an account with an **active** `StudentGuardian` link; "staff" = a role with the permission in the route table. A relationship miss must be an **identical 404** (same status, headers, body shape and comparable timing as a non-existent id); a missing permission is 403. "Hidden item" = a `GradeItem` that is `Hidden`, `Scheduled` (publish time in the future) or unpublished again; "leak" = any trace of such an item or of an unpublished score: value, title, count, rank, marker, average, or even its existence.

## 0. Preconditions

| # | Check | How | Expected |
|---|---|---|---|
| 0.1 | Test world has: two clients (A, B), two schools in A, one class course with a published, a scheduled, a hidden and an event-derived (auto) item, a student with a guardian, a **guardian of two children**, a teacher of record, a former teacher, a substitute, a counselor of each school, a supervisor | `GradeLeakWorld` test kit (Task 40 probe suite) | Every row below runs against it; seed values are distinctive (for example 77.77, titles with a unique marker string) so a leak can be found by `Assert.DoesNotContain` |
| 0.2 | Open owner questions are listed with their status | Plan "Remaining owner questions": Q21 (MoE bands), O2 (excuse attachment retention and antivirus), O3 (digits) | Each row that depends on one is marked blocked or uses the default in force |
| 0.3 | The seeded Saudi presets are marked provisional | Read provisioning output | `Provisional = true`, `SeedKey`, `SeedVersion` on every seeded scale (L7) |

## 1. Grade-leak paths: published-only (L6, Review Focus 1, FR-GRD-05)

The rule: **no unpublished score, total, title, count, marker or rank reaches a student or guardian through any route, notification, export, error, cache or audit detail.** The only student/guardian read path is `PublishedGradeReads`; totals are computed by the engine from published items only.

### 1.1 The helper is the only path

| # | Check | How | Expected |
|---|---|---|---|
| 1.1.1 | Every student/guardian grade endpoint (`GET /api/me/grades`, `GET /api/students/{sid}/grades`, any search/autocomplete, band legend, notification text builder, error message builder) goes through `PublishedGradeReads` | **Mutation check:** a test that swaps one student endpoint to the staff grid query (or to `Gradebook` entities directly) and fails; plus an architecture test that no type in the student/guardian endpoints references the grid query or `GradeEntry` set | Pass; the mutated build fails the leak test (record the failing test name) |
| 1.1.2 | The staff "what the student sees" preview (`GET .../gradebook/students/{sid}/student-view`) is staff-only and student-unreachable | Call it as the student, the guardian, the other teacher, the former teacher, a counselor | student/guardian/counselor 403; other or former teacher 404; teacher of record 200 and it returns exactly what the student route returns |
| 1.1.3 | No student-reachable route renders the staff preview or the grid | Route list guard (like the M4b embed guard): enumerate routes returning gradebook data, assert each requires `Grades.Manage`/`View`/`ViewReports` | New route without a permission fails the build |
| 1.1.4 | Totals for a student are computed from published items **only** and by the same engine as the grid | Publish 2 of 4 items; compare the student total with a recomputation over the 2 items; change a hidden score by 100 points | Student total unchanged; equals the engine result over published items; never "normalised" using hidden items |

### 1.2 Per-surface leak tests (`GradeLeakAttackTests`)

For each row run it as the **student**, the **guardian** (own child), the **guardian with two children swapping ids**, and a student of another class; the seed marker strings must not appear anywhere in the response (body, headers, ETag, `Link`, error text).

| # | Surface | Attack | Expected |
|---|---|---|---|
| 1.2.1 | Grades list | Request with a hidden item, a scheduled item (publish time in the future by the server clock), an un-published-again item | Only published items; item count equals published count; no `hiddenCount`/`total` field that includes hidden items |
| 1.2.2 | Query parameters | `?include=`, `?expand=`, `?items=all`, `?sort=` by a hidden item id or by a column name, `?page`/`pageSize` past the end, `?termId=` of another class | Parameters cannot widen the result; sort by a hidden column is refused or ignored; paging totals count published rows only |
| 1.2.3 | Item existence | Ask for a hidden item by id on any item route | Identical 404 to a non-existent id (status, body shape, timing class); never 403 |
| 1.2.4 | Rank, percentile, class statistics | Look for average, min, max, median, rank, standard deviation, "better than N%" in any student/guardian DTO | None (L6: none shown in M5); a test pins the DTO property names so a new statistics field fails the build |
| 1.2.5 | "Newer score exists" marker | After a teacher edits a published item's score post-publish | The marker is staff-only; the student route shows only the published value and no flag |
| 1.2.6 | Event-derived (auto) items | An auto item created from a quiz/assignment event is `Hidden` until published (L3) | Absent from every student/guardian surface, including the course totals and the notification count |
| 1.2.7 | Scheduled publish | Schedule an item for later; read as student before and after the job runs (fake clock) | Before: absent; after: present exactly once; the job changes nothing for a client other than its own (section 6) |
| 1.2.8 | Un-publish | Un-publish a published item | Immediately absent from the student routes; any cache keyed by the old state is invalidated (see 1.5); audited |
| 1.2.9 | Provisional scale | Publish under a provisional scale | 409 `scale_provisional` without `acknowledgeProvisionalScale`; with it, audited; the band legend shown to students is the configured one (L23) |
| 1.2.10 | Lock reads (L5) | Lock a gradebook (test-only lock, Task 12), record an event after `LockedAtUtc`, publish nothing | Student sees the `GradeSnapshot(Kind = Lock)` values; the later event is ignored and **not visible** (not even as a count) to the student; staff see the "events after lock" marker; writes answer 409 `gradebook_locked` |
| 1.2.11 | Snapshots | Read `GradeSnapshot` rows (`Publish`, `Lock`) through any route as the student | Only published values of the student's own snapshot; another student's snapshot is the identical 404 |
| 1.2.12 | Guardian of two children | Child A's id with child B's route/body/query; swap `sid` in the path | Identical 404; response contains no data of the other child |
| 1.2.13 | Guardian link state | Guardian whose link is inactive/ended/unconfirmed | 404 as for a stranger |
| 1.2.14 | Former and substitute staff | Former teacher (assignment ended yesterday), substitute | 404 on the grid and the preview (L15); no grades reachable |
| 1.2.15 | Supervisor, counselor | Supervisor of another course; counselor | Supervisor: reports for supervised courses only; counselor: 403 on every `Grades.*` route (L16) |
| 1.2.16 | Sorting/search side channels | Search item titles via autocomplete as student; response size or ordering that depends on hidden rows | Results only from published items; no timing or length difference attributable to hidden items (compare response length with and without a hidden item) |

### 1.3 Errors, validation and problem bodies

| # | Check | How | Expected |
|---|---|---|---|
| 1.3.1 | 404/409/422 bodies of every student/guardian grade route | Force each error with a hidden item involved (invalid term, wrong state, bad query) | The body, `detail`, `instance` and extension fields never name a hidden item, its title, id of a hidden item, a score or a count |
| 1.3.2 | Validation messages | A rejected request referencing a hidden item id | Same message as for an unknown id |
| 1.3.3 | `Retry-After`, `ETag`, `Last-Modified` | Compare headers with and without a hidden item changing | Do not change when only hidden data changes |

### 1.4 Notifications, logs and audit (L26, Privacy)

| # | Check | How | Expected |
|---|---|---|---|
| 1.4.1 | `Grades.Published` notification | Publish a batch; read the notification body in ar and en, the push/e-mail payload, the SignalR payload | Course name only: **no grade value, no item title, no band**; one notification per student (and linked guardians) per batch |
| 1.4.2 | Resource placeholders | Resource test over `Notif_*` / `Grd_*` texts | Fails on `{points}`, `{score}`, `{title}`, `{grade}` style placeholders in grade notification texts |
| 1.4.3 | Scheduled publish and un-publish notifications | Run the job; un-publish | A scheduled publish notifies at publish time, never at scheduling time; un-publish sends nothing that reveals the item |
| 1.4.4 | Logs | Log-capture assertion across a publish, a student read, an import, an export and a 404 | No grade values or hidden titles in any log line (ids only) |
| 1.4.5 | Audit details | Read audit rows (publish changes, overrides, provisional acknowledgement, exports, imports) **as a non-staff caller and as a teacher of another course** | Not readable (403/404); the stored summaries hold ids, not values or titles |
| 1.4.6 | Guardian notification to the wrong guardian | Publish; check the recipients | Only active linked guardians of that student; none of another child's |

### 1.5 Caches, exports and indirect views

| # | Check | How | Expected |
|---|---|---|---|
| 1.5.1 | Response caching | Inspect `Cache-Control` on every student/guardian/staff grade response; two users (student then teacher) hitting the same URL | `private, no-store` (or per-user vary); no shared or output cache serves the teacher's body to a student; server-side caches are keyed by user **and** publish state |
| 1.5.2 | Cache after publish changes | Student reads, teacher un-publishes, student reads again | Second read reflects the change at once |
| 1.5.3 | Reports | `GET /api/grade-reports/{student|class|course}` + `/export` as supervisor (own courses), manager, teacher, counselor | Teacher/counselor 403; supervisor/manager see staff data only within scope; a report route is never callable by a student or guardian |
| 1.5.4 | Report Excel and column export "Scale" sheet (L27) | Open the workbook as produced for staff | Scale sheet has bands and descriptors only; no hidden item titles outside a staff-only export |
| 1.5.5 | Student/guardian exports (if any is added) | Look for any download of grades by a student | None in M5 (Excel is staff-only per D58); a test fails if a student route returns a file |
| 1.5.6 | Search | Global/search endpoints used by the shell | No grade or item title from a hidden item is indexed or returned |
| 1.5.7 | Print/PDF, thumbnails, share links | Grep for any such route | None in M5 (PDF report cards are M7) |
| 1.5.8 | `serverNowUtc` and timing | Compare responses before/after a scheduled publish time | A student cannot learn the scheduled time of a hidden item (no `publishAt` for unpublished items in student DTOs) |

### 1.6 Writes and the lock seam

| # | Check | How | Expected |
|---|---|---|---|
| 1.6.1 | Only `IGradebookWriter` writes entries | Architecture test: no other type calls `SaveChanges` on gradebook entities | Pass |
| 1.6.2 | Optimistic concurrency (L12) | Two writers, stale version | 409 `concurrency_conflict` carrying the **current value only to a staff caller** with `Grades.Manage` |
| 1.6.3 | No writes on read (L4) | A student, guardian or report read | Creates no row (assert table counts); the backfill runs only in the staff grid GET with `Grades.Manage`, never in a closed year |
| 1.6.4 | Mass assignment | PUT entries with extra fields (`published`, `lockedAtUtc`, `source`) | Ignored or 400; cannot publish or override source through a score write |
| 1.6.5 | Event-derived scores | Import or PUT over an event-derived score | Refused (event scores are never written by import or manual edit) |

## 2. Spreadsheet import and export (L9, Task 10, FR-GRD-04)

| # | Check | How | Expected |
|---|---|---|---|
| 2.1 | Formula injection on every export cell | `SpreadsheetAttackTests`: names, item titles, category names, notes, comments starting with `=`, `+`, `-`, `@`, tab, CR, and leading spaces/quotes; unicode look-alikes (full-width `＝`) | Every cell goes through `SpreadsheetCell.Safe` (prefix/escape so it is stored as text); open the file with a parser and assert the cell type is string and the value starts with the escape; `AuditExporter` still escapes after the move (single shared helper, no copy) |
| 2.2 | No other export path bypasses the helper | Grep every `.xlsx`/CSV writer; architecture test that spreadsheet cell writes go through `SpreadsheetCell` | One helper; test fails if a writer sets a raw string |
| 2.3 | CSV/Excel hyperlinks and DDE | Titles with `HYPERLINK(`, `cmd|`, `DDE` | Neutralised as text |
| 2.4 | `.xlsx` only | Upload `.xlsm`, `.xls`, `.xlsb`, `.csv`, `.ods`, a zip renamed `.xlsx`, a polyglot, an HTML file renamed `.xlsx`, a file with a macro part (`vbaProject.bin`) | Refused with the documented code; sniffed by content, not by extension or declared content type |
| 2.5 | Size and shape limits | Over `Files.MaxUploadMb`, over 2 000 rows, over 50 columns, a sparse sheet with a huge used range, a zip bomb inside the package, a 30 s+ parse (shared strings blow-up) | Refused or aborted within the 30 s budget; memory and CPU stay bounded (measure peak); no partial apply |
| 2.6 | XML safety | Crafted `.xlsx` with DTD, XXE to a local file and a URL, billion laughs, external `schemaLocation` | DTD processing prohibited; no outbound request, no file read (canary); parse fails cleanly |
| 2.7 | External links and defined names | Workbook with external links, `INDIRECT`/external references, defined names pointing at other files | Ignored (never fetched, never evaluated); the import reads cell **values** only, never formulas results from other workbooks |
| 2.8 | Preview token (15 min) | Replay across users, across gradebooks, across items, across file contents (changed SHA-256), after 15 min, after use | **One identical refusal** each; token bound to (uploader, gradebook, item, file SHA-256) |
| 2.9 | Apply revalidates | Change data between preview and apply (score edited, student unenrolled, item published/locked, gradebook locked) | Apply re-validates against current data; stale rows rejected; lock answers 409 `gradebook_locked` |
| 2.10 | Row-level authorization | Import rows for students not in the class course, from another term, with another client's student number | Rejected per row; nothing written for them; no information about other students in the error list |
| 2.11 | Out-of-range and type confusion | Scores above max, negative, `NaN`, `1e309`, dates, text in number cells, huge numbers, trailing/leading control characters | Per-row errors in the preview; never an exception or a 500 |
| 2.12 | Event-derived scores never touched | Import a column containing event items | Refused for those rows |
| 2.13 | Audit | Export, preview, apply, refused import | Each audited with ids and counts (no values, no file content); a refused attempt is also audited |
| 2.14 | Export scope | Export an item column as the teacher of another course, the former teacher, a counselor | Other/former teacher 404, counselor 403; the file names no students outside the class course |
| 2.15 | File names and headers | Filename with CR/LF, path separators, unicode bidi controls | `Content-Disposition` is safe (sanitised), `nosniff`, `Cache-Control: private, no-store` |
| 2.16 | Streaming | Large export and import | Not buffered whole in memory; limits enforced before the full read |

## 3. Excuse attachments (L10, D65, Task 19)

D65: the antivirus hook (`IFileScanner`) is a **no-op in v1**. The compensating controls below are therefore mandatory, and every row marked **AV off** must hold without a scanner. A real scanner is a pre-production gate (see 3.12).

| # | Check | How | Expected |
|---|---|---|---|
| 3.1 | Type allow-list by **content** | `ExcuseAttachmentAttackTests`: upload `jpeg`, `png`, `webp`, `pdf` genuine; then HTML, SVG, XML, JS, `.exe`, Office files, a zip, a text file renamed `.jpg`, a `.pdf` renamed `.png`, polyglots (JPEG+HTML, PDF+JS, GIF+PHP), double extensions (`a.pdf.html`) | Only the four types accepted, decided by magic bytes; everything else refused with a stable code; the extension and declared `Content-Type` are never trusted; a refused upload leaves no object |
| 3.2 | Size | Over `Excuses.MaxAttachmentMb` (5), exactly at, zero bytes, a declared length that lies, chunked upload that never ends | Refused at the limit while streaming; nothing partial stored |
| 3.3 | AV off | Upload an EICAR test string inside an allowed type | Accepted today (documented) **and** never served inline: download only with `Content-Disposition: attachment` and `nosniff`; record this as a known, accepted v1 residual risk |
| 3.4 | Storage | Inspect the object store | Private bucket/prefix per client and OrgUnit; no public URL; the object key is not guessable and not derived from the user's file name; no pre-signed URL in any DTO other than the dedicated `attachment-url` response |
| 3.5 | Signed URL | `GET /api/excuses/{id}/attachment-url` | Valid 5 minutes (test with the clock), single excuse and object only, `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`; reuse after expiry refused; the URL is not logged |
| 3.6 | Who can read | Submitter, the student's **linked** guardians, a counselor/manager with `Excuses.Review` in the student's unit subtree; then another guardian, a guardian of a different child, a teacher, the student under `GuardiansOnly`, another school's counselor, another client's counselor, an unlinked guardian | Allowed set reads; every other caller gets 404 (403 only for a permission miss); no difference in body or timing between "no attachment" and "not allowed" |
| 3.7 | Inline preview | Review drawer | Inline only for jpeg/png/webp; PDFs download only (no in-app PDF viewer); an SVG or HTML can never render inline |
| 3.8 | Retention job (site 8) | Reject/withdraw an excuse; advance the clock past `Excuses.AttachmentRetentionDays` (365); run the job for client A with client B data present | Attachments of rejected/withdrawn excuses purged for client A only; approved excuses keep theirs for the year; client B untouched (`Retention_job_purges_only_own_client_files`); a purged file answers 404 |
| 3.9 | Submit scope | Submit for a child that is not yours, for sessions of another child, for a session not Absent/Late, after `Excuses.MaxDaysAfterAbsence` (school days via `IClientCalendar`), a disallowed reason, a required attachment missing (`AttachmentRequiredReasons`) | Refused with the documented codes; covered sessions frozen at submission |
| 3.10 | Submitters (D61) | Student under `GuardiansOnly` | 403 `excuse_submitter_not_allowed`; `canSubmitExcuse` is resolved by the server and the client never reads the setting (L21) |
| 3.11 | State machine and reviewer | Approve/reject/escalate/withdraw out of order, twice, concurrently; the submitter as reviewer | `excuse_reviewer_is_submitter` 409; approve flips records to Excused in one transaction with change rows; reject needs a comment; no double notification |
| 3.12 | Scanner seam | `IFileScanner` registered as no-op; the call order | The hook runs **before** the object becomes readable and before the excuse is submittable; a scanner that rejects must make the upload fail closed; a **pre-production gate** row: a real scanner wired, or an owner sign-off on D65 for the release |
| 3.13 | Metadata | Strip/ignore EXIF GPS and PDF metadata on download? | Documented: stored as uploaded; not exposed in any DTO; (decision for the owner, record the answer) |
| 3.14 | Logs and audit | Upload, download, refuse | Ids only; no file names with personal data in logs; decisions audited |

## 4. Substitute-teacher attendance scope (L15, Tasks 15-17)

`IsSubstituteOn(sessionId)` grants `Attendance.Take` on **that one session, on the exception's date only**.

| # | Check | How | Expected |
|---|---|---|---|
| 4.1 | Scope is one session | Substitute for session S1 tries S2 (same class course, same day), S3 (same class course, next week), the class course's grade/gradebook routes, attendance history of S1's course, the student list of the class | S1 take/read: allowed; everything else 404; no `Grades.*`, no other course data |
| 4.2 | Date bound | Use the substitute on the day before, the day after, the same date in another time zone boundary (client at `Asia/Riyadh`, request just before/after local midnight) and with the fake clock moved | Allowed only on the exception's local date (`IClientCalendar`); 404 outside |
| 4.3 | Exception lifecycle | Exception cancelled, edited to another teacher, deleted, slot unpublished or moved | The old substitute loses access at once (no cache of "is substitute"); the new one gains it only for the new date |
| 4.4 | Record the substitute created | Substitute takes attendance, then loses access | The record/`AttendanceRecordChange` rows remain attributed to the substitute (audit); the teacher of record sees them |
| 4.5 | Edit window (L14) | Substitute edits after `Attendance.EditWindowHours` | 409 `attendance_edit_window_closed` for `Take`; only `Attendance.Edit` holders (Manager/Counselor) with a required reason, audited |
| 4.6 | Former and current teacher dates (L15) | `ClassCourseTeacher` active `From <= today <= To` on the **local** date | A former teacher (ended yesterday) gets 404; a teacher whose assignment starts tomorrow gets 404 |
| 4.7 | Impersonation of the exception | A teacher of another school creates/edits an exception for a slot they do not manage; a teacher names themselves as substitute | `Timetable.Manage` + unit access required; a teacher cannot create or edit exceptions (403); no self-assignment |
| 4.8 | Substitute in another client or unit | Exception naming a user of client B or of a sibling school | Refused; the user must be a teacher of the same client; probe row |
| 4.9 | Take on a future session or a cancelled one | Substitute takes attendance before the session, or on a cancelled (holiday/exception) session | 409 for future; cancelled/holiday sessions are not generated or are read-only |
| 4.10 | Counselor/other staff take | Counselor `POST` ad hoc session or take | 403 (`Attendance.Take` is teacher/substitute only) |
| 4.11 | Live-session attendance (L11) | Teacher of another course confirms/maps another course's live attendance; proposals for `Guest` rows | 404; only `Matched` (verified email of an enrolled student) are proposed; never display-name matching; a session with `ParticipantsImportedAtUtc` null proposes nobody Absent |
| 4.12 | Probe row | The substitute caller in the probe suite for every attendance route | 404 for every route except S1 take/read on its date |

## 5. Cross-client and cross-unit timetable reads (bypass sites 3 and 4, Tasks 13-16)

| # | Check | How | Expected |
|---|---|---|---|
| 5.1 | Teacher's own timetable (bypass site 3) | `GET /api/me/timetable` as a teacher with slots in two schools of client A, while client B has a teacher with the same user email or same name and overlapping hours | Only slots whose `TeacherUserId` is the caller (and substitute exceptions for the caller) **within the same `OrganizationId`**; test `Teacher_timetable_returns_only_own_slots_in_own_client`; never a row of client B or of a sibling client |
| 5.2 | `IgnoreQueryFilters` sites | Review every `IgnoreQueryFilters` in Tasks 13-16 | Each carries `// ISOLATION-BYPASS: <reason>`, an `IsolationBypassGuardTests` entry, and an explicit `OrganizationId` predicate; unlisted use fails the guard |
| 5.3 | Class and student timetable reads | `GET /api/classes/{id}/timetable`, `GET /api/students/{sid}/timetable`, `.../export` as other school's admin, other client's admin, other guardian, student of another class | 404 for every miss; drafts visible only with `Timetable.Manage`; a student/guardian sees published slots of own class only |
| 5.4 | Drafts and unpublished data | Student/guardian read while a timetable is draft, or a slot is unpublished | No draft slot, no teacher name from a draft, no exception text |
| 5.5 | Teacher names and rooms | Student timetable DTO | Teacher display name and room label (free text, D60) only; no teacher e-mail, user id of the teacher, or availability |
| 5.6 | Exceptions | Read exceptions of another class/school/client | 404; `GET .../exceptions` follows `UnitAccess` |
| 5.7 | Conflict validation across clients | `validate`/`publish` with a teacher who also teaches in client B (same human, two accounts) | Conflicts computed within the client only; the response never reveals a slot of another client ("teacher busy at that time in another school" is a leak) |
| 5.8 | Session generator (site 4) | Run `AttendanceSessionGenerator` for client A while client B has published slots | `Generator_for_client_a_leaves_client_b_untouched`; `Generator_never_writes_in_platform_context`; each created row carries the organization it was generated for; the platform list query returns organization ids only |
| 5.9 | Periods and working days | Unit override of periods; a lower unit's override read by a sibling | Periods are per unit and overridable by a lower unit only for that unit; sibling/other client cannot read or write them (`UnitAccess`) |
| 5.10 | `Org.TimeZone` | Client A in `Asia/Riyadh`, client B in another zone (test uses `Europe/London` for DST) | Local day, working day, holiday and edit-window decisions use **the caller's client** calendar (`IClientCalendar`); a generation run for A never uses B's zone; DST gap/overlap tested |
| 5.11 | Holiday and calendar events | A holiday of client B's year | Never affects client A's generation (`CalendarEvent` kind `Holiday` of the unit's own year) |
| 5.12 | Copy-term and copy-from | Copy a timetable from a term/class of another school or client | Refused/404; only within the unit's subtree and client |
| 5.13 | Export | Timetable export file | Only the caller's class/course slots; cells go through `SpreadsheetCell.Safe` (section 2) |
| 5.14 | Isolation tests | One per new table | `Gradebook`, `GradeCategory`, `GradeItem`, `GradeEntry`, `GradeSnapshot`, `GradingScale`(+`Band`), `WeightTemplate`(+`Item`), `Period`, `TimetableSlot`, `TimetableException`, `AttendanceSession`, `AttendanceRecord`, `AttendanceRecordChange`, `LiveAttendanceMapping`, `AbsenceExcuse`, `AbsenceExcuseSession`, `AttendanceAlert` each have `<Entity>_is_isolated_per_client_and_subtree` and are listed in the M5 isolation guard |

## 6. Jobs and system context per client (Review Focus 4, bypass/system sites 4-9)

| # | Check | How | Expected |
|---|---|---|---|
| 6.1 | `ScheduledPublishJob` (5) | Two clients with due items | `Scheduled_publish_runs_each_client_in_its_own_context`; idempotent when run twice; a job failure for A does not block B |
| 6.2 | Live attendance job and reconcile sweep (6) | Payload with a foreign organization id | `Live_attendance_job_with_a_foreign_org_payload_does_nothing`; the organization id from the payload is re-checked against the `LiveSession` row under that context; the sweep loops organizations; the sink only enqueues an idempotent `attendance-live:{sessionId}` job and returns |
| 6.3 | `AttendanceThresholdJob` (7) | Records of A and B | Counts only own client's records; alerts created only in own client |
| 6.4 | `ExcuseAttachmentRetentionJob` (8) | See 3.8 | Own client only |
| 6.5 | Preset reconciler (9) | Seed version bump | Never touches edited copies or other clients (`Preset_reconciler_never_touches_edited_copies_or_other_clients`) |
| 6.6 | Platform-context writes | Architecture/runtime guard | No job writes rows with `OrganizationId` empty; a system-context site not in the plan's table fails review |
| 6.7 | Job keys and scheduling | Keys like `attendance-live:{sessionId}` | Unique per session; no key built from user input; payloads carry ids only |

## 7. Local days and time (Review Focus 5, L2)

| # | Check | How | Expected |
|---|---|---|---|
| 7.1 | One calendar service | Grep for `DateTime.UtcNow.Date`, `ToLocalTime`, `TimeZoneInfo` outside `IClientCalendar` in Tasks 13-20 | None; generation, `GenerateDaysAhead`, the edit window, excuse deadlines, threshold windows and publish-at displays all use it |
| 7.2 | Boundary tests | Fake clock at 23:59:59/00:00:00 local, DST zone | Window opens/closes on the local boundary, not UTC |
| 7.3 | Server clock | Every timed response carries `serverNowUtc`; the client's clock is never trusted | A request with a manipulated client time header/body is ignored |

## 8. Guardians, roles and permissions (L16, Task 40 oracle)

| # | Check | How | Expected |
|---|---|---|---|
| 8.1 | Route/permission oracle | Task 40 probe rows: every row of the route table with the **wrong caller** column | 403 for a permission miss, 404 for a relationship miss, exactly as the table says; no route without `RequirePermission` or `IAccessEvaluator`; the M5 endpoint matrix lists every new route |
| 8.2 | Built-in role matrix | Compare `BuiltInRoleDefaults` with the plan matrix (8 -> 9 -> 10 permissions waves) | Exact match; a test pins it so a role gain fails the build; **Counselor has no `Grades.*`**; Supervisor only own supervised courses; Student `Excuses.Submit` effective only under `GuardiansAndStudents` |
| 8.3 | Guardian reading attendance and excuses | Guardian of two children swapping ids; guardian of a withdrawn child | Identical 404; withdrawn/inactive link stops reads immediately |
| 8.4 | `Attendance.Absent` notification | Mark absent | Grouped per guardian per session, only on Taken/confirmed; text carries no reason or excuse comment |
| 8.5 | Settings | `Grades.*`, `Timetable.*`, `Attendance.*`, `Excuses.*`, `Org.TimeZone`, `AcademicYears.TermsPerYear` writes | `Settings.Update` + `UnitAccess`; client-level keys only at the root; teacher 403 |
| 8.6 | Concurrency and idempotence | Double publish, double approve, double submit | One effect, one audit row, one notification |

## 9. Results (QA3, Task 41, first pass, 2026-11-14)

Legend: **OK** = verified, evidence named; **OK\*** = holds by code or by another reviewer's tests, not re-run by QA3; **GAP** = no automated test yet (the behaviour was read in code, listed in section 10 as test debt); **BUG** = a defect, filed (section 10); **BLOCKED** = the code the row needs is not implemented or not merged yet.
"Evidence" names the test or the review comment. Local runs were made by QA3 on the sha shown (build `-warnaserror`, Testcontainers).

### 9.1 What was reviewed

| PR | State (2026-11-14) | Sha reviewed | QA3 verdict and local run |
|---|---|---|---|
| #625 wave 1 contract | merged | ed73185d (draft), merged cbc7cc13 | contract shapes reviewed; QA1 approved the merge |
| #651 wave 1b contract | merged | 17185503 | delta checked (Withdrawn staff-only marker), contract unit tests 273/273 |
| #641 scales, #631 engine, #633 terms | merged | not reviewed by QA3 | OK\* (QA1/QA2 approvals) |
| #638 gradebook model | open, approved | not reviewed by QA3 | OK\* |
| #646 published-only reads | open, approved | e19704c8 | QA3 ran GradeLeak, PublishedGradeReads, GradeReport, M3 matrix and GradeReads suites 219/219, plus the guard mutation (9.2) |
| #650 grid | open, approved | not reviewed by QA3 | OK\* (QA1/QA2) |
| #656 publishing, snapshots | open, approved | 4f0d7149 | unit 718/718, integration 2117/2117 |
| #718 column import/export, report Excel | open, approved | b23fd0c5 | unit 741/741, integration 2131/2131 plus XXE and zip-bomb probes |
| #643 timetable slots | open, approved | 7a1d5583 | unit 674/674, integration 1856/1856 |
| #653 exceptions, own views | open, approved | be5fc74c | unit 675/675, integration 2128/2128 |
| #657 attendance sessions | open | 40c6ba4c (via #661) | QA2 reviewed; QA3 ran it inside #661 |
| #661 take, edit window, history | open, approved | d73f87fb | unit 741/741, integration 1937/1937 (changed files) |
| #719 alerts, report Excel | open, approved | 045f5422 | unit 751/751, integration 2280/2280 |
| #720 live attendance proposal | open, approved | 22835d8b | one-line delta read; QA2 ran 9cac4838 |
| #635 excuses | open, approved | 25a28391 | unit 780/780, integration 2259/2259 |

### 9.2 Section 1: grade-leak paths

| Rows | Status | Evidence |
|---|---|---|
| 1.1.1 helper is the only path | **OK** | `PublishedGradeReadsGuardTests` (2 tests). **Mutation done by QA3:** a student route reading `Set<GradeItem>` makes the guard fail (1 failed, restored). The guard is a source scan of the two student-facing files; a new student route in a new file is not covered until it is added to its list (see F8) |
| 1.1.2 preview staff-only | **OK** | `Student_cannot_call_the_staff_preview`, `Teacher_of_another_class_gets_404_from_the_preview_and_no_item_titles`, `Staff_preview_equals_the_student_view` (#646) |
| 1.1.3 route guard | OK\* | per-route `RequirePermission` is enforced by the M5 endpoint matrix of #624 (draft); until it merges the guard covers only the two files above |
| 1.1.4 totals from published only | **OK** | `Hidden_item_never_changes_category_percent_final_or_counts`, `Student_sees_only_published_items_and_totals_from_them`; golden `published-only-hide.json` |
| 1.2.1 list, 1.2.6 auto items, 1.2.7 scheduled | **OK** | `Student_response_never_carries_a_hidden_or_scheduled_item`, `Guardian_response_never_carries…`, `Scheduled_item_is_hidden_until_due`; #656 `Scheduled_item_is_published_by_the_job_once` |
| 1.2.2 query parameters | **OK** | `GradeLeakAttackTests.No_query_parameter_widens_what_a_student_or_a_guardian_sees` (16 variants: include, expand, items, all, status, state, sort by a hidden title, fields, paging past the end, asStaff, preview, q) and `A_bad_term_never_turns_into_a_leak_or_a_server_error`; only the one published item ever comes back |
| 1.2.3 hidden item by id | OK | no student route takes an item id (nothing to probe); staff item routes are relationship-scoped (#650 OK\*) |
| 1.2.4 statistics fields | **OK** | contract test pins staff-only property names incl. `Withdrawn` (#651 `GradingContractTests`) |
| 1.2.5 newer-score marker | **OK** | `Newer_score_marker_never_reaches_students` |
| 1.2.8 un-publish | **OK** | #656 `Unpublish_hides_again_and_is_audited` and `GradeLeakAttackTests.Hiding_a_published_item_takes_effect_on_the_next_read_and_publishing_again_brings_it_back` (student and guardian, the very next read) |
| 1.2.9 provisional scale | **OK** | #656 `Publish_under_a_provisional_scale_is_409_without_acknowledgement_and_the_acknowledgement_is_audited` |
| 1.2.10 lock reads | BLOCKED | Task 12 (lock) not built; #656 rules a manual publish on a locked gradebook is allowed and tests it |
| 1.2.11 snapshots | **OK** | no route reads `GradeSnapshot` (nothing to leak); `ValuesJson` is `[AuditIgnore]` (#656 test) |
| 1.2.12 guardian of two children, 1.2.13 link state | **OK** | `Guardian_of_two_children_cannot_read_one_under_the_other_id`, `Inactive_guardian_link_is_404`; #635/#653 re-check the live link on every read |
| 1.2.14 former/substitute staff | OK\* | a substitute holds the attendance relationship only (#653/#661 tests); former teacher 404 relies on #650 |
| 1.2.15 supervisor, counselor | OK\* | `BuiltInRoleMatrixTests` pins "Counselor has no Grades.*" |
| 1.2.16 side channels | **OK** | `Changing_only_hidden_data_changes_nothing_a_student_can_observe`: after changing a hidden score and adding four hidden items the student's courses are deep-equal and ETag, Last-Modified, Cache-Control, Retry-After, Link are unchanged. Timing is not measured |
| 1.3.1-1.3.3 error bodies and headers | **OK** | `A_guardian_asking_for_another_childs_id_gets_the_answer_of_an_id_that_never_existed` (status, body and stable headers identical to an unknown or malformed id), `Staff_only_routes_answer_a_student_and_a_guardian_without_any_grade_data` (grid, preview, export, item, reports, as student, guardian and foreign guardian) |
| 1.4.1 notification text | **OK** | #656 `Notification_contains_no_grade_value_or_item_title`; the event declares only `CourseNameAr/En` |
| 1.4.2 resource placeholder test | GAP | no resource test over grade notification texts (F8) |
| 1.4.3 scheduled/un-publish notices | **OK** | notices at publish time only; hide sends none (#656) |
| 1.4.4 logs | GAP | no log-capture assertion (F8) |
| 1.4.5 audit details | **OK** | `GradeEntry` is `[Audited]` with Points (Lead's ruling); before/after only via `GET /api/audit/{id}` (`Audit.View`, unit subtree; Org Admin and Manager); list and export carry none; summaries carry ids and counts (QA3 note on #656) |
| 1.4.6 recipients | **OK** | `One_notification_per_student_per_batch_for_the_student_and_the_guardian`, `A_student_who_left_the_course_gets_no_snapshot_and_neither_notice_nor_guardian_notice` |
| 1.5.1 Cache-Control | **BUG F1** | no `Cache-Control` on any API response except the library reader; no response-cache middleware is registered, so no shared cache can serve another user, but the checklist requires `private, no-store` |
| 1.5.2 cache after un-publish | OK | no cache layer |
| 1.5.3 reports | OK\* | #646 report tests (teacher/counselor 403, supervisor scope); 300 ms budget and row cap added by QA2's findings |
| 1.5.4 Scale sheet | **OK** | `GradeReportExcel` (bands and descriptors only, escaped; #718 unit test) |
| 1.5.5 student exports | **OK** | `A_student_route_never_returns_a_file` (format=xlsx/csv, download, /export, /export.xlsx: JSON or 404, never a file type or an attachment) |
| 1.5.6-1.5.8 search, print, publishAt | **OK** | no such routes; student DTOs carry no `publishAt` (contract test) |
| 1.6.1 only `IGradebookWriter` writes entries | **BUG F3** | #718 `GradeColumnExchange.ApplyAsync` adds and sets `GradeEntry` rows directly (after `EnsureWritableAsync`); no architecture test enforces the rule |
| 1.6.2 optimistic concurrency | **OK** | #650/#718 (version-bound apply, `changed_since_preview`); current value only to staff |
| 1.6.3 reads write nothing | **OK** | `Read_never_writes_rows` |
| 1.6.4 mass assignment | GAP | request DTOs have no `published/lockedAtUtc/source` fields (code read); no test sends them |
| 1.6.5 event scores | **OK** | #718 `import to an event item is 400` |

### 9.3 Section 2: spreadsheets (#718 b23fd0c5, #719 045f5422)

| Rows | Status | Evidence |
|---|---|---|
| 2.1 formula injection | **OK** | `SpreadsheetCellTests` (escaping theory for `= + - @`, tab, CR); `The_export_neutralises_formula_names` (#719: five columns, data type Text, no formula). Full-width `＝` and leading spaces are not formulas in Excel and are left as typed |
| 2.2 one helper | **BUG F4** | three copies of the rule: `SpreadsheetCell.Safe` (#718), `AuditExporter` (until #718 merges) and `AttendanceReportExporter.Safe` (#719); no architecture test |
| 2.3 HYPERLINK/DDE titles | **OK** | same helper; stored as text |
| 2.4 `.xlsx` only | **OK** | extension, zip magic, `xl/workbook.xml` and `[Content_Types].xml`, no `vbaProject.bin`/embeddings/ActiveX/macrosheets/`macroEnabled`; `.xlsm`, `.xls`, csv renamed all 400 (#718 tests) |
| 2.5 limits and zip bomb | **OK** | after b23fd0c5: 100 parts, 20 MB inflated, 100:1 ratio, counted by really inflating; 2000 rows, 50 columns, 30 s budget. QA3 probe before the fix: a 456 KB upload inflated to 150 MB (4.8 s, 133 MB) and was accepted; the fix is tested by `A_small_file_that_expands_hugely_is_refused` |
| 2.6 XML safety | **OK** | QA3 probe: DOCTYPE with an external entity in the sheet and in the workbook part, and a nested-entity expansion, are all `unreadable`; no file content reached a row |
| 2.7 external links | **OK** | ignored, never fetched (Lead/BE1 decision accepted by QA3) |
| 2.8 token | **OK** | bound to uploader, gradebook, item; every failure is `grade_import_expired`; a replay is harmless because each row carries the entry version (`A_token_cannot_be_applied_twice`). The file SHA-256 is in the audit, not verified at apply (apply has no file) |
| 2.9 apply revalidates | **OK** | resized item, left student, excused, locked (409) |
| 2.10 row authorization | **OK** | unknown number is a row error, nothing about other students; a number over 50 characters is never echoed |
| 2.11 type confusion | **OK** | `1E+30`, NaN, text: row errors |
| 2.12 event scores | **OK** | 400 |
| 2.13 audit | **PARTIAL (F5)** | export, preview and apply are audited with ids, counts and the file hash; a **refused** upload (bad type, bomb) is not audited |
| 2.14 export scope | **OK** | relationship scoped, counselor 403, no students outside the class course |
| 2.15 headers | **BUG F1** | file name is server-made (`grades-{id}.xlsx`); `nosniff` comes from the global headers; `Cache-Control` missing (F1) |
| 2.16 streaming | OK (accepted) | the upload is buffered but bounded (`Files.MaxUploadMb`, 20 MB inflated) |

### 9.4 Section 3: excuse attachments (#635 25a28391)

| Rows | Status | Evidence |
|---|---|---|
| 3.1 allow-list by content | **OK** | `ExcuseAttachmentValidatorTests`; at submit the stored bytes are re-read and validated (`An_attachment_is_checked_by_its_content_at_submit…`). A polyglot with a valid magic prefix passes the sniff by design and is safe only because it is never served inline (3.3, 3.7) |
| 3.2 size | **OK** (follow-up F6) | cap checked after the object is downloaded to a spool file; check `StoredFile.SizeBytes` first |
| 3.3 AV off | **PARTIAL** | the scanner is off by default (`The_scan_option_defaults_to_off`) and runs when enabled; "download only, `attachment`, `nosniff`" is BLOCKED by 3.5. v1 residual risk accepted by D65 until a real scanner is wired |
| 3.4 storage | OK\* | per-client bucket (`MinioFileStorage`), server-made object key; code read |
| 3.5 signed URL, 3.6 who can read, 3.7 inline preview | **BLOCKED F2** | `GET /api/excuses/{id}/attachment-url` is declared in the contract but **not mapped** in #635 or #719; #644 (counselor UI) works against a fake. The client side (#644) is reviewed: inline only for jpeg/png/webp, PDFs download only, no iframe |
| 3.8 retention job, 6.4 | **BLOCKED F2** | only the setting `Excuses.AttachmentRetentionDays` exists; no `ExcuseAttachmentRetentionJob` |
| 3.9 submit scope | **OK** | `ExcuseSubmitTests`: strangers 404, other child, not Absent/Late, deadline in school days, reasons, required attachment; absences older than 365 days are "too late", not a 500 |
| 3.10 D61 | **OK** | `Student_submit_follows_the_submitters_setting_and_strangers_get_404`; the client reads `canSubmit` from the server |
| 3.11 state machine, reviewer | **OK** | version token (`A_second_decision_on_a_stale_excuse_loses…`, `Approve_racing_withdraw_has_one_winner…`), reviewer is not submitter, reject needs a comment, approve flips records in one save. Open: approve reads the records through the reviewer's own scope (F6) |
| 3.12 scanner seam | **PARTIAL** | the validator runs before the excuse exists; the object was already stored by the general upload route. Pre-production gate: a real scanner or owner sign-off on D65 |
| 3.13 metadata | OPEN (owner) | stored as uploaded, never in a DTO |
| 3.14 logs, audit | OK\* | ids only (code read) |

### 9.5 Section 4: substitute scope (#653 be5fc74c, #661 d73f87fb, #720 22835d8b)

| Rows | Status | Evidence |
|---|---|---|
| 4.1 one session | **OK** | `The_substitute_takes_only_that_session_on_that_date` (read, save, history on that session; 404 for the same slot's other dates, other sessions, the ad hoc session, an ordinary teacher) |
| 4.2 date bound | **OK** | `A_session_dated_tomorrow_in_the_client_zone_cannot_be_taken_and_today_can`; the relationship ends at client-local midnight |
| 4.3 lifecycle | **OK** | exception changed to Cancelled or deleted removes access at once (same test) |
| 4.4 attribution | OK | history rows carry the substitute's id |
| 4.5 edit window | **OK** | `Teacher_edit_after_the_window_is_409_and_a_counselor_edits_with_a_reason` |
| 4.6 former/current teacher dates | OK\* | #657 (QA2) |
| 4.7 impersonation | **OK** | exception routes need `Timetable.Manage` + unit access; substitute must be another active teacher of the school, never the teacher of record (`Substitute_rules`) |
| 4.8 other client/unit | **OK** | other-client user refused; sibling-school substitute reads via a marked, own-id-only bypass (`A_substitute_assigned_in_a_sibling_school_still_sees_the_lesson_on_that_date`) |
| 4.9 future/cancelled | **OK** | future is 409 (`attendance_session_future`); holidays are not generated (#657) |
| 4.10 counselor take | **OK** | `Another_teacher_gets_404_and_a_student_is_refused` (counselor reads, cannot take) |
| 4.11 live attendance | **OK** | #720: confirm and mapping 404 for another teacher; only `Matched` participants proposed; nothing proposed until participants are imported |
| 4.12 probe rows | GAP | substitute caller rows are in #624 `M5RelationshipProbeTests` (draft); today only the tests above cover it |

### 9.6 Sections 5-8

| Rows | Status | Evidence |
|---|---|---|
| 5.1 own timetable | **OK** | `TimetableViewTests` (own slots through the caller's filters; covered lessons by a marked bypass limited to the caller's own substitutions and date) |
| 5.2 bypass sites | **OK** | every `IgnoreQueryFilters` read in #639-#661/#635 carries the marker, the client predicate and a guard entry (checked per PR) |
| 5.3-5.6 reads, drafts, DTO, exceptions | **OK** | `Other_school_admin_gets_404_and_the_table_is_isolated`, `A_draft_slot_never_reaches_a_view`, guardian own-children only; cards carry names, no ids, no notes |
| 5.7 conflicts across clients/schools | **OK** | `A_clash_with_an_unreadable_slot_names_only_its_kind_and_another_client_never_clashes` |
| 5.8 generator | OK\* | #657 `Generator_for_client_a_leaves_client_b_untouched`, `Generator_never_writes_in_platform_context` (QA2) |
| 5.9 periods | **OK** | `Periods_never_cross_clients`, `Periods_inherit_and_a_lower_unit_overrides` |
| 5.10, 7.2 time zone and boundaries | **OK** | `ClientCalendarTests` (Europe/London DST, 21:30 UTC rolls to the next Riyadh day), edit window, thresholds' 30 day window ends at the client date |
| 5.11 holidays | OK\* | calendar is read per unit/year and client |
| 5.12 copy-term, 5.13 timetable export | BLOCKED | routes not mapped yet (Task 14 part 2) |
| 5.14 isolation test per new table | **OK** | each new table has one (Period, TimetableSlot, TimetableException, AttendanceSession/Record/Change, AttendanceAlert, LiveAttendanceMapping, AbsenceExcuse/Session, GradeSnapshot; earlier tables by #641/#638/#650) |
| 6.1 scheduled publish | **OK** | per client, two runners announce once (#656) |
| 6.2 live job | **OK** | `Live_attendance_job_with_a_foreign_org_payload_does_nothing` (#720) |
| 6.3 threshold job | OK\* | #719 (QA2) |
| 6.5-6.7 reconciler, platform writes, job keys | OK\* | #641, #657 |
| 7.1 one calendar service | **OK** | `LocalDayConversionGuardTests` covers the M5 folders; the one miss QA3 found (escalation targets used the UTC date) was fixed (9d6e928c) |
| 7.3 server clock | OK\* | DTOs carry the server time; the client clock is never an input |
| 8.1 oracle | PARTIAL | per-PR probe rows exist for every mapped route; the M5 matrix and relationship probes are #624 (draft) |
| 8.2 role matrix | **OK** | `BuiltInRoleMatrixTests` |
| 8.3 guardian reads | **OK** | #646/#653/#635 tests |
| 8.4 absence notice | OK\* | #657 |
| 8.5 settings | OPEN | `Org.TimeZone` unknown-id write validation (QA3 #639/#621 item) is not verified as done |
| 8.6 idempotence | **OK** | double publish (#656), double approve/withdraw (#635), duplicate-key races mapped (#653) |

### 9.8 Independent attack tests (QA3, written for this review)

The tests were written by QA3, not by the authors of the code, and each file was checked by mutation (a guard in the code under test was removed or weakened and the file had to fail). They are on branches pushed to the repository and need the unmerged stack, so they join main when the chains do (not in this PR, which is documents only):

| Branch (tip it builds on) | File | Tests | Covers | Mutation check |
|---|---|---|---|---|
| `qa/706-attack-sheet` (#718 b23fd0c5) | `tests/Lms.UnitTests/Spreadsheets/SpreadsheetAttackTests.cs` | 58 | 2.1-2.12: twelve formula and DDE strings through the column export (quote prefix, no `<f>`), refused uploads by content (html, csv, OLE, png, pdf, truncated zip), forbidden parts in six spellings, macro content types, zip bombs (total, ratio, part count, million-row range), DTD/XXE with a canary file and a URL, entity expansion, external links and defined names, formulas and text in number cells, absurd numbers, long student numbers never echoed, hidden first sheet | Safe disabled (11 fail), bomb check dropped (2), entry cap dropped (1), number length (1), forbidden parts (7) |
| `qa/706-attack-att` (#635 25a28391) | `tests/Lms.UnitTests/Excuses/ExcuseAttachmentSniffAttackTests.cs` | 32 | 3.1-3.3, 3.12: 20 refused prefixes (WAVE/AVI RIFF, late or lower-case PDF, partial PNG/JPEG, GIF, SVG, HTML, XML with DTD, exe, ELF, zip, script), polyglot typed by its first bytes, hostile file names, exact cap, zero byte, scanner off or fail-closed, stream rewound | WEBP check weakened (2 fail), cap off by one (1) |
| `qa/706-attack-att` | `tests/Lms.IntegrationTests/Excuses/ExcuseAttachmentAttackTests.cs` | 7 | 3.1, 3.2, 3.9: refused types and sizes at submit leave no excuse, another client's file with the same user id, reuse of a file, a stranger learns nothing from the answer, no URL, key or file id in any answer | validator call removed (2 fail) |
| `qa/706-attack-att` | `tests/Lms.IntegrationTests/Security/SubstituteScopeAttackTests.cs` | 5 | 4.1-4.3, 4.7, 4.8: 18 routes closed while the cover is live (grading, ad hoc sessions, other sessions, history, reports, alerts, timetable publish), self-assignment, past-date cover, cover named for another user, foreign client's teacher | date check removed from the rule (1 fail); the user check is a second layer in the endpoint, so removing it from the rule alone does not fail |
| `qa/706-attack-att` | `tests/Lms.IntegrationTests/Security/TimetableCrossClientAttackTests.cs` | 5 | 5.1-5.3, 5.6, 5.7, 5.9: ten routes of the other client closed in both directions, a real foreign id answers as an unknown id (status, body, headers), own timetable carries nothing of the other client, a foreign teacher's slot leaks no clash, validate and read describe only the own client | slot access check removed (4 fail) |
| `qa/706-attack-grade` (#656 4f0d7149 + #646 e19704c8) | `tests/Lms.IntegrationTests/Security/GradeLeakAttackTests.cs` | 29 | 1.2.2, 1.2.8, 1.2.16, 1.3.1-1.3.3, 1.5.5: 16 query variants, bad terms, hidden-data changes invisible (courses deep-equal, stable headers), hide and re-publish take effect on the next read, foreign child's 404 identical to an unknown id, staff routes closed to student and guardians, never a file | published-only flag flipped in `PublishedGradeReads` (21 fail) |

Findings from writing them: the substitute (any Teacher) can read the class timetable and the exception list, including the staff note (F11); everything else held.

### 9.7 Sign-off

| Section | Reviewer | Sha | Evidence | Blocked rows |
|---|---|---|---|---|
| 1 Grade-leak paths | QA3 | #646 e19704c8, #656 4f0d7149, #718 b23fd0c5 | 9.2 | 1.2.10 (Task 12); test debt F8 |
| 2 Spreadsheets | QA3 | #718 b23fd0c5, #719 045f5422 | 9.3 | none; F4, F5 open |
| 3 Excuse attachments | QA3 | #635 25a28391 | 9.4 | 3.5-3.8 (F2) |
| 4 Substitute scope | QA3 | #653 be5fc74c, #661 d73f87fb, #720 22835d8b | 9.5 | none; 4.12 waits for #624 |
| 5 Cross-client timetable reads | QA3 | #643 7a1d5583, #653 be5fc74c | 9.6 | 5.12, 5.13 |
| 6 Jobs per client | QA3 | #656, #720, #719 | 9.6 | 6.4 (F2) |
| 7 Local days | QA3 | #639/#643, #661, #635 | 9.6 | none |
| 8 Guardians and roles | QA3 | #646, #653, #635 | 9.6 | 8.1 waits for #624; 8.5 open |

## 10. Findings and test debt (filed as issues)

| Id | Sev | Finding |
|---|---|---|
| F1 (#736) | medium | No `Cache-Control: private, no-store` on authenticated API responses (grade reads, exports, attachment URLs). Add one middleware for `/api` (rows 1.5.1, 2.15) |
| F2 (#737) | high | Excuse `attachment-url` endpoint and the attachment retention job are not implemented (rows 3.3 to 3.8, 6.4); the counselor UI (#644) runs on a fake |
| F3 (#738) | medium | #718 apply writes `GradeEntry` rows directly instead of through `IGradebookWriter`; add the architecture test (row 1.6.1) |
| F4 (#739) | low | Three copies of the spreadsheet formula-escape rule; use `SpreadsheetCell.Safe` everywhere and add an architecture test (row 2.2) |
| F5 (#740) | low | A refused grade import (bad type, bomb) is not audited (row 2.13) |
| F6 (#741) | medium | #635: approve reads covered records through the reviewer's own scope (count check missing); check the stored file size before downloading it; keep the sniffed content type with the excuse |
| F7 (#742) | medium | #644: the signed attachment URL expires after 5 minutes and the drawer never refreshes it; the content security policy has no `img-src` for the storage origin |
| F8 (#743) | low | Task 41 test debt, mostly paid (9.8): remaining are log-capture assertions (1.4.4), the grade-notification resource placeholder test (1.4.2), mass-assignment bodies on the grade writes (1.6.4), the lock reads (Task 12) and the guard file list of `PublishedGradeReadsGuardTests` for new student-facing routes |
| F9 (#744) | low | #661: a teacher's take accepts `Excused` directly (bypasses the excuse flow); decide with Task 19 |
| F10 (#745) | low | #656: guardian recipients when `Features.GuardianPortal` is turned off after activation; `Org.TimeZone` unknown-id write validation (row 8.5) |
| F11 (#755) | low | Every teacher of the school can read the staff `Note` of a timetable exception (`Timetable.View`); found by the substitute attack tests |
