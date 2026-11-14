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

## 9. Sign-off

Fill in at review time (one line per section, each with the commit sha reviewed, the test names run, and the list of rows marked blocked):

| Section | Reviewer | Sha | Evidence (tests / probes) | Blocked rows |
|---|---|---|---|---|
| 1 Grade-leak paths | | | | |
| 2 Spreadsheets | | | | |
| 3 Excuse attachments | | | | |
| 4 Substitute scope | | | | |
| 5 Cross-client timetable reads | | | | |
| 6 Jobs per client | | | | |
| 7 Local days | | | | |
| 8 Guardians and roles | | | | |

Files this review produces (Task 41): `tests/Lms.IntegrationTests/Security/GradeLeakAttackTests.cs` (section 1, with the mutation check of 1.1.1), `SpreadsheetAttackTests.cs` (section 2), `ExcuseAttachmentAttackTests.cs` (section 3), substitute and timetable cases in the Task 40 probe suite. Findings are filed as `bug` + `sev:*` against the owning lane.
