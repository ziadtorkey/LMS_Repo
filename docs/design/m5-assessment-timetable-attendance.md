# M5 design spec: gradebook, scales, timetable, attendance and excuses

**Plan:** `docs/superpowers/plans/2026-11-18-m5-assessment-timetable-attendance.md` (#621), Task 21. This spec is the gate for the FE pages and dialogs of Tasks 23–39.
**PRD:** FR-GRD-01..05, FR-TT-01..06, FR-ATT-01, FR-EXC-01..04, X-01, X-10; owner decisions D58–D65; Lead decisions L7, L10–L25.
**Mockups:** `docs/design/mockups/m5.html`: static HTML in the Stitch tokens (`LmsTheme.cs`, `lms-stitch.css`). Open it with `?screen=gradebook|publish|scales|scale-edit|weights|my-grades|timetable|attendance-today|attendance-take|attendance-live|excuse-submit|excuse-review|alerts&lang=ar|en&theme=light|dark`.
**Screenshots:** `docs/screenshots/ux-m5/<screen>-<lang>-<theme>-<width>.png`. Each of the 13 screens is shot at 1440 px (en light, ar dark) and 360 px (ar light, en dark), 52 in all, and none scrolls sideways. Below 600 px the mockup renders the phone layout (the gradebook list, the timetable day view) instead of hiding the desktop one.

Everything in `docs/DESIGN.md` applies. This file only adds what M5 needs. Where FE1's proposal (#616) and the plan's Lead decisions differ, **the plan wins**: the keyboard model is L24 (not #616's F2 menu) and the attendance shortcuts are digits (L25, not P/A/L/E). The new classes in §9 go into `lms-stitch.css` in one `lane:ux` PR before Tasks 23–38 start.

---

## 1. Shared rules for M5

- **Western digits everywhere (L20).** Grades, points, percentages, dates, times and counts use 0–9 in both languages, and they're culture-invariant (#509). Numbers that sit next to Arabic text go in `<span dir="ltr">` or the `num` class (`direction: ltr; unicode-bidi: isolate; font-variant-numeric: tabular-nums`).
- **The client never computes a grade (L18).** Category results, the final grade, the band and `isValid` all come from the server. The only client-side arithmetic is the preview total in the weights chip while someone is typing.
- **Colour is never the only signal.** Every cell state, attendance status, chip and coverage problem also has a text form, an icon or a pattern, and a text alternative for screen readers.
- **Server time** (`serverNowUtc`) decides edit windows, scheduled publishing and excuse deadlines, as in M4b. The client time zone is `Org.TimeZone` (default `Asia/Riyadh`, D64), and it's shown once wherever a time is typed.
- **Optimistic concurrency (L12).** A `concurrency_conflict` reloads the row or session, shows "Someone else changed this. We loaded the latest values." in a polite live region, and keeps the user's typed value in the field so they can apply it again.
- **Icons**, Rounded only:
  - **Grading:** Assignment (score from an event), Grading (scales), Balance (weights), Publish, Pending (provisional).
  - **Timetable:** CalendarViewWeek, SwapHoriz (substitute), EventBusy (cancelled), Warning (conflict), OpenWith (move).
  - **Attendance:** HowToReg, CheckCircle / Cancel / Schedule / Verified (the four statuses), Videocam (proposed from a live session), Lock (window closed).
  - **Excuses:** AssignmentLate (excuse), TrendingUp (escalate), NotificationImportant (alert).

## 2. Gradebook grid (Tasks 24–28)

Mockup: `screen=gradebook` (1440: the grid; 360: the student-first list) and `screen=publish`.

### 2.1 Layout
- **Route:** `/class-courses/{id}/gradebook?termId=`, the Gradebook tab of `ClassCourseTabs` (L22).
- **Page header:** `PageHeader` with "Term · scale name" as the subtitle and actions Import, Export, Settings (an icon button) and **Publish** (filled). Below 600 px, Import and Export move into the Settings menu.
- **Toolbar** (inside the card, above the grid):
  - a term segmented control;
  - the **weights chip** (`lms-weights-chip`): "Weights total 100" (success, CheckCircle), or Warning with the missing amount ("Weights total 90: 10 missing");
  - on the trailing side, the keyboard hint, which is also the grid's `aria-describedby`.
- **The grid** is a real `<table role="grid">` (`lms-gb`) inside a scroll container (`lms-gb-wrap`) that scrolls in both directions:
  - Header row 1 holds the category groups, with `colgroup` and `colspan` and the weight as a number. Header row 2 holds the items: title, "out of N" and the publish state (Published / Hidden / Scheduled MM-dd). An Assignment icon marks items that come from events.
  - The student column is sticky at the inline start. Both header rows are sticky at the top, and their corner cell sits above both.
  - Each category ends with a tinted **Category** result column. **Final** (with a 2 px start border) and **Band** (a chip with the band label in the user's language) come last.
  - Rows are 36 px high with tabular numerals and no zebra stripes. The student cell has an avatar and the name.
- **`scroll-padding`** on the container is the height of the two header rows plus the width of the student column, so the focused cell is never under a sticky edge (WCAG 2.4.11, L24). This is tested in LTR and RTL.

### 2.2 Cell states (every state has text, not only colour)

| State | Look | Text for assistive tech |
|---|---|---|
| Score from an event (assignment, quiz, package) | Points + Assignment icon; read-only | "9 of 10, from Forces worksheet assignment" |
| Manual score | Points | "18 of 20" |
| Overridden | Points + a warning-coloured corner triangle at the inline end | "17 of 20, overridden: {reason}" |
| A newer score exists | Points + a "newer score" warning mini-chip | "5 of 10, overridden; a newer score exists" |
| Excused from the item | An "excused" neutral mini-chip | "Excused" |
| No score | An en dash; the final uses graded items only (`Grades.MissingPolicy`) | "No score" |
| Editing | A recessed input inside the cell with the focus glow | the input's label is "{student}, {item}" |
| Saving | Muted value + spinner; a visually hidden "saving" | announced only if it takes over 1 s |
| Not saved | Error-colour value + Error icon + "not saved" mini-chip; Enter retries | "Not saved: {reason}. Press Enter to try again." |

A legend under the grid (`lms-gb-legend`) repeats these forms, so nothing relies on the colour key.

### 2.3 Keyboard (L24, verbatim)
- **Moving:** the arrow keys move between cells, Home and End go to the ends of the row, and Ctrl+Home goes to the first cell. There's one roving `tabindex`, so Tab leaves the grid in navigation mode.
- **Editing:** **Enter or F2** toggles editing. While editing, Tab commits and moves right, Enter commits and moves down, and Esc cancels. Entering and leaving edit mode are announced ("Editing Lab report for Omar Khaled", "Saved").
- **Cell menu:** **Shift+F10** or the context-menu key opens it. It offers Override score…, Excuse from this item and Clear score, and, for the newer-score state, **Keep my override / Use the newer score**. Clear sits last, in the error colour. Focus returns to the cell when the menu closes.
- **Focus outline:** a 2 px primary outline at −2 px offset plus a 4 px surface inset, at least 3:1 against the selected, hover and override backgrounds in both themes. In forced-colors mode it's `outline: 2px solid Highlight`.
- **Override:** the dialog requires a reason, and the server row replaces the optimistic value.

### 2.4 The phone gradebook (L19)
- **Below 600 px** the page renders **one** layout, the student-first list; the desktop grid isn't present and hidden with CSS. Edits typed before a breakpoint change survive it.
- **Top of the card:** previous and next student buttons (the arrows mirror in RTL), the student's name with "3 of 26", and a search field.
- **Items** are rows grouped by category (`lms-gb-item`, at least 52 px high). Scores from events are read-only text with their icon; manual scores are a recessed 84 px input.
- **Footer row:** Final and Band.
- **Note:** an info alert says the phone is for correcting one score, and a whole class is entered on a larger screen.

### 2.5 Publish dialog
- **Items table:** a checkbox per item, its current state chip, and the change: Publish now, Schedule (a date and time, in the future by server time) or Keep hidden.
- **Confirmation sentence:** "26 students and their guardians will see 'Mid-term exam' and the totals it affects. Totals that students see include published items only."
- **Provisional scale (L7, L23):** when the gradebook's scale is provisional, an **error alert at the top** says "Publishing is blocked. This gradebook uses a provisional scale. Ask a unit admin to confirm the scale before you publish." It has an **Open grading scales** button, and Publish is disabled. Teachers never see the word "provisional" anywhere else.
- **After publishing:** "N notifications queued". Focus returns to the Publish button.

### 2.6 States
- **Loading:** a skeleton in the shape of the grid.
- **No students:** an `EmptyState` linking to enrollment.
- **No items:** "Add a grade item. Assignments and quizzes appear here when they're scored."
- **Locked (M7 seam):** an Info banner, and every cell is read-only.

## 3. Scales and weight templates (Task 23, D59, D62, D63)

Mockups: `screen=scales`, `screen=scale-edit` and `screen=weights`.

1. **`GradingScalesPage`** (`/org-units/{id}/grading-scales`, in the unit settings tabs and the Security and settings group):
   - **Actions:** **From a Saudi preset** (outlined) and **New scale** (filled). The preset menu lists the server's templates: school bands, university 5-point (the default suggestion for a university client, D63) and university 4-point.
   - **Table columns:** Name (with "From {unit}" for inherited scales), Kind (Percentage / Letter / GPA 5.00 / GPA 4.00), number of bands, used by ("12 gradebooks"), and status chips:
     - **Default** (success, with a dot);
     - **Template** (neutral, ContentCopy icon);
     - **Provisional, needs verification** (warning, Pending icon), with an inline **Confirm** button on copies;
     - **Update available** (info) when the seed version is newer than the copy.
   - **Provisional chip:** shown on admin pages only (L23).
   - **Band numbers and Arabic descriptors** come from the server seed and are **never written in client code or resources** (Task 23 interfaces). The mockup's numbers are #611 §4.6's provisional seed, for illustration only.
2. **`GradingScaleDialog`** (full screen at 360 px):
   - **Fields:** Arabic and English names, then the **band editor**: a table of *From %*, *Label (Arabic)*, *Label (English)* and, for GPA kinds, *Points*. It has add and remove buttons, and labelled inputs in each row.
   - **Coverage strip** (`lms-coverage`): a 0–100 bar with one segment per band, each labelled when it's wide enough. A **gap** is a red-hatched segment with "!", and an overlap is a hatched overlap. The axis below it is proportional (0, 60, 65, 70… 100).
     - **The strip is LTR in both languages**, like every number scale in the product (L20).
     - It has `role="img"` with the problem as its label, and the same problems repeat in an error alert as text: "Gap: 60 to under 65 % has no band. Bands must cover 0 to 100 % with no gap and no overlap."
     - Save is disabled while the problem list isn't empty. The server still validates.
   - **Header chip and info alert** on a provisional copy: "Seeded from the Saudi preset (needs verification). Saving an edit, or Confirm, removes the provisional state."
   - **Warning when the scale is in use:** "12 gradebooks use this scale. They keep their copy; new gradebooks use the changes."
   - **Buttons:** Cancel, **Confirm scale** (outlined, only on provisional copies with no problems) and Save.
3. **Make default:** a row action, behind a confirmation that new gradebooks will use it.
4. **`WeightTemplatesPage`:**
   - **Layout:** a list plus an editor card. The editor holds rows of category name and weight %, with move up, move down and remove buttons, which give keyboard-friendly reordering; a drag handle is an extra, never the only way.
   - **Weights chip:** the same chip as the gradebook toolbar, plus a warning sentence that grading still works when the total isn't 100.
   - **Copying into a gradebook:** the template is applied from the gradebook's Settings ("Apply weight template"). The confirmation says later template edits don't change this gradebook.
5. **Terms per year (D62, L8):** in `YearDialog`, a **Two terms / Three terms** segmented control with a preview of the term names from the server's template.
   - It's editable while the year is Planned. Otherwise it's disabled, with "This year has terms in use; edit the terms instead."
   - The default (two) comes from the server setting and isn't hard-coded in the client.
6. **Student and guardian view** (`MyGradesPage`, `ChildGradesPage`; mockup `screen=my-grades`):
   - **Course cards:** one per course. The card header has the course total, labelled **"based on published grades"**, its band chip, and "{points} points for this course" (D58: per course, never a cumulative GPA, L18). The body lists published items only: no placeholders and no counts of hidden ones.
   - **Band legend:** a collapsible `details` card with the scale name and the bands in the user's language.
   - **No provisional chip** here (L23).

## 4. Timetable (Tasks 31–32)

Mockup: `screen=timetable` (1440: the week builder in the middle of a keyboard move; 360: the day view).

1. **The week grid** (`lms-tt`, `role="grid"`):
   - **Rows** are periods, with the period name and times. **Columns** are working days in reading order, so Sunday is at the inline start (the right in Arabic) and the grid mirrors. **The times don't mirror**: `08:20–09:05` stays LTR.
   - **Today's column** has a "Today" chip.
   - The working days come from the unit setting (Sunday–Thursday in the mockup); nothing assumes a Monday start.
2. **Slot card** (`lms-slot`):
   - **Content:** the course (bold, ellipsis), the teacher and the room as plain text (D60), with a 3 px primary bar at the inline start.
   - **Empty cells:** a quiet dashed "+" with the accessible name "Add a lesson".
   - **Badges** from exceptions, shown only on the week of that date: **Substitute: {name}** (info, SwapHoriz) and **Cancelled** (error, EventBusy).
   - **Conflicts:** a warning chip with the conflict in words ("Teacher double-booked") and a warning-coloured bar. Conflicts come from the server's answer only.
3. **Moving a lesson** (WCAG 2.1.1, 2.5.1):
   - **Keyboard:** select a card and press **M**. An info status line says "Moving 'Mathematics': use the arrow keys, Enter to drop, Esc to cancel." The target cell shows "Drop here" in a dashed primary outline.
   - **Pointer:** drag and drop does the same.
   - **Menu and phones:** a "Move to…" option (day and period selects) is the third way and the one used on phones.
   - **Block mode:** a refused move restores the card and shows the server's message, with the clashing card highlighted.
   - **Warn mode:** the move saves, and a **Conflicts (n)** alert below the grid lists each conflict as a sentence that links to its card.
4. **Header:**
   - a **Draft / Published** chip;
   - a week navigator: previous and next buttons, the ISO date range in LTR, and **This week**;
   - Copy from the previous term (outlined), which shows the skipped list with reasons when it finishes;
   - **Publish timetable** (filled). Its dialog says that publishing generates attendance sessions for the coming days.
5. **Phone (below 900 px):**
   - **The day view:** a day segmented control (today selected) and a vertical list of the periods with their times and slot cards.
   - **No dragging on phones:** "Move to…" is the way to move a lesson.
6. **Read-only views** (class, teacher `/me/timetable`, student, guardian): the same component without edit affordances.
   - **Teacher view:** cards show the class name instead of the teacher.
   - **Live-session slots** show M4b's Join entry.
7. **Set-up page:**
   - **Periods:** a table and a dialog. Times are entered LTR, and overlaps and ordering are shown at the field.
   - **Working days:** seven toggle chips.
   - **Conflict mode:** Block or Warn, each with a one-line sentence on what it does.
   - Unsaved changes use `lms-save-bar`.

## 5. Attendance (Tasks 33–35), mobile first

Mockups: `screen=attendance-today`, `screen=attendance-take` and `screen=attendance-live`. The take screens are shot at viewport height, so the sticky bar sits where the user sees it.

1. **Today's sessions** (`/attendance`):
   - **Layout:** a date field, then a grid of session cards (`lms-session`, one column at 360 px).
   - **Card content:**
     - **Header:** the course, class and period, and the times and room in LTR.
     - **State chip:** **Open** (info) or **Taken** (success).
     - **Hints and badges:** "28 of 30 marked" for a draft; **You are the substitute** (info); **Proposed from the live session** (Videocam); **Edit window closed** (Lock).
     - **One action:** **Take attendance** (filled, 44 px), or **Review**.
2. **Taking attendance** (`/attendance/sessions/{id}`):
   - **Header:** the course, class and period title, and **Mark all present**, the first action (filled, 44 px). A search field and **Next unmarked** come before the roster.
   - **Rows** (`lms-att-row`): the avatar, name and a note line (for example "Arrived at 09:12" or the live-session source), then a **radio group of four labelled buttons** (`lms-att-opts`), each at least 44 px high: Present · Absent · Late · Excused.
     - Each has an icon **and** its word, and the selected one adds a check mark and a 2 px border in the status colour. Colour supports the meaning but never carries it alone.
     - The digit hints `1`–`4` are drawn on the buttons on desktop (L25: matched by `event.code`, active only while a row has focus). Below 600 px the hints are hidden and the buttons fill the row width.
     - Within a row the arrow keys move between the buttons, and Tab moves to the next row.
     - An unmarked row shows "Unmarked" in the warning colour with an empty-circle icon, never a blank.
   - **Sticky summary bar** (`lms-att-bar`):
     - **Content:** counts as chips (Present, Absent, Late, Excused, Unmarked) in a polite `role="status"`, the connection and draft state ("Online · draft saved on this device"), and **Save attendance** (filled).
     - **Position:** it sits above `env(safe-area-inset-bottom)` and never under the on-screen keyboard.
     - **On phones:** the counts form one scrollable line, and Save takes the full width.
   - **Local draft (L25):** a warning banner says "You have unsaved marks from 08:58 on this device", with **Restore** and **Discard**. The draft never overwrites a newer server state, and it's cleared on save and sign-out.
   - **After the edit window:** users with `Attendance.Edit` must enter a reason, and the history drawer shows "Edited by {name}".
3. **Live-session proposal (L11):**
   - **Banner:** "These marks were proposed from the Zoom participants list. Check them, then confirm."
   - **Rows:** each proposed row shows its source ("Joined for 44 min", "Joined late at 09:14"). The filled action becomes **Confirm attendance**.
   - **Unmatched participants:** a section lists **Zoom participants not matched** ("iPad Air" (guest)), each with a "This is…" student picker and **Skip**. Only `Matched` participants are proposed automatically, so there's no name guessing.

## 6. Excuses and absence alerts (Tasks 36–38)

Mockups: `screen=excuse-submit`, `screen=excuse-review` and `screen=alerts`.

1. **Submitting** (guardian, or student when allowed; `canSubmitExcuse` comes from the server, L21):
   - **Format:** a dialog on desktop and a full-screen **stepper** at 360 px (`lms-steps`, reused from M4b): Sessions · Reason · Details.
   - **Policy hint at the top:** "You can submit within 5 school days of the absence. Your school decides which reasons need a document." The numbers come from the server.
   - **Sessions:** a checklist of the excusable sessions, each with date, course, period and status in words. Under it: "An excuse covers only sessions that are absent or late now. Later absences need a new excuse."
   - **Reason:** a segmented control with the allowed reasons (Illness, Family, Official, Other; L13).
   - **Details:** optional free text.
   - **Document:** "JPEG, PNG, WebP or PDF, up to 5 MB." When the reason requires a document, the hint says so (L10).
   - **Errors:** out-of-policy answers appear as plain sentences at the field.
   - **Students under `GuardiansOnly`** see the absence without the action, with the line "Your guardian can submit an excuse."
2. **Tracking:** the list shows each excuse with a status chip: Submitted (info), Approved (success), Rejected (error) or Escalated (warning), each with a dot and its word. **Withdraw** is available while the status is Submitted.
3. **Review** (`/excuses/review`, counselor and manager):
   - **Queue:** a segmented status control (Submitted, Escalated, Approved, Rejected), then a table of student, submitter, reason (with a paperclip icon when there's a document), sessions, submitted date and how long it has waited.
   - **Detail panel:** a 420 px panel beside the queue at 1100 px and wider, and a full-screen drawer below that. Its contents:
     - **Covered sessions**, each with a status icon and its word.
     - **The document:** an image thumbnail when it's a raster image. PDFs download only, never inline and never as HTML (L10). A 5-minute signed link is fetched on click.
     - **The last 30 days**, as a strip of small LTR squares (`lms-history`): **P/A/L/E letters in each square** with a key below, so it doesn't depend on colour.
     - **Comment:** required to reject, and validated before the call.
     - **Approval preview:** "Approving makes 3 records Excused."
     - **Actions:** Escalate (outlined, with a picker of reviewers in scope, Manager first; L14), Reject (outlined, error colour) and Approve (filled).
     - **Reviewer is the submitter:** the server refuses (409), and the message is shown in the panel.
   - **Bulk approve:** select rows, then confirm with the count. There's no bulk reject.
4. **Absence alerts** (`/attendance/alerts`):
   - **Stat tiles:** "Students over the threshold" and "New this week".
   - **Table:** the student, the formula in words ("4 absences + 3 lates × ½ = 5.5", D61), the alert date and **Open attendance**.
   - **Threshold settings** live in the attendance settings group.

## 7. Notifications (L26)

The texts carry labels only, never a grade value or a hidden item's title, for example "A new grade was published in Physics 1". Each one deep-links: grades go to My grades, absences to the child's attendance with the excuse action focused, and decisions to the excuse.

## 8. Accessibility checklist for M5 (for the Task 39 sign-off)

- [ ] The grid follows L24: Enter or F2 to edit, Shift+F10 for the menu, Tab only while editing, the focused cell is never under a sticky header or column in LTR or RTL, the focus outline is at least 3:1 and visible in forced colors, and edit mode is announced on entering and leaving.
- [ ] Every cell state and attendance status has a text form (§2.2, §5.2), and the legend is present.
- [ ] Attendance buttons are at least 44 px, in one `radiogroup` per row named by the student. The digit shortcuts work only while a row has focus, and the summary is `role="status"`.
- [ ] Timetable moves work by keyboard (M, arrows, Enter, Esc), by the "Move to…" menu, and by drag. Drag is never the only way.
- [ ] The coverage strip's problems also appear as text, and Save is disabled with the reason visible.
- [ ] Every dialog and drawer returns focus to the control that opened it. The phone gradebook and the stepper have one heading each.
- [ ] Reduced motion: no animated transitions in the grid or the timetable move. The saving spinner is static under `prefers-reduced-motion`.
- [ ] Forced colors: chips and status buttons keep a border (`border: 1px solid CanvasText`), and the selected attendance button uses `Highlight`.
- [ ] Nothing scrolls sideways at 360 px. All 52 shots were checked with `scrollWidth`.

## 9. New classes (`lms-stitch.css`)

The reference CSS is the "M5 additions" block in `m5.html`. It uses logical properties and palette variables only. (The coverage axis numbers are positioned with `inset-inline-start` inside an LTR box.)

| Class | Use |
|---|---|
| `lms-gb-wrap`, `lms-gb`, `lms-gb-student`, `lms-gb-cat`, `lms-gb-catres`, `lms-gb-final`, `lms-gb-hdr`, `lms-gb-legend` | The grid, its sticky edges and its legend |
| `.cell` modifiers `override`, `focus-cell`, `editing`, `saving`, `error`; `mini`, `mini-warn`, `mini-muted` | The cell states (§2.2) |
| `lms-weights-chip` | The weights total chip (gradebook and templates) |
| `lms-gb-picker`, `lms-gb-item` | The phone student-first list |
| `lms-coverage`, `lms-coverage-axis` | The band coverage strip |
| `lms-tt`, `lms-tt-h`, `lms-tt-p`, `lms-slot` (+ `empty`, `conflict`, `selected`, `drop`) | The timetable grid and slot cards |
| `lms-session`, `lms-session-top` | Today's session cards |
| `lms-att-row`, `lms-att-who`, `lms-att-opts`, `lms-att-opt` (+ `p`, `a`, `l`, `e`), `lms-att-bar` | Taking attendance |
| `lms-check`, `lms-review`, `lms-history` (+ `h-p`, `h-a`, `h-l`, `h-e`) | Excuse checklists, the review layout and the history strip |
| `lms-steps` | Already in `lms-activities.css` (M4b); reuse it, don't copy it |

## 10. Open points for the Team Lead

- **Q-UX1 Coverage strip direction.** It's LTR in Arabic (the percentage axis follows L20). If the owner prefers 100 → 0 from the right in Arabic, it's one CSS line.
- **Q-UX2 Saudi seed numbers.** They're still provisional (#611 Q21–Q24). The UI is built so they change as data, with no client release.
- **Q-UX3 Attendance on large lecture sections.** It's 200 rows with search and no virtualisation (L17). If universities need more, a "Show absent only" filter is the next step, not a different layout.
