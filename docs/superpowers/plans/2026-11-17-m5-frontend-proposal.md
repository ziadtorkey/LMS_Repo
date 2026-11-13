# M5 frontend proposal: gradebook, timetable, attendance, excuses

> **Status: DRAFT proposal for the Team Lead (no code).** Author: FE2. Frontend half of M5, paired with BE4's backend proposal (`2026-11-17-m5-backend-proposal.md`, PR #611) and written in the format of the M4b frontend proposal (#440). Task numbers (`H1`...`H18`) are local; the Lead renumbers them. Owner decisions applied: **D58** (all #611 defaults), **D59** (Saudi grading presets, GPA, two or three terms), **D60** (rooms are free text), **D61** (excuse submitters and alert defaults).
>
> **Sources read:** #611 (all sections), PRD FR-GRD-01..06, FR-TT-01..06, FR-ATT-01, FR-EXC-01..04, X-01, X-05, X-10, D58 to D61, `docs/DESIGN.md`, the Stitch references in `docs/design/stitch/`, and the client code this builds on (section 1).

## 0. Scope and shape

M5 gives four audiences four jobs:

| Audience | Jobs |
|---|---|
| **Teacher** | Run the gradebook of a class course (grid, categories, weights, overrides, import and export, publish), see the timetable, take attendance fast, confirm live-session attendance. |
| **Student** | See own published grades, own timetable and attendance, submit an excuse when the client allows it (D61). |
| **Guardian** | Same, per child, published grades only (D58 Q18); submit excuses (default submitter, D61). |
| **Admin and staff** | Grading scales and weight templates with the Saudi presets (D59); periods, working days and the timetable builder; the counselor excuse queue and alert settings; supervisor reports. |

What the frontend deliberately does **not** do:
- Compute any grade. The grid, the student view and the reports render what the server's one engine returns (#611 A2); the client never sums, weights or rounds. The only client arithmetic is a *preview* of the weights total (it is a sum of integers the user is typing).
- Lock or unlock grades (M7, FR-GRD-06). The grid shows a "locked" banner when the server says so, and nothing more.
- PDF report cards (M7, D58). Excel only.
- A room list or room clash check (D60). Room is a plain text field.
- Per-teacher availability limits (D58).
- Message delivery or quiet hours (M6). M5 shows in-app notification labels only.

### Design and conventions (every task, same as M4a and M4b)
- `docs/DESIGN.md` and the lms-ui skill, on the Stitch tokens (Serene Academic Emerald): `PageHeader`, `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, one filled primary action per page, `lms-stats` tiles, `lms-status-chip`, `lms-stack-table` labelled cards at 360 px, `lms-save-bar` for unsaved settings forms, logical CSS, Rounded icons, mirrored direction icons, ISO dates (`yyyy-MM-dd`, left to right).
- Never hard-code hex values. Grade and attendance states use the **existing palette roles** (Success, Warning, Error, Info) plus a text label, so colour is never the only signal (section 8).
- ar and en resource entries for every key (parity test), bUnit tests first, a `Page_renders_rtl_in_arabic` per page, screenshots in `docs/screenshots/pr-<name>/` at 1440 and 360 px in ar and en (light and dark), 0 warnings, files under about 300 lines.
- API clients follow the M3/M4 pattern: `I<Module>Api` + HTTP implementation using `Routes.*` and `ProblemCodes.*` from `Lms.Shared` + an in-memory fake registered only when `Lms:UseFakeApi=true`. The fakes mimic the server rules the UI depends on (publish visibility, edit window, conflict mode).
- Server time: every window or deadline comparison uses `serverNowUtc` from the response (the M4a countdown pattern). The edit window of attendance and the excuse deadline are examples.
- Resource key prefixes: `Grd_*`, `Scale_*`, `Tt_*`, `Att_*`, `Exc_*`, `Error_<code>` per new problem code, `Notif_*` / `NotifEvent_*` for the new events.

## 1. What M5 builds on (verified on main, 2026-11-17)

| Seam | Where | M5 use |
|---|---|---|
| Page chrome and tables | `Components/Ui/*` (`PageHeader`, `SectionCard`, `EmptyState`, `LoadingState`, `DetailItem`), `ServerTable` | Every M5 page. |
| Class course shell | `ClassCourseTabs` (Plan, Forum, Announcements, Live sessions), role-aware | New tabs **Gradebook** (Grades.Manage) and **Attendance** (Attendance.Take); a teacher or supervisor sees them, students do not. |
| Terms and years | `Pages/AcademicYears/*` (`TermsPanel`, `TermDialog`, `YearDialog`) | The term picker of the grid and timetable reads the year's terms. The **terms-per-year option and the term template of D59** extend `YearDialog`/`TermsPanel` (task H17). |
| Unit settings | the Stitch settings pages (`unit_settings_*`, `lms-save-bar`) | New settings groups: grades (category method, missing policy), timetable (working days, conflict mode), attendance (edit window, live thresholds), excuses (submitters, policy, alert). |
| Teaching and parents | `Teaching/MyClassCoursesPage`, `Me/MyChildrenPage`, `Guardians/*`, `Me/MyStudentProfilePage` | Entry points: "My grades", "My timetable", "My attendance" for students; per-child tabs for guardians. |
| Notifications | `NotificationText`, `NotificationLabelParityTests` | New labels in ar and en (the parity test fails until they exist). |
| Live sessions (M4b) | `LiveSessionPanel`, `LiveParticipantsDialog`, `LiveSessionsList` | The attendance proposal is shown from the live session and from the attendance session list (H10). |
| Forms | `FormFlush` + `form-flush.js` (#536), `CloseOnEscapeKey` + `dialog-focus.js` | The grid's inline cell edit flushes before navigating or publishing; dialogs get Esc and focus return. |
| Uploads | `AssetUploader`, `ApiHttp.UploadAsync` | Excuse attachment; grade column import file. |
| Excel | server side only | The client downloads a file the server renders (`download.js`). |

## 2. Permissions and rules the UI keys off (finalised by the Lead's wave contracts, #611 section 8)

`Grades.View`, `Grades.Manage`, `Grades.Publish`, `Grades.ConfigureScales`, `Grades.ViewReports`, `Timetable.View`, `Timetable.Manage`, `Timetable.Configure`, `Attendance.Take`, `Attendance.Edit`, `Attendance.View`, `Excuses.Submit`, `Excuses.Review`, `Excuses.Escalate`.

The UI hides entries the caller lacks the permission for and **never** relies on that: the server answers 403 or 404. Relationship rules (teacher of record, substitute on one session, guardian link, counselor subtree) are server side; the UI shows what the server returns and maps a 404 to the standard not-found state.

## 3. Pages and components

### 3.1 Gradebook (teacher, supervisor)

**Route** `/class-courses/{Id}/gradebook?termId=`: `ClassCourseTabs` tab, term picker (defaults to the current term), `PageHeader` with the scale name chip, one filled action **Publish** (menu: publish selected, publish all ready, schedule), secondary: Import, Export, Settings.

**`GradebookGrid`** is the heart of M5 and the riskiest component. Decisions:
- **Layout:** a real `<table role="grid">` with sticky header row and a sticky first column (student name and number), items as columns grouped under their category header (name, weight %, category result column at the end of each group), a **final** column and the **band** (letter or GPA points) at the far end. Rows: roster of the class course (server order, then by name). Density: 36 px rows, tabular numerals, no zebra; the focused cell has a 2 px outline in the primary colour.
- **Virtualisation:** classes are 20 to 60 students and 5 to 30 items, so rows are rendered plainly up to 60 and the columns scroll horizontally. A bigger roster (university sections) pages by 50 students through `ServerData` rather than virtualising, because the row count is the thing the server limits (decision in H3, spike item S2).
- **Keyboard first (WAI-ARIA grid pattern):** arrow keys move the focus between cells; Enter or typing starts editing; Enter commits and moves down; Tab commits and moves right; Esc cancels; Home/End jump to the row ends; Ctrl+Home to the first cell; F2 opens the cell menu (override reason, excuse the item, clear). One roving `tabindex` for the whole grid so Tab leaves the grid after the first stop. The key map lives in a small JS module (`gradebook-grid.js`, about 80 lines: focus management only, never data), the cell state in Blazor.
- **Cell kinds** (each also has a text form for screen readers, not only a colour):
  - *Event score* (from an assignment, quiz, package): read only, shows the points and a small source icon; the cell menu offers **Override**.
  - *Manual score:* editable inline, validated against `MaxPoints` (client says "out of range" before the call, server stays authoritative).
  - *Override:* editable, with a "manually set" marker (a small corner flag plus `aria-label` "overridden: reason"); the reason is required and shown in a tooltip and the cell menu.
  - *Newer score exists* (#611 A3, a regrade after an override): a Warning marker with the text "a newer score exists" and an action to keep or drop the override.
  - *Excused from item:* a dash chip "excused", not counted.
  - *Empty:* an en dash; the **Missing policy** (normalise, zero, hide) decides what the final shows, and the final column carries a `title` such as "calculated from graded items only" so a teacher is never surprised.
- **Weights and categories:** the `GradebookConfigPanel` (a `SectionCard` above or beside the grid, collapsible) edits categories (ar/en name, weight, order) with a **live total** chip: 100 = Success "Weights total 100", otherwise Warning with the missing amount and the sentence that grading still works but the gradebook is marked invalid (#611 3.1). Categories can be reordered with up/down buttons (keyboard) and drag handle (pointer). **Apply weight template** is a menu action that copies values in (a later template edit does not change this gradebook; the confirm dialog says so).
- **Optimistic edit with the server as the judge:** the cell shows the typed value and a saving indicator, the response replaces the row's computed cells (category results, final, band) so the grid never shows a client-calculated number. A failure reverts the cell and shows the error in a live region and in the cell (text, not only red).
- **Item header:** title, max points, source icon, publish state chip (**Hidden**, **Published**, **Scheduled at yyyy-MM-dd HH:mm**), a column menu (edit, move category, publish state, delete manual item). Auto items created from events appear in "Uncategorised" (weight 0) until moved; the first time a teacher opens a gradebook with uncategorised items a one-line `MudAlert` offers "Move them to a category".
- **Publish flow (`PublishItemsDialog`):** a table of items with their state, checkboxes and a mode per selection: publish now, schedule (date-time picker, validated in the future against `serverNowUtc`), or hide. The confirm text says exactly what changes in plain words: "12 students and their guardians will see item X and the totals it affects" and **warns that totals visible to students include only published items** (#611 A4). After publish the dialog shows "N notifications queued" (grouped per student, #611 4.5). Un-publishing is allowed and is audited; the dialog says so. Focus returns to the Publish button.
- **Import and export of a column (`GradeColumnImportDialog`):** Excel download of one item column; upload shows a **preview** (counts of rows, unknown student codes, out-of-range points, rows that would overwrite an existing score) with an **Apply** step. Event-derived scores are never touched and the preview says so. Errors per row come from the server (code list), shown as a table with a downloadable error file.
- **States:** loading skeleton of the grid shape; empty class (no students: `EmptyState` linking to enrollment); no items (`EmptyState` with "Add a grade item" and "Assignments and quizzes appear here when they are scored"); locked gradebook (M7 hook): an Info banner and every cell read-only; offline/failed save: the cell keeps the value, shows "not saved", retry on Enter.
- **360 px:** a grid is not usable as a wide table on a phone. Under 600 px the grid **switches to a student-first list**: a student picker (previous/next buttons and a search) and the selected student's items as labelled rows with the same inline edit. Stated limit: teachers grade on a laptop; the phone view exists to correct one score, not to enter a class.

**Student detail drawer:** clicking a student name opens a right (left in RTL) `MudDrawer` with that student's items, category results, final, band and a "what students see" preview (published items only) toggle, so the teacher can verify what the student and guardian see before publishing.

### 3.2 Student and guardian grades

- **`MyGradesPage`** `/me/grades` (student): a term picker, a stat row (`lms-stats`: overall result, band, number of published items) and one `SectionCard` per course with its categories and **published** items (title, points/max, percentage, date). The course total is the server's published-only total, labelled "based on published grades". Hidden items do not exist in the page: no placeholders, no counts of hidden items (#611 A4, Q18).
- **`ChildGradesPage`** `/me/children/{StudentId}/grades` (guardian): the same component with the child's name in the header; the guardian link is checked by the server.
- A **band legend** (scale name and bands in the user's language) under the totals, because the Saudi school bands have Arabic descriptors (D59); the legend is a `lms-simple-table` in a collapsed `SectionCard`.
- **GPA** (D58): shows the band's points only, labelled "points for this course", never a cumulative GPA.
- New-grade notification text "A new grade was published in {course}" opens this page; no grade value appears in any notification (#611 section 8).

### 3.3 Grading scales and weight templates (admin)

- **`GradingScalesPage`** `/org-units/{Id}/grading-scales` under unit settings: `ServerTable` of scales (name, kind: percentage / letter / GPA, default chip, inherited-from chip, template chip), action **New scale** with a menu **From the Saudi preset** (school, university 5-point, university 4-point) or blank.
- **`GradingScaleDialog`** (full-width on phones): ar/en name, kind, a **band editor** table (min percent, label ar/en, GPA points) with row add/remove and keyboard-friendly inputs. Live validation mirrors the server: bands cover 0 to 100 with no gap and no overlap, shown as a **coverage strip** (a segmented bar with the band labels) plus a text list of problems, so a teacher sees a gap rather than reading an error code. Saving a scale used by a gradebook shows "existing gradebooks keep their scale; new ones use this" per the server rule.
- **Saudi presets (D59):** the preset rows come from the server seed (#611 4.6), never from the client. The page shows each seeded scale with a **"Provisional, needs verification" chip** while the seed note says so (data field from the server, Q-H1), a link to the owner's verification note, and an **Edit** action. When the owner confirms the real bands the chip disappears with the data, with no client release.
- **Default per client:** a radio in the table row ("Make default"), confirm dialog because it changes new gradebooks.
- **`WeightTemplatesPage`**: a list and a small editor (name, categories with weights and a live total chip, same as the grid's config panel, so both reuse `CategoryWeightsEditor`).
- **Term structure (D59):** in the academic-year dialog an option **Two terms / Three terms** (setting `AcademicYears.TermsPerYear`) with a preview of the term names (ar/en) laid out by the server's term template; editable while the year is Planned. The 2026-27 structure is an owner question (Q-H2) and the default must not be hard-coded in the client.

### 3.4 Timetable

- **Admin set-up** `/org-units/{Id}/timetable-setup`: **Periods** (`ServerTable` + dialog: name ar/en, start, end, order; times left to right, validated for overlap and order), **Working days** (seven toggle chips; the first weekday follows the culture's week start but the stored days are absolute), **Conflict mode** (Block or Warn, with the sentence of what each does). This is a settings page with `lms-save-bar`.
- **`TimetableBuilderPage`** `/classes/{Id}/timetable?termId=`: a weekly grid, rows = periods (with times), columns = working days in reading order (so the week runs right to left in Arabic: **the grid mirrors; the time axis labels do not**). Each cell holds a **slot card** (course name, teacher, room text). Empty cells show a quiet "+".
  - **Add or edit** a slot: click or Enter on a cell opens `SlotDialog` (class course picker from the class's courses, teacher picker from eligible teachers, room as plain text, D60). **Move** by drag and drop on pointer devices **and by keyboard**: select a slot, press `M`, arrow keys to a target cell, Enter to drop, Esc to cancel (the same interaction is offered as a "Move to..." menu with day and period selects for assistive technology and for phones). Drag is only an accelerator, never the only way (WCAG 2.5.1, 2.1.1).
  - **Conflicts:** teacher double-booked and class double-booked are shown **as the server's answer**: in Block mode the move is refused with the clashing slot highlighted and a text message; in Warn mode the slot saves and a warning chip appears on both cards, plus a "Conflicts (n)" list in a panel that links to each. The client does not predict conflicts (it would disagree with the server once exceptions exist).
  - **Draft and publish:** a state chip (Draft, Published) in the header; **Publish timetable** is the one filled action; the dialog states that publishing generates attendance sessions for the coming days (`Attendance.GenerateDaysAhead`). **Copy from previous term** (`CopyTermDialog`): pick the source term, run, show the **skipped list** (class course, teacher or period no longer exists) with reasons.
  - **Export** to Excel (download).
- **Exceptions and substitutes (`TimetableExceptionDialog`, FR-TT-05):** from a slot card's menu, a date picker, a kind (Substitute, Cancelled, Moved with a new period), a substitute teacher picker, a note. Exceptions are shown on the **week view of that date** as a badge on the card ("Substitute: name", "Cancelled"), and never change the base slot's card.
- **Views (read-only), one component `TimetableView` with four entry points:** class (`/classes/{Id}/timetable` for students and staff without Manage), teacher (`/me/timetable`: own slots across classes, with the class name on each card), student (own class) and guardian (`/me/children/{StudentId}/timetable`). A **day view** is the phone layout: a day selector (today first, by the user's culture) and a vertical list of the day's periods; the week grid is for 600 px and up. Cards show an exception badge and, for a live-session slot, the Join entry of M4b.
- **Dates and week:** a week navigator (previous/next, "this week") because exceptions are dated; the displayed date range uses ISO dates left to right inside Arabic text.

### 3.5 Attendance taking

Teachers take attendance between classes, often on a phone, so this page is designed **mobile-first**.
- **`MyAttendancePage`/`AttendanceTodayPage`** `/attendance`: **today's sessions** for the teacher (and any session where they are the substitute), as large cards (course, class, period times, a state chip: **Open** or **Taken**, and a progress hint "28 of 30 marked" for a draft). One filled action per card: **Take attendance** / **Review**. A date selector shows other days; past sessions show an **"Edit window closed" lock chip** when `serverNowUtc` is past the edit window (#611 5.5), and the Edit action is then only shown to users with `Attendance.Edit`.
- **`AttendanceTakePage`** `/attendance/sessions/{Id}`: the roster as **one row per student**: avatar or initials, name, number, and a **segmented control of four large buttons** (Present, Absent, Late, Excused) that are 44 px high on touch and show the **text label as well as an icon** (colour is supportive only). Defaults and speed:
  - **Mark all present** is the first, filled action (a client shortcut that sends a default status plus exceptions, #611 5.5); the teacher then taps only the exceptions. A tap on an already selected button does not clear it (clearing is an explicit "Unmarked" state, because Absent by omission would be a wrong record); on a **Taken** session unmarked students cannot exist.
  - **Keyboard and screen reader:** each row is a radio group (`role="radiogroup"` with the student's name as its label); arrow keys move within a row, Tab to the next row, and the shortcut keys `P`, `A`, `L`, `E` set the focused row. A "Next unmarked" button jumps to the first row without a status.
  - **Sticky summary bar** at the bottom (above the safe area on phones): counts per status, and the **Save** button (filled). It never disappears behind the keyboard (`position: sticky` with `env(safe-area-inset-bottom)`).
  - **Note** per student (Late/Absent): a small text button opens an inline field, collapsed by default.
  - **Autosave of a draft in the browser** (per session id, in `localStorage`, a per-viewer convenience only) so a lost connection in a classroom does not lose the marking; on reload a banner offers "Restore your unsaved marks". The page shows a **connection state**; Save failing keeps the marks and offers Retry (the same pattern as the package save state).
  - **Dense roster:** up to 60 rows without virtualisation; a search box filters by name for large lecture sections.
  - A session whose date is in the future cannot be taken (disabled with the reason); a closed class, term or year shows the existing closed messages.
- **Substitute teachers** see only their session and an Info banner "You are the substitute for this session"; the UI shows what the server returns and does not infer it.
- **Edit after the window:** a user with `Attendance.Edit` sees the "Edited by {name}" history (`AttendanceRecordChange` list in a drawer) and a **reason field is required** when editing a Taken session after the window.

### 3.6 Live-session attendance proposal (FR-TT-06, #611 5.6)

- When a live session ends, the server creates an `AttendanceSession(Source = Live)` in the **Open** state with proposed statuses. The teacher finds it in **today's sessions** with a **"Proposed from the live session" chip** and in the `LiveSessionsList` row action "Attendance".
- `AttendanceTakePage` shows a **proposal banner** (Info): "These marks were proposed from the Zoom participants list. Check them, then confirm." Each proposed row shows a small **source icon** (joined for N min, joined late at HH:mm) as a tooltip and as text for assistive technology. The teacher edits any row and presses **Confirm attendance** (the filled action replaces Save for this source). Nothing is final without the confirm (D58: Zoom proposes Present, the teacher confirms).
- **Unmatched participants** (guests, nicknames, shared accounts): a `SectionCard` "Zoom participants not matched" lists them with a student picker per row ("This is...") to map them; the mapping is saved per live session (#611 section 11). A Skip option leaves them unmapped.
- If the provider list is unavailable the session still exists with every student unmarked and a Warning "Zoom did not return the participant list"; the teacher marks manually.

### 3.7 Excuses and absence alerts

- **Guardian and student entry:** in the child's attendance page (`/me/children/{StudentId}/attendance`, and `/me/attendance` for a student) each **Absent or Late** record that is still excusable has an **Explain this absence** action, and the page header has **Submit an excuse**. Who sees the action follows `Excuses.Submitters` (D61): the setting comes with the permission/relationship answer; a student under `GuardiansOnly` sees the absence without the action and the sentence "Your guardian can submit an excuse".
- **`ExcuseSubmitDialog`** (a stepper on 360 px, a dialog on desktop): reason (from the allowed list, Q14), either a date range or a list of the child's excusable sessions (the dialog loads the covered sessions as the date range changes and shows them as a checklist, frozen at submission as the server does), free text, attachment (`AssetUploader`; required or not per the reason and policy, Q14), and the policy hint at the top: "You can submit within N days of the absence" (from the server, with `serverNowUtc`). Out-of-policy answers (`excuse_out_of_policy` and its reason code) map to plain sentences next to the field. An excuse covers only sessions that are Absent or Late **now**; later absences need a new excuse, and the dialog says so.
- **Tracking:** `MyExcusesPage` (student) and the child tab (guardian): a list with a **status chip** (Submitted, Approved, Rejected, Escalated), the covered sessions, the decision comment and the history; **Withdraw** while Submitted. A decision notification opens this page.
- **Counselor queue** `ExcuseReviewPage` `/excuses/review`: a `ServerTable` filtered by status (default Submitted), unit subtree, student, date; columns: student, submitter, reason, covered sessions count, submitted date, attachment indicator, age. Row opens the **`ExcuseReviewDrawer`**: the details, the attachment (opened as a download or an image preview, never inline HTML), the covered sessions with their statuses and **the student's absence history for context**, and the actions **Approve**, **Reject** (comment required, the field is validated before the call), **Escalate** (a picker: Manager or a named staff member, Q15). The reviewer cannot be the submitter: the server refuses, the UI shows the message. An approved excuse tells the reviewer "N records become Excused".
- **Bulk review:** select rows and Approve, with a confirm dialog that lists the count (no bulk reject; a reject needs a comment each).
- **Absence alerts (FR-EXC-04):** `AttendanceAlertsPage` `/attendance/alerts` for counselors: a list of students who crossed the threshold, with the count formula shown (absences + half the lates, in the last N days), the alert date and a link to the student's attendance. The **threshold settings** (5 in 30 days, late = half, D61) live in the attendance settings group with the other defaults.
- **Absence notifications to guardians (FR-EXC-01):** labels only, in both languages: "{student} was marked absent in {course} on {date}" opens the child's attendance page with the excuse action focused.

### 3.8 Reports (supervisor, counselor)

- **`GradeReportsPage`**: per student, class and course, using the engine's output. Staff see all items; the page shows a prominent **"as of" time** (the engine's computed-at time) because the excuse approval changes history (#611 section 11). Filters by term; Excel export through the server.
- **`AttendanceReportsPage`**: counts and percentages per status over a date range, per student and class; a stat row and a table; Excel export.
- Supervisors see their courses, managers the unit scope, enforced by the server.

## 4. Client services (new, M3/M4 pattern)

| Interface | Content |
|---|---|
| `IGradebookApi` | gradebook (get, config, categories, items, entries, override, publish, import preview/apply, export), student and guardian grades, reports |
| `IGradingScalesApi` | scales, bands, default, presets, weight templates |
| `ITimetableApi` | periods, working days, slots, validate, publish, copy-term, exceptions, views, export |
| `IAttendanceApi` | sessions (list, ad hoc, live proposal, confirm, unmatched mapping), records, edit with reason, summaries, own/guardian reads |
| `IExcusesApi` | submit, withdraw, own list, queue, approve, reject, escalate, alerts |
| Fakes | one per interface, registered only with `Lms:UseFakeApi=true`; `FakeGradebookApi` implements a **simplified engine** only to make the demo data consistent, and its tests state that the real engine is the server's; the fake enforces published-only visibility, the edit window with the server clock, Block vs Warn conflicts and the excuse state machine. |

## 5. Contract needs (for the Lead's wave contracts, section 8 of #611)

1. **Computed cells in the grid response**, never client math: per student per item the displayed points, a **state enum** (`Event`, `Manual`, `Override`, `NewerScoreExists`, `Excused`, `Empty`), and per student the category results, final, band, plus the gradebook-level `isValid`, `weightsTotal`, `computedAtUtc` and `serverNowUtc`.
2. **Entry write response returns the recomputed row** (cells, categories, final, band) so the grid replaces its values from the server.
3. **Publish endpoints** accept a list of item ids with a mode and return the affected student count and the number of notifications queued; a **preview of what students will see** (published-only totals) as a staff read for one student.
4. **Provisional flag on seeded scales** (`needsVerification` plus a note key) so the Saudi preset chip is data-driven (D59, Q-H1).
5. **Band legend read** for students and guardians: scale name and bands (labels in ar/en) for the scale of each course.
6. **Term structure:** `TermsPerYear` and the term-name template read for `YearDialog`.
7. **Timetable:** the conflict answer shape in both modes (clashing slot ids with their course and teacher names, so the UI shows the reason without extra calls); slot read includes exception badges for a date range; copy-term result with the skipped list and reasons; `TimetableView` reads accept a week range.
8. **Attendance:** session list for a user and a date (including substitute sessions) with `state`, `source`, `markedCount`, `rosterCount`, `editableUntilUtc`; take call that accepts a default status plus exceptions; a **change history** read; the live proposal fields per record (`source`, `joinedMinutes`, `joinedAtUtc`) and the unmatched participants list with a mapping call.
9. **Excuses:** the policy read for the child (max days, allowed reasons, attachment rule, `Submitters`), the excusable sessions for a date range, status history, an escalate target list, and a counselor queue with the student's recent absence history.
10. **Alerts:** a list read with the formula inputs (absences, lates, window days).
11. **Permissions** as in section 2 and **problem codes** (`timetable_conflict`, `excuse_out_of_policy`, edit-window and locked-gradebook codes, scale-validation codes) each with an `Error_*` resource.
12. **Notification events** with ar and en labels: grades published, absence marked, excuse decided, attendance threshold crossed.

## 6. Task breakdown (one issue and one PR each; sizes S up to 1 day, M about 2 days, L 3 days or more for one FE agent)

| # | Task | Size | PRD | Waits for |
|---|---|---|---|---|
| H1 | Gradebook, scales and weight-template clients and fakes; `Grd_*`/`Scale_*` keys; permission and tab wiring in `ClassCourseTabs` | M | FR-GRD-01, 02 | wave 1 contract |
| H2 | **Grading scales and weight templates admin**: `GradingScalesPage`, `GradingScaleDialog` with the coverage strip, Saudi preset menu and provisional chip, `WeightTemplatesPage`, `CategoryWeightsEditor` | L | FR-GRD-02, 03, D59 | H1, BE 2 |
| H3 | **Gradebook grid (read and render):** `GradebookGrid` (sticky headers, grouped columns, cell kinds, student-first list at 360 px), term picker, empty and locked states, student detail drawer | L | FR-GRD-01, 04 | H1, BE 3-4 |
| H4 | **Grid editing:** keyboard model (`gradebook-grid.js`), inline edit, override with reason, newer-score marker, excuse-from-item, error and retry handling, `FormFlush` for in-flight edits | L | FR-GRD-04 | H3 |
| H5 | **Categories and weights panel, weight template apply, item management** (manual items, move, uncategorised alert) | M | FR-GRD-03 | H3 |
| H6 | **Publish flow:** `PublishItemsDialog` (now, schedule, hide), state chips, what-students-see preview, notification labels | M | FR-GRD-05 | H3, BE 5 |
| H7 | **Column import and export:** `GradeColumnImportDialog` with preview and apply, Excel download | M | FR-GRD-04 | H4, BE 4 |
| H8 | **Student and guardian grades:** `MyGradesPage`, `ChildGradesPage`, band legend, published-only copy, notification text | M | FR-GRD-04, 05 | H1, BE 4 |
| H9 | **Timetable set-up and builder:** periods, working days, conflict mode, `TimetableBuilderPage`, `SlotDialog`, keyboard move and drag, conflict display, draft and publish, copy-term | L | FR-TT-01..03 | wave 2, BE 6-7 |
| H10 | **Timetable views, exceptions and export:** `TimetableView` (week and day layouts, four entry points), `TimetableExceptionDialog`, exception badges, Excel download | M | FR-TT-04, 05 | H9, BE 8 |
| H11 | **Attendance clients, fakes, today's sessions page, edit-window and history drawer** | M | FR-ATT-01 | wave 3, BE 9 |
| H12 | **Attendance taking page** (segmented controls, mark-all-present, keyboard shortcuts, sticky summary, local draft restore, note) | L | FR-ATT-01 | H11 |
| H13 | **Live-session attendance proposal:** banner, confirm flow, source tooltips, unmatched participants mapping, row action in the live sessions list | M | FR-TT-06, D58 | H12, BE 10 |
| H14 | **Own and guardian attendance and excuse submission:** `ExcuseSubmitDialog`, tracking list, withdraw, policy hints, submitter setting handling | L | FR-EXC-02, D61 | H11, BE 11 |
| H15 | **Counselor review queue:** `ExcuseReviewPage` and drawer, approve, reject (comment), escalate, bulk approve, absence-history context | L | FR-EXC-03 | H14, BE 11 |
| H16 | **Absence alerts and notification labels:** `AttendanceAlertsPage`, threshold settings group, absence/alert/excuse label parity | S | FR-EXC-01, 04, D61 | H15, BE 12 |
| H17 | **Settings groups and year setup:** grades, timetable, attendance and excuse settings (with `lms-save-bar`), `YearDialog` two/three terms option (D59) | M | D58, D59 | wave contracts |
| H18 | **Reports:** `GradeReportsPage`, `AttendanceReportsPage`, Excel downloads, "as of" labelling | M | FR-GRD-04 | H8, H12 |

**Critical path (FE):** H1 -> H3 -> H4 -> H6 (about 9 FE days) in parallel with H9 -> H10 and H11 -> H12 -> H14 -> H15 (about 13 FE days); H2, H5, H7, H8 and H17 fill gaps. **Total about 38 FE days for one agent, so two FE agents split it: A = gradebook and scales (H1 to H8), B = timetable, attendance, excuses (H9 to H16, H18), H17 shared.** A second QA task should add E2E for the three headline journeys in section 9.

## 7. Spike items (resolve before or in the task, not after)

- **S1 Grid interaction (H3/H4):** a one-day prototype of the keyboard-first grid with 60 rows by 25 columns under bUnit and Playwright, to confirm Blazor re-render cost per cell edit and the roving-tabindex approach with `role="grid"`. Fallback: render rows with `@key` and `ShouldRender` guards per row component.
- **S2 Large rosters:** whether the server pages students for university sections or the grid receives all of them (contract item 1); the page size decides virtualisation.
- **S3 Drag and drop in RTL (H9):** pointer drag with the logical column order, and the keyboard move, verified in both directions at 360 and 1440 px.
- **S4 Local draft of attendance (H12):** `localStorage` is a per-viewer convenience only; confirm the server remains the single source and a stale draft never overwrites newer server data (compare the session's `modifiedUtc`).

## 8. Accessibility, RTL and 360 px notes

- **Never colour alone:** every status has a text label and an icon; the four attendance buttons show their words; grade cell states have a text form for screen readers; conflict and weight problems are sentences, not only a red outline.
- **Grid:** `role="grid"`, row and column headers with `scope`, `aria-colindex`/`aria-rowindex` for the sticky layout, a single roving tabindex, `aria-readonly` on event cells, `aria-invalid` plus `aria-describedby` on a rejected value. The save and error messages go to one polite live region (not one per cell, so a fast typist is not flooded).
- **Targets and touch:** 44 px minimum for the attendance buttons and the timetable cells' add control; 24 px minimum elsewhere (WCAG 2.5.8); no hover-only actions: every row or card action is a visible icon button with an `aria-label`.
- **Drag and drop has a keyboard and menu equivalent** (section 3.4).
- **Focus return** after every dialog and drawer; `CloseOnEscapeKey` applies; the review drawer traps focus; the grid cell returns to the edited cell after an error.
- **Reduced motion:** the live "saving" indicators and the proposal highlight do not animate under `prefers-reduced-motion`.
- **RTL:** grids and weekly timetable columns mirror; time labels, ISO dates, percentages (`85.5%`), points (`18/20`) and student numbers stay left to right inside Arabic text (`dir="ltr"` spans or `lms-ltr`); direction icons (previous/next week, move) are mirrored; Arabic band descriptors (ممتاز, جيد جدا, ...) are shown from data, never typed into the client; Arabic-Indic digits follow the culture setting already used elsewhere (decide once in H1 with the Lead, Q-H3).
- **360 px:** the gradebook is a student-first list (3.1), the timetable is a day list (3.4), attendance taking is the primary phone page (3.5), the scale and template editors use full-width dialogs, tables use `lms-stack-table` labelled cards. Every page gets a 360 px screenshot and a bUnit test that the stacked layout classes are present.
- **Dark mode:** bands and statuses use the palette roles that already reach AA in dark mode; the coverage strip and the timetable cards use surface tokens, not custom colours.

## 9. Tests

- bUnit for every page and dialog (loading, empty, error-with-retry, success, permission-less entry hidden, RTL render, stacked layout class). Fakes enforce the server rules they depend on, and each has a test of that rule.
- `GradebookGrid`: keyboard navigation test (arrows, Enter, Tab, Esc, Home/End), "client never computes" test (the final comes from the response), published-only student view test (no hidden item text anywhere in the markup), locked state read-only test.
- Attendance take: mark-all-present then one exception sends a default plus exception; unmarked rows cannot be saved on a Taken session; edit-window lock uses `serverNowUtc`; local draft restore never overwrites a newer server state.
- Timetable: Block vs Warn display from the same response shape; keyboard move equals menu move; exception badge never alters the base card.
- Excuses: submit validation and policy messages; reject needs a comment; reviewer-is-submitter message; bulk approve confirm lists the count.
- Notification label parity for every new event; resource parity (ar, en) per page.
- **E2E (QA, three journeys):** (1) teacher sets weights, enters a manual score, publishes, the student sees it and the guardian sees it, an unpublished item is invisible to both; (2) admin builds and publishes a timetable with a conflict in Block mode, attendance sessions appear for the teacher, the teacher takes attendance on a 360 px viewport; (3) guardian submits an excuse with an attachment, the counselor approves, the record shows Excused and the threshold alert does not fire for it. Each in ar and en.

## 10. Owner and Lead questions

Owner questions (via the Lead):
1. **Q-H1 Saudi presets (D59, Q21 to Q23):** until the official school bands, the 2026-27 term structure and the university table are confirmed, the presets show a **Provisional** chip. Should students and guardians see that chip, or only admins? (Proposal: admins only; students see the band legend as configured.)
2. **Q-H2 Term structure default:** do new clients start with two or three terms? The client reads it from the setting and has no default of its own.
3. **Q-H3 Digits:** Arabic-Indic or Western digits in grades, dates and attendance counts in the Arabic UI? (Proposal: Western digits everywhere, the product's current behaviour, with the convention recorded in DESIGN.md.)
4. **Q-H4 Phone gradebook:** is "correct one score on a phone" enough, or must teachers enter a whole class on a phone? (Proposal: enough; a full grid on a phone is not planned.)
5. **Q-H5 Attendance on a roster of 60+:** do lecture sections of a university client matter in v1? It decides search and paging on the take page.
6. **Q-H6 Excuse attachment viewing:** may the counselor see images inline in the review drawer, or download only? (Proposal: images and PDFs inline in a sandboxed viewer, others download.)
7. **Q-H7 Absence history in the review drawer:** the counselor sees the student's recent absences for context; confirm this is acceptable for guardians' privacy expectations (it is staff-only data).

Lead questions:
- **Q-L1** Wave order for FE: gradebook first (wave 1), timetable second, attendance and excuses third; or start the attendance take page earlier because it is the most-used page? It needs wave 2's sessions, so the order above is forced unless ad hoc sessions ship in wave 1.
- **Q-L2** Is `Excuses.Submit` for a student always resolved by the server from the `Excuses.Submitters` setting, so the client never reads the setting? (Proposal: yes, return a `canSubmit` flag on the child's attendance read.)
- **Q-L3** Can the grid response mark `isValid` and `weightsTotal`, or must the client sum the category weights itself for the live chip? (The chip itself sums what the user types; the saved state comes from the server.)
- **Q-L4** The two-FE split in section 6 needs `ClassCourseTabs` to be touched by H1 (A) and H11 (B) in quick succession: agree who adds both tabs in the contract-wave PR.

## 11. Risks

| Risk | Mitigation |
|---|---|
| **The grid is the heaviest component**; slow re-render on edit, broken keyboard model, screen reader confusion | Spike S1 first; per-row components with `ShouldRender`; a single live region; the ARIA grid pattern tested with bUnit and one manual screen-reader pass noted in the PR. |
| **Client-side calculation drifting from the engine** | The client never computes; tests assert the displayed final equals the response; the fake's simplified engine is documented as a demo aid. |
| **A hidden item leaking** in the student or guardian page (titles, counts, totals) | Student/guardian components receive only published data; a bUnit test renders a fake response that deliberately includes a hidden item to prove nothing is shown; QA's leak test is part of the M5 checklist (#611 Task 14). |
| **Attendance taking too slow** on phones | Mark-all-present first, large targets, sticky save, local draft; QA times the 30-student flow at 360 px (target: under 30 seconds for 2 exceptions). |
| **Drag and drop inaccessible or wrong in RTL** | Keyboard and menu move are required features, not extras; S3 verifies RTL. |
| **Provisional Saudi bands shown as official** | The chip is data-driven, admins can edit, and no preset number appears in client code or resources. |
| **Notification volume and wording leaking grades** | Labels carry course and event only; a test greps the resource files for `{points}`-style placeholders in grade notifications. |
| **Scope: about 38 FE days** | Two FE agents, three waves, H1 to H8 (gradebook) can start as soon as wave 1 lands; H17 and H18 are the first items to defer. |
| **`ClassCourseTabs` and `YearDialog` merge conflicts** between the two FE tracks | Q-L4: one PR adds the tabs and the term option placeholders; each track then only edits its own panels. |
