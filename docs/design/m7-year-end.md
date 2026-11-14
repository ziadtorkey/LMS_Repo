# M7 design spec (draft): year end, lifecycle and external sign-in

**Status:** draft, revision 1. It follows FE2's M7 frontend proposal (#808, tasks H1–H9) and the owner decisions **D78–D87**. API and permission names are #808's proposals until the Lead's M7 contract PRs fix them; §14 lists what this spec asks #808 to change.
**PRD:** FR-YEAR-04, FR-YEAR-05, FR-GRD-06, FR-STU-07, FR-STU-08, FR-SEC-07, FR-SEC-08, FR-ORG-07/08/09; X-01 (bilingual), X-09 (large data), X-10 (accessibility, 360 px).
**Mockups:** `docs/design/mockups/m7.html`. It's static HTML in the Stitch tokens from `LmsTheme.cs` and `lms-stitch.css`. Open it with `?screen=home|years|close|gradebook|grade-request|grade-changes|promo-preview|promo-run|promo-result|withdraw|archive|readmit|new-year|idp-saml|idp-ldap|login-ldap&lang=ar|en&theme=light|dark` (and `screen=promo-result&undo=over`).
**Screenshots:** `docs/screenshots/ux-m7/<screen>-<lang>-<theme>-<width>.png`. Each screen is shot at 1440 px (en light, ar dark) and at 360 px (ar light, en dark): 64 shots, plus `promo-result-undo-over` in en at 1440 px and ar at 360 px. None scrolls sideways; the archive's segmented filter scrolls inside itself at 360 px, as every `.seg` does.

Everything in `docs/DESIGN.md` (the Stitch section and its deviations table) applies. This file only adds what M7 needs. The new classes in §11 go into `lms-stitch.css` in a small `lane:ux` PR before the M7 pages start.

---

## 1. Shared rules for M7

- **The server decides; the UI says what it decided.** Locks, eligibility, the undo window, who may approve: all are server facts. The UI shows the answer and its reason, and never guesses.
- **One pattern for actions with consequences** (#808 H1, reused by close, promotion, withdrawal, new year):
  1. A dialog whose body starts with one plain sentence about the effect.
  2. A **What will happen** list (`lms-effects`): one row per effect, an icon, a **bold count** and the words ("**184 gradebooks** are locked").
  3. **Worth checking first** rows in the warning colour, each with a Review link. They **never block** unless the server says so (D79, D84).
  4. A **typed confirmation** (`lms-confirm-type`) **only where nothing can undo the action**. In M7 that is **closing a year** (D79: no reopen). Promotion has an undo window (D84) and withdrawal has re-admission, so they use a plain confirm button.
  5. The confirm button is **filled error** (`btn-danger`) only for close year and withdraw. Promotion's Run is filled primary.
  6. Long work runs as a **job** (`lms-job`): a `role="progressbar"` with `aria-valuenow`, one `role="status"` line, and "You can leave this page". The result lives on the job's URL, so a reload shows it again.
- **Server time.** The undo clock, an effective date that starts today, and "ends in 42 days" are computed from the response's `serverNowUtc` and shown in the client time zone (D64). The undo banner shows the **clock time** ("until 14:32"), with the countdown in brackets. A countdown alone isn't enough.
- **Dates, codes and numbers** follow DESIGN.md: `yyyy-MM-dd` and `HH:mm`, invariant, in `<span dir="ltr">`. Year names (`2026/2027`), student codes, class codes, grades ("12 → 15", always LTR via `lms-diff`), URLs, DNs and filters are LTR too.
- **Names** sit in `<bdi>`. An English name in an Arabic row keeps its order.
- **Disabled, not hidden.** Continue, Undo and Approve stay visible when they can't be used, with the reason as text next to them and in `aria-describedby`. Permissions still decide whether an action exists at all.
- **Staff-only words.** A withdrawal reason (D81) and a grade-change reason are never shown to the student or the guardians. Where they appear, a small `visibility_lock` icon and "Staff only" say so once.
- **Large data (X-09).** The promotion review and the archive are server-paged `ServerTable`s. Bulk actions send the **filter**, not ids. No page loads every row.

## 2. Year-end checklist on the home page (D86)

`screen=home`. Shown to the Org Admin and Managers while a year is Active and **from 60 days before its end** (D86; the server sends the flag and the step states).
- A `SectionCard` with an `event_available` icon. Title "Year end: 2026/2027"; subtitle "The year ends on 2027-06-24, in 42 days. Each step is a link; nothing here blocks you."
- `lms-checklist`, four steps in order: **Publish the term grades**, **Close the year**, **Run the promotion**, **Open next year**. Each row has a state disc and a title, a one-line state in words, and one action at the end.
- **States:**
  - **Done:** a success disc with a check, plus what was done and when.
  - **Needs attention:** a warning disc with "!", the count, and **Review**.
  - **To do:** the step number. If the step can't start yet (the promotion before close), the title is muted and the line says why: "Available after the year is closed."
- The step state is in text, never in colour alone. The card is a list (`ol`), not a stepper: steps can be done in any order the server allows.
- **Stat tiles above** (`lms-stats`, fixed width): active students, classes, grade changes waiting, unpublished grade items.

## 3. Academic years (FR-YEAR-04/05)

`screen=years`. The existing page gains row actions; the primary header action is **Open next year** (§9).
- **Active year:** **Close year** (outlined, lock icon) and Edit.
- **Closed year:** a neutral chip with a lock icon and "Closed". Actions are **Promotion** (outlined, links to `/promotion` with this year) and **Closing report** (text).
- **Planned year:** Activate and Edit, as today. `active_year_exists` offers "Close 2026/2027 first" as a link to the Close dialog.
- **At 360 px:** each year is a labelled card. The year name is the card title, and the actions sit on their own row at the end of the card (`td.nolabel`).

## 4. Close the year (FR-YEAR-04, D79)

`screen=close`, a dialog of 640 px (full screen below 600 px).
- **Title:** "Close 2026/2027". **Lead:** "Closing locks this year for everyone. It can't be reopened; a grade that must change later goes through a manager's approval."
- **What will happen** (from `close-preview`): gradebooks locked, attendance sessions locked, "The promotion opens for 2026/2027 (Org Admin anywhere, managers in their own units)" (D78).
- **Worth checking first (doesn't block closing)** (D79): unpublished grade items, assignments still open, excuse requests waiting. Each has a count and **Review**, which opens the right page in a new tab so the dialog keeps its state.
- **Blocking problems**, if the server ever sends any (`year_close_blocked`), are an outlined error alert above the list, and **Close the year** is disabled with the reason.
- **Typed confirmation:** "To confirm, type the year name: **2026/2027**". The match is exact after trimming, and the button enables on match. The box is LTR; the label is not.
- **Actions:** Cancel (outlined) and **Close the year** (filled error, lock icon). It runs as a job if the server says it's long; otherwise the dialog closes, a snackbar says "2026/2027 is closed", and the row turns Closed.

## 5. The locked year and asking for a grade change (FR-GRD-06, D80)

`screen=gradebook` (teacher) and `screen=grade-request`.
- **On every grade and attendance page of a closed year:** a header chip "2025/2026 · Closed" with a lock icon. Under the header, an `lms-locked` strip (`role="status"`, recessed, lock icon): "This year is closed. A grade can change only with a manager's approval.", with "How it works" as a link.
- **Cells keep their values.** On focus or hover of a grade cell, a teacher with `Grades.ChangeRequest` sees **Request a change** (text button, `edit_note`). On touch it's in the cell's menu.
- **Cell states**, as a chip **under** the value, never colour alone:
  - "Change pending" (warning);
  - "Changed after close" (info, `history` icon; opens the audit entry);
  - "Change rejected" (neutral, with the note in a tooltip and in the detail).
- **The request dialog** (600 px):
  - **Fields:** student, item, current grade (read-only), **New grade** (number, LTR, "/ 20" after it), **Reason** (a text area, 20–500 characters, with a live counter).
  - **Help line:** "The approver and the audit log see it; the student doesn't."
  - **Info alert:** "A manager or the org admin decides. You can't approve your own request, and you can have one open request per grade."
  - **Actions:** Cancel and **Send request**.
  - **Errors:** `grade_change_pending` (in the dialog, as text, with a link to the pending request) and `concurrency_conflict` (reload the cell and say so, the M5 pattern).

## 6. Grade-change approvals (FR-GRD-06, D80)

`screen=grade-changes` (manager), route `/grade-changes`, nav **Year end › Grade changes** with a count badge.
- **Segmented filter:** Waiting (count) · Approved · Rejected.
- **Two columns from 900 px** (`lms-mod`, as M6 moderation): the list and the detail. **Below 900 px** the list comes first; a row opens the detail as a full-screen dialog.
- **List columns:** Student, Item, **Change** (`lms-diff`: "12 → **15**", LTR, tabular figures), Requested by, Waiting (age). A row the viewer requested shows **"Your request"** in the Requested-by cell.
- **Detail:**
  - student and class, the item;
  - "From **12 → 15** / 20" large;
  - the reason quoted on a recessed panel;
  - **History** (`lms-timeline`: requested, and the grade's earlier entries);
  - **Note**, needed to reject; the teacher sees it.
- **Actions, at the end:** **Reject** (outlined error, enabled once the note has text) and **Approve** (filled).
- **D80:** on the viewer's own request, both are disabled with "Your own requests show 'Your request'; another manager or the org admin decides them." The server enforces it with 403 `grade_change_not_allowed`.
- **Notifications:** requested, approved, rejected (#808 H8).

## 7. Promotion (FR-STU-07, D12, D78, D83, D84)

Route `/promotion`, nav **Year end › Promotion**; also the **Promotion** button on a Closed year row. A three-step wizard with `lms-steps`: **Choose · Review · Run**. Below 600 px only the current step shows its label; the others keep a visually hidden label.

**7.1 Choose** (not mocked; standard fields):
- **Source:** the source year (Closed only, D79; an Active year shows "Close 2026/2027 first" as a link).
- **Unit:** the unit picker, limited to the caller's units for a Manager (D78), and "Include the units below".
- **Target:** the target year (Planned or Active; a Closed one is refused with `year_closed`).

**7.2 Review** (`screen=promo-preview`):
- **Scope line:** "North Branch · Grade 10 · 2026/2027 → 2027/2028". The arrow is a character, so it follows the reading direction.
- **Summary tiles:** Promote, Repeat, Transfer out, No target class. The problem tile uses the error icon colour.
- **Blocking problems** (an error alert, above the warnings): rows without a target class, a missing level mapping, a student already placed. Each has **one fix link** ("Create the class", "Open the level mapping"). **Continue** is disabled with "Fix the 3 rows without a target class to continue." next to it.
- **Warnings** (a warning alert, D84): "**5 students** have an open grade-change or excuse request. They're flagged, not blocked." with **Show them**, which applies the "Only rows with a note" filter.
- **Table:**
  - **Tools:** search, class and outcome filters.
  - **Bulk bar** (`lms-bulk`) when rows are selected: "2 selected", **Set outcome ▾** and **Reset to suggested**. Selecting "all matching the filter" is offered when the page is full, and the request sends the filter.
  - **Columns:** select box; Student (name, code under it in LTR); From class; **Outcome** (a real select: Promote, Repeat, Graduate, Transfer out); **Target class** (a select limited to the mapped next level's classes, or the chip "No target class" in error); Note (warning chips "Open excuse request", "Open grade change").
  - **Suggestions** come from the level mapping only (D83). Every row is editable; an edited row shows "Changed" in the Note column, and "Reset to suggested" clears it.
  - **Saving:** each change is saved as a draft on the server. The footer says "Changes are saved as you go." and a polite region announces "Omar Saleh: Repeat".
- **Footer** (`lms-wizard-foot`): Back, then Continue at the end.
- **At 360 px:** each student is a labelled card with the selects under the name, and the bulk bar wraps.

**7.3 Run** (`screen=promo-run`):
- **Before:** Continue opens a confirm dialog with the counts ("120 promote · 4 repeat"), the effect ("New placements are created in 2027/2028, the old ones are closed, and history stays readable") and "You can undo for 10 minutes, until someone does something in 2027/2028." The confirm button is filled primary. There's no typed confirmation, because undo exists (D84; §14).
- **Running:** `lms-job` with a progress bar, "Processed 46 of 124 students." (polite, at most every 10 % or 10 s) and "You can leave this page…". One run per unit; a second one gets `promotion_in_progress` with a link to the running one.

**7.4 Result and undo** (`screen=promo-result`, and `&undo=over`):
- **Title:** says the outcome: "Promotion finished" or "Promotion finished with 2 problems". The subtitle says who ran it and when.
- **Undo banner** (`lms-undo`, info tint, `data-until` for E2E): "You can undo this promotion until **14:32** (9 minutes left), and only until someone does something in 2027/2028." **Undo promotion** is outlined, at the end. Undo runs as a job and restores **the students who were promoted** (D84).
- **When undo is over:** the banner turns neutral with the server's reason in words:
  - `promotion_undo_window_over`: "The 10 minutes to undo have passed."
  - `promotion_undo_activity_exists`: "Undo is no longer possible: attendance was recorded in 2027/2028 on 2027-08-29."
  
  The button stays visible and disabled.
- **Tiles:** Students, Promoted, Repeating, Not changed (error icon).
- **Not changed (n)** card (D84: per-student success):
  - "Nothing was changed for these students. Fix the cause, then run the promotion again for them.";
  - a table of student, target class and reason in words;
  - **Download the list** and **Run again for these 2** (filled), which re-opens Review filtered to them.
- **History** (`/promotion/runs`, "Past runs" in the header): a `ServerTable` of runs with state chips (Draft neutral, Running info, Done success, Partly done warning, Undone neutral, Failed error).

## 8. Withdrawal, graduation, archive and re-admission (FR-STU-08, FR-SEC-07, D81, D82)

**8.1 Withdraw** (`screen=withdraw`):
- **Entry:** from the student profile's **Student record ▾** menu (Withdraw, Graduate, Re-admit as the state allows). Org Admin, Manager and Counselor can withdraw (D82).
- **Dialog** (640 px; full screen below 600 px): "Withdraw Omar Saleh". **Lead:** "Omar leaves the school. You can re-admit him later; his student code stays the same."
- **Fields:**
  - **Reason:** a select from the client's list plus **Other** (D82).
  - **Effective date:** today by default; a past or future date is allowed as the server allows. The help says when sign-in stops, with the date.
  - **Note:** required for Other, otherwise optional. Help: "Staff only: the student and guardians never see the reason." (D81).
- **What will happen:**
  - signed out, and can't sign in from the effective date;
  - leaves class rosters and active lists;
  - grades, attendance and history stay, read-only for staff, in the archive;
  - **guardians keep read access to closed years** (D81).
- **Action:** **Withdraw student** (filled error). No typed confirmation, because re-admission undoes it.
- **No bulk withdrawal in v1** (D84). Year-end graduation of a whole level goes through the promotion's Graduate outcome.

**8.2 Graduate** uses the same dialog with the graduation date, no reason list, and "Graduate" as the action (filled primary).

**8.3 Archive** (`screen=archive`, route `/students/archive`, nav **Year end › Student archive**):
- Read-only and server-paged.
- **Search:** "Search by name, code or national ID".
- **Segmented filter:** All · Withdrawn · Graduated · Transferred out, plus a "Year left" select.
- **Columns:** Student (name and code), Left (status chip and date), Last class, Reason (header with the staff-only icon), and **Re-admit** (text button) for permitted roles.
- **Footer note:** "The reason is visible to staff only."
- **A row opens the read-only profile:** no edit buttons, only Re-admit; the history timeline is first.

**8.4 Re-admit** (`screen=readmit`):
- **Dialog** "Re-admit Lina Haddad". "Her student code **ST-2024-0118** stays the same." (D25).
- **Fields:**
  - **Year:** Active or Planned only.
  - **Class:** shows its room, "10-B · 24 of 30"; a full class is disabled with "Full".
  - **Effective date** and **Reason**.
- **Info alert:** sign-in is restored, guardian links return, and the placement history shows "Re-admitted".
- **Errors:** `readmit_class_full` and `readmit_year_closed`, in words, on the field.

**8.5 Sign-in after leaving** (FR-SEC-07): "This account is not active. Contact your school." The same words for withdrawn, graduated and expired accounts.

## 9. Open the next year (FR-YEAR-05, FR-ORG-07/08)

`screen=new-year`. **Open next year** on the years page.
- **Wizard** in a 760 px dialog (full screen below 600 px), `lms-steps`: **Dates · Structure · Review · Create**.
- **Dates:** the name is suggested from the previous year ("2027/2028"); start and end; terms copied with shifted dates in a small preview table.
- **Structure** (mocked): "How should the new year start?" as three **option cards** (`lms-option`, radios with a sentence each):
  - **Copy the class structure from 2026/2027**;
  - **Apply a template**;
  - **Both**.
- **When copying:**
  - **Units to copy:** removable chips.
  - **What to copy:** checkboxes with their counts: Classes 48, Class courses 312, Course blueprints 96, Timetable periods 8.
- **Info alert, bold first line:** "**Students are never copied.** Place them with the promotion run after this year is closed." Then "The new year is created as Planned; you activate it when it starts."
- **Review:** counts of what will be created, and **skipped locked settings** as an info list (the template page's pattern).
- **Create:** a job, then a result with links: "Go to 2027/2028", "Create more classes", "Start the promotion run".
- **Templates:** the existing list and preview, with `{Year}` defaulted to the new year's name.

## 10. SAML 2.0 and LDAP (FR-SEC-08, D85)

Both live in the existing **Sign-in providers** dialog (720 px; full screen below 600 px). The kind select shows only that kind's fields.

**10.1 SAML** (`screen=idp-saml`):
- **Names:** the display name in Arabic and in English (`bdi`).
- **Identity provider metadata:** a segmented choice of **Metadata URL** or **Upload XML** (XML only, with a size cap); URLs are LTR monospace.
- **Signing certificate:** the fingerprint in monospace, and a warning chip "17 days" **from 30 days before expiry**. The help line gives the date and what to do.
- **"Give these to your identity team":** reply (ACS) URL, entity ID, and our metadata URL. Each is a `lms-copy-row` with **Copy**; the value is LTR monospace and wraps anywhere.
- **Matching:** email attribute and national ID attribute (LTR).
- **Info alert:** "Sign-in starts from the LMS (SP-initiated). Starting from the identity provider's portal isn't supported in v1." (D85). Signed assertions are always required and aren't shown as a switch.
- **Actions:** **Test connection** at the start, then Cancel and **Save**.

**10.2 LDAP** (`screen=idp-ldap`):
- **Server and port** (LTR).
- **Secure connection:** a segmented **LDAPS · StartTLS**, with "Unencrypted LDAP isn't offered." under it (D85).
- **Service account:** the bind DN (LTR monospace), and the **service account password**, which is write-only. It reads "Stored. Leave empty to keep it." and is never sent back.
- **Search:** the search base and user filter (LTR monospace).
- **Info alert:** "Roles are assigned in the LMS. Directory groups aren't mapped to roles in v1." (D85).
- **Test result** (`role="status"`, `lms-checks`): one line per check, each with a success or error icon **and words**, for example "The search found no user for 'faisal.q'. Check the search base." Never a stack trace, never the password.

**10.3 Sign-in page** (`screen=login-ldap`, no app shell):
- **Header:** the brand, the language switch, and the title "Sign in to Al Noor Schools".
- **Provider buttons:** one per enabled provider, outlined and full width, with its display name (SAML is a redirect).
- **Divider:** "or with your school account".
- **LDAP form:** Username (`autocomplete="username"`, LTR), Password (`current-password`, LTR), and **Sign in** (filled, full width).
- **One error for every failure:** "Sign-in failed. Check your username and password and try again.", in an error alert with `role="alert"` above the fields. It's the same for a wrong user, a wrong password, and an account the LMS doesn't know.
- **Footer:** "Trouble signing in? Contact your school."

## 11. New classes (`lms-stitch.css`)

| Class | What it draws |
|---|---|
| `lms-steps` | Wizard steps: number disc, label; current filled primary, done tinted with a check; connector lines; follows reading order. Below 600 px non-current labels are visually hidden |
| `lms-effects` | "What will happen" rows on recessed panels: icon, bold count, words, optional link; `.warn` rows use the warning icon colour |
| `lms-confirm-type` | Typed-confirmation block with an error-tinted border |
| `lms-job`, `lms-bar` | Job progress: status line and an 8 px bar (`role="progressbar"`) |
| `lms-undo` (`.over`) | Undo banner: info tint with the clock time and the action at the end; neutral when the window is over |
| `lms-locked` | Locked-year strip on grade and attendance pages |
| `lms-diff` | "old → new" grades: LTR isolate, tabular figures |
| `lms-checklist` | Year-end checklist rows with state discs (done, warn, to do) |
| `lms-copy-row` | Read-only value with a Copy button (SP values, fingerprints) |
| `lms-checks` | Test-connection results, one check per line with an icon and words |
| `lms-option` | Radio option card with a sentence |
| `lms-bulk` | Bulk-action bar over a table |
| `lms-wizard-foot` | Wizard footer: saved state at the start, the reason and the actions at the end |
| `btn-danger` | Filled error button, used only for close year and withdraw |

`lms-mod` (two columns, M6) and `lms-stats` (`lms-stats-fixed`, #711) are reused. Every new rule uses logical properties and no uppercase, and the colours come from the palette (StitchStylesheetTests).

## 12. Accessibility checklist for M7 (X-10, D87: WCAG 2.1 AA)

- [ ] **Dialogs:** every dialog has `aria-labelledby` on its title, returns focus to the opening button, and is full screen below 600 px with the actions sticky at the bottom.
- [ ] **Typed confirmation:** the box has a visible label that includes the text to type. The button's disabled reason is in `aria-describedby`.
- [ ] **Job progress:** `role="progressbar"` with `aria-valuenow`/`max` and a name. Announcements are polite and throttled (10 % or 10 s), never per row.
- [ ] **Undo banner:** the clock time is in the text. The countdown isn't announced every second, only when the window ends ("Undo is no longer possible…", polite).
- [ ] **Promotion review:** outcome and target are native selects named "Outcome for Omar Saleh". Changes are announced politely, and the bulk bar is a `toolbar`.
- [ ] **State in words:** cell states, checklist states, test results and status chips all have text, never colour alone. Warning and error colours meet 3:1 for icons and 4.5:1 for text in both themes (DESIGN.md tokens).
- [ ] **Disabled actions:** Continue, Undo, Approve and Reject say why in text next to them.
- [ ] **Sign-in:** autocomplete tokens are set and the error has `role="alert"`. No password ever goes in a URL or a log.
- [ ] **Targets:** 44 px below 600 px (`lms-touch`) for every button, select and row action.
- [ ] **Steps:** `lms-steps` is an `ol` with `aria-current="step"`.

## 13. Owner decisions applied (D78–D87)

| Decision | Where |
|---|---|
| D78 Org Admin anywhere, Manager in own units | §4 effect line, §7.1 unit picker limited for managers |
| D79 Close before promote; no reopen; unpublished grades warn | §3 actions, §4 typed confirmation and "doesn't block" list, §7.1 source year Closed only |
| D80 Manager or Org Admin approves, never the requester | §5 info line, §6 "Your request" and disabled actions |
| D81 Guardian keeps closed years; reason staff-only | §8.1 effect list and note help, §8.3 staff-only column |
| D82 Who withdraws; reason list plus Other with a note | §8.1 |
| D83 Suggestion from level mapping only; every row editable | §7.2 |
| D84 Warnings not blocks; per-student success; undo restores the successful ones; no bulk withdrawal | §7.2 warnings, §7.4 "Not changed" card and undo, §8.1 |
| D85 SP-initiated SAML; LDAPS or StartTLS; no group mapping | §10 |
| D86 Checklist from 60 days | §2 |
| D87 WCAG 2.1 AA | §12 |

## 14. Alignment with #808

This spec follows #808's tasks H1–H9 and its `data-testid` table. It asks #808 to change:

1. **No typed confirmation for the promotion run** (H4 proposes typing the student count). D84 gives the run an undo window, so a confirm dialog with the counts is enough. Typing stays only for **close year**, which nothing can undo (D79).
2. **Wizard step names:** **Choose · Review · Run** (H3 says Preview and Execute). They're plainer, and "Run" matches "Past runs".
3. **Checklist step 1** is **Publish the term grades** (H8 says "review open grades"). It names the action that clears the D79 warning.
4. **Undo restores the successful students** (D84), so the result page's failure list keeps a **Run again for these n** action that reopens Review filtered to them (H4 doesn't name it).
5. **Grade-change cell states** are text chips under the value (H2 says "a tooltip"). A tooltip alone isn't reachable on touch.
6. **The withdrawal and grade-change reasons** carry a visible "staff only" marker where staff read them (D81), not only a rule in the API.
7. **Test ids** to add to H9's table: `yearend-step-{n}` exists; add `promo-undo-banner` with `data-until` (H4 has it), `year-close-confirm-input` (H2 has it) and `readmit-confirm`; nothing else changes.
