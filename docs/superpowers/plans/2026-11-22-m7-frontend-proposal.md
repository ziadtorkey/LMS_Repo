# M7 frontend proposal: year-end, promotion, lifecycle, external sign-in, release pass

> **Status: DRAFT proposal for the Team Lead (no code).** Author: FE2. Frontend half of M7 (Year-End & Release, DEV-PLAN §2), written in the format of the M6 FE proposal (#612) and `2026-11-15-m4b-frontend-proposal.md`. The backend half does not exist yet, so every API name below is **proposed**; the Lead's M7 contract PRs decide the final names (section 4). No code was changed.
>
> **Sources read:** PRD FR-YEAR-01/04/05, FR-STU-07/08, FR-GRD-06, FR-SEC-07/08, FR-ORG-07/08/09, X-09, X-10, D12, D15, D17, D22, D23, D27, PRD §9; DEV-PLAN §2 M7; the merged client code this builds on (section 1, verified on `origin/main`, 2026-11-14); the M5 plan (grade lock seam, Task 12) and the M6 plan (moderation and rules patterns).

## 0. Scope and shape

M7 is the year-end cycle plus the release pass. For the frontend it is five product surfaces and one cross-cutting pass:

1. **Close year and grade-change approval** (FR-YEAR-04, FR-GRD-06): an Org Admin closes a year (locks grades and attendance read-only, opens the promotion workflow); after the lock, a teacher asks for a grade change and a Manager approves or rejects it; every decision is audited.
2. **Promotion run** (FR-STU-07, D12): choose source year and unit and a target year; per-student outcome (promote, repeat, graduate, transfer out) with a **preview**; **execute as a job** with progress; an **undo window** (default 10 minutes, a setting) that ends at the first activity in the new year; history stays readable.
3. **Withdrawal, graduation, archive, re-admission** (FR-STU-08, FR-SEC-07): a student leaves with a reason and an effective date, becomes inactive and cannot sign in; a read-only **archive** search; **re-admission** back into a class.
4. **Open a new year from copy or template** (FR-YEAR-05, FR-ORG-07/08): copy last year's class structure and/or apply a taxonomy template with placeholders, with a preview and a result summary.
5. **SAML 2.0 and LDAP sign-in** (FR-SEC-08 part 2): two new provider kinds in the existing identity-provider admin and on the sign-in page.
6. **Release pass** (X-10, X-09 hooks, PRD §9): the accessibility and 360 px sweep over every page added since M5, and the stable `data-testid` hooks for the PRD §9 acceptance suite in ar and en.

What the frontend deliberately does **not** do: any promotion rule, lock, undo-window or eligibility decision (the server owns them, and the UI shows what the server answers); a billing or archive-export product; a grade-recalculation UI (M5 owns the engine); performance work on the server (X-09 is a backend pass, the frontend only supplies the large-data demo checks in H9); a free-text "reason" that is shown to the student (reasons are staff-only, see Q4).

### Design and conventions (apply to every task, same as M4b and M6)
- `docs/DESIGN.md`, the Stitch screens and the lms-ui skill: `PageHeader` (one filled primary action), `SectionCard`, `ServerTable`, `EmptyState`, `LoadingState`, labelled-card tables at 360 px, logical CSS, Rounded icons, mirrored direction icons, ISO dates `yyyy-MM-dd` left-to-right inside RTL text, sentence case, no inline colours or font sizes.
- Every key in ar and en (parity test), bUnit tests first, a `Page_renders_rtl_in_arabic` per page, screenshots in `docs/screenshots/pr-<name>/` at 1440 and 360 px in ar and en, light and dark, 0 warnings, files under about 300 lines.
- API clients follow the usual pattern: `I<Module>Api` + HTTP implementation using `Routes.*` and `ProblemCodes.*` + an in-memory fake registered only when `Lms:UseFakeApi=true`; the fakes **mimic the server rules the UI depends on** (section 4.2).
- Server time: every window (the undo countdown, the effective date, a lock that begins today) is computed from `serverNowUtc` of the response, never from the browser clock; the client time zone setting (D64) decides how a date is shown.
- Resource key prefixes: `YearClose_*`, `GradeChange_*`, `Promo_*`, `Lifecycle_*`, `Archive_*`, `NewYear_*`, `Idp_*` (existing), `Login_*`, `Error_<code>` for each new problem code.
- **Destructive and long actions** follow one pattern, defined once in H1 and reused by H2, H4 and H6: a confirmation dialog that states the effect and the count, a typed confirmation only where the action cannot be undone, a job with a progress `role="status"` region, and a result summary that survives a page reload (the job id is in the URL).

## 1. What M7 builds on (verified on `origin/main`)

| Seam | Where | M7 use |
|---|---|---|
| Academic years | `Pages/AcademicYears/AcademicYearsPage`, `YearDialog`, `TermsPanel`, `CalendarPanel`; `AcademicYearStatuses` (Planned, Active, Closed); a Closed year already shows a lock icon and hint | Adds the **Close year** action and the "Open next year" entry on this page; Closed rows link to the closing report |
| Identity providers | `Pages/Access/IdentityProvidersPage`, `IdentityProviderDialog`; `IdentityProviderKinds = [Oidc, AzureAd]` | New kinds `Saml` and `Ldap` with their own field groups (H7) |
| Students | `Pages/Students/StudentListPage`, `StudentProfilePage`, `CreateStudentPage`, `ChangeRequestQueuePage`; `PersonStatuses`, `PersonStatusChip`, `PersonStatusDialog` (staff) | The lifecycle dialogs reuse the status chip and history table; the archive is a filter and a page next to the list (H5) |
| Org templates | `SaveTemplateDialog`, `IOrgApi`, `ApplyTemplateRequest` with placeholders (`{Year}`, `{Name}`), `ApplyTemplateResultDto` | The new-year wizard calls the same template list and apply, adding a year placeholder default (H6) |
| Level transfer | `AcademicFlagsTab` (Level Transfer flag and mapping, FR-ORG-09) | The promotion preview reads the mapping; a missing mapping is a blocking row in the preview, with a link to this tab |
| Grade lock | M5 `gradebook_locked` (409), lock seam (M5 Task 12), `PlacementGuard` (closed class or year 409) | Close year turns the seam on; every grade, attendance and assignment page already maps the 409, H2 only adds the "request a change" entry on the locked state |
| Jobs and progress | the import jobs of M2/M5 (`StudentImportPage`, column import) | The same polling and progress pattern for promotion and copy jobs |
| Notifications | `NotificationText` + parity test | New events (H1, H2, H4) in ar and en |
| Sign-in page | `Pages/Authentication`, `RedirectToLogin`, the provider list shown by the login screen | External provider buttons for the new kinds (H7) |

## 2. Permissions (proposed, finalised by the Lead's contract PR)

| Constant | Who | Frontend use |
|---|---|---|
| `Years.Close` | Org Admin (D17 keeps year editing with Org Admin) | Close year, reopen window if any (Q2) |
| `Years.Open` (or reuse `Years.Manage`) | Org Admin | New-year wizard |
| `Grades.ChangeRequest` | Teacher on own class courses | "Request a change" on a locked grade |
| `Grades.ChangeApprove` | Manager (FR-GRD-06) | Approval queue; Org Admin per Q3 |
| `Promotion.Run` | Org Admin, Manager (Q1) | Preview, execute, undo |
| `Students.Lifecycle` | Org Admin, Manager, Counselor for withdrawal (Q5) | Withdraw, graduate, re-admit |
| `Students.Archive.View` | Org Admin, Manager, Counselor, Supervisor | Archive search (read-only) |
| `IdentityProviders.Manage` (existing) | Client-root admin (D18) | SAML and LDAP setup |

The UI hides what the caller lacks and never relies on that; the server answers 403 or 404 as everywhere else. The relationship rule (a Manager acts only inside their units) is server-side; the fake mimics it.

## 3. Task list

Each task = one issue = one PR. Sizes: S up to 1 day, M about 2 days, L 3 days or more. "BE" names the backend task the UI waits for or fakes. H1 is first, as it defines the shared job pattern.

### H1: Year-end clients, fakes and the shared job panel (M)
**PRD:** FR-YEAR-04/05, FR-STU-07/08, FR-GRD-06. **Depends:** M7 contract wave (Lead).
`IYearCloseApi` (preview of what closing locks, close, closing report), `IGradeChangesApi` (list, request, approve, reject), `IPromotionApi` (create run, preview, execute, run status, undo, history), `ILifecycleApi` (withdraw, graduate, readmit, archive search), `INewYearApi` (preview copy, run copy), each with HTTP implementation and a fake. Shared components: `JobProgressPanel` (a polled job with a `role="status"` region, "x of y", cancel where the server allows, a result card that survives a reload through the job id in the URL), `ConfirmEffectDialog` (effect text, count, optional typed confirmation), `YearChip` (name, status, lock icon).
**Tests:** `Fake_close_locks_gradebooks_and_blocks_writes_with_gradebook_locked`, `Fake_promotion_undo_is_refused_after_activity_or_after_the_window_by_server_time`, `Fake_withdrawn_student_cannot_sign_in_and_is_not_in_active_lists`, `Fake_grade_change_needs_a_different_approver_than_the_requester`, `JobProgressPanel_announces_progress_politely_and_keeps_the_result_after_reload`.
**Keys:** `Job_*`, `Confirm_*`.

### H2: Close year and grade-change approval (L)
**PRD:** FR-YEAR-04, FR-GRD-06. **Depends:** H1, BE close-year task, M5 lock seam.
- **Close year** on `AcademicYearsPage`: a row action (Active years only, `Years.Close`) opens `CloseYearDialog`: a **what will happen** list read from the preview (gradebooks locked, attendance sessions locked, open assignments by class course, terms still open, ungraded items, unpublished grade items) with counts and links; **blocking** problems (a term without published grades if the client requires it, Q2) shown as an outlined alert with the next step; a typed confirmation (the year name) because closing cannot be undone by the UI. After closing, the row shows the Closed chip and a "Promotion" button (H3) and a "Closing report" link.
- **Locked state on grade and attendance pages** (M5 pages, a small shared component `LockedNotice` added to them in this PR, not a rewrite): "This year is closed. Grades can only change with a manager's approval." with a **Request a change** button for a teacher with `Grades.ChangeRequest`.
- `GradeChangeRequestDialog`: student, item, current value, new value, **reason (required, 20 to 500 characters)**; one open request per grade; the teacher then sees the request state on the cell (pending, approved, rejected) with a tooltip, not a hidden color.
- `GradeChangeQueuePage` (route `/grade-changes`, nav under the year-end group, `Grades.ChangeApprove`): `ServerTable` filtered by state, year, class course; row shows student, item, old to new value (left-to-right numbers), requester, reason, age; detail drawer with the audit trail; **Approve** and **Reject** (reject needs a note); the requester cannot approve their own request (button disabled with the reason; the server enforces it). Approved changes appear in the gradebook with a "changed after close" marker and a history entry (audit).
- Notification labels for requested, approved, rejected.
- Error mapping: `year_closed`, `gradebook_locked`, `grade_change_pending`, `grade_change_not_allowed`, `concurrency_conflict` (M5 pattern: reload the row, say so).
**Tests:** `Close_dialog_lists_what_will_be_locked_and_blocks_on_blocking_problems`, `Close_requires_the_typed_year_name`, `Locked_grade_cell_offers_a_change_request_to_the_teacher_only`, `Request_requires_a_reason_of_20_characters`, `Requester_cannot_approve_own_request`, `Approved_change_shows_the_changed_after_close_marker`, `Queue_page_renders_rtl_in_arabic`, `Queue_is_a_stacked_card_list_at_360px`.
**Keys:** `YearClose_*`, `GradeChange_*`, `Error_grade_change_*`.

### H3: Promotion run: set-up and preview (L)
**PRD:** FR-STU-07, FR-ORG-09. **Depends:** H1, BE promotion preview task.
- Route `/promotion` (nav under the year-end group; also a "Promotion" button on a Closed year row), a **three-step wizard** (`lms-steps`, as the package upload does): **1. Choose** source year and unit (the unit picker reused from the student list), target year (Planned or Active; a closed year is refused), "include sub-units"; **2. Preview**; **3. Execute** (H4).
- **Preview table** (`ServerTable`, filterable by class, outcome, problem): per student: name, code, source class, **outcome** (select: Promote, Repeat, Graduate, Transfer out), target class (select, limited to the target year's classes of the mapped next level), a problem cell. Bulk actions: set outcome for the filtered rows ("Repeat for all selected", "Graduate the final level"), and "Reset to the suggested outcome". The suggestion comes from the server (level transfer mapping and, if configured, a pass or fail rule from M5; Q6). The table keeps **edits as a draft on the server** (`PUT /promotion/runs/{id}/decisions`), so a reload keeps them; the client holds nothing the server does not have.
- **Summary strip** (`lms-stats`): counts per outcome, students without a target class, capacity warnings per target class (over capacity is a warning, not a block, unless the server says so), students with an open balance or an open request that the server flags (a flag list, Q7).
- **Blocking problems** (no level mapping, a target class missing, a student already placed in the target year) are listed above the table with a link to the fix (the Level Transfer tab, the class list) and disable **Continue**.
- **Large runs** (X-09, 50,000 students per client): the preview is server-paged and sorted; bulk actions send a filter, not ids (`SetOutcomeForFilterRequest`); the UI never loads all rows.
- Accessibility: outcome cells are real `select`s with a column label and a row label; changes announce "Ahmed: Repeat" in a polite region; keyboard: Tab through selects, no custom grid.
**Tests:** `Preview_is_paged_and_filters_are_server_side`, `Outcome_change_is_saved_as_a_draft_and_survives_reload`, `Bulk_set_sends_a_filter_not_ids`, `Missing_level_mapping_blocks_continue_and_links_to_the_fix`, `Over_capacity_class_warns_but_does_not_block`, `Closed_target_year_is_refused`, `Preview_renders_rtl_in_arabic`, `Preview_is_a_stacked_card_list_at_360px`.
**Keys:** `Promo_*`.

### H4: Promotion run: execute, progress, result, undo window (L)
**PRD:** FR-STU-07, D12. **Depends:** H3, BE execute job and undo.
- **Execute** step: a summary ("120 promote, 6 repeat, 40 graduate, 3 transfer out"), the effect in words ("New class placements are created, old ones are closed, history stays readable"), `ConfirmEffectDialog` with a typed confirmation of the number of students, then `JobProgressPanel` ("Processed 430 of 1,200", polite announcements every 10 percent or 10 seconds, whichever is later).
- **Result card:** counts per outcome, failures with the reason per student (a downloadable list, `download.js`), a clear "Nothing was changed for these students" for the failed ones (the job is per student and idempotent, Q8), and the **undo banner**: "You can undo this promotion until 14:32 (9 minutes left), and only before anyone does something in the new year." A countdown computed from `serverNowUtc` and `undoUntilUtc`, a visible time, not only a countdown; the **Undo** button is disabled after the window with the reason from the server (`promotion_undo_window_over` or `promotion_undo_activity_exists`, each with its own plain words).
- **Undo** runs as a job with the same panel; the result says what was restored. After undo the run can be edited and executed again (H3).
- **History page** (`/promotion/runs`): `ServerTable` of runs (year, unit, who, when, counts, state: Draft, Running, Done, Undone, Failed, Partially failed), row opens the result card; read-only.
- A student's profile shows the promotion in its placement history (M3 placement table; one new column "Reason").
- Error mapping: `promotion_in_progress` (409, only one run per source unit at a time), `promotion_conflict`, `year_not_closed` (if Q2 requires closing first), `feature_disabled`.
**Tests:** `Execute_requires_the_typed_student_count`, `Progress_is_announced_politely_not_per_row`, `Result_survives_reload_through_the_job_id`, `Undo_countdown_uses_server_time_and_shows_the_clock_time`, `Undo_is_disabled_with_the_reason_after_the_window_or_after_activity`, `Failed_students_are_listed_with_a_reason_and_downloadable`, `Second_run_for_the_same_unit_is_refused_while_one_is_running`, `History_renders_rtl_in_arabic`.
**Keys:** `Promo_Run_*`, `Promo_Undo_*`, `Error_promotion_*`.

### H5: Withdrawal, graduation, archive and re-admission (L)
**PRD:** FR-STU-08, FR-SEC-07, D15, D27. **Depends:** H1, BE lifecycle tasks.
- `StudentProfilePage`: the existing Deactivate button stays for **temporary** deactivation (FR-STU-01 status); this task adds a **Student record** menu with **Withdraw**, **Graduate** and (on an inactive, withdrawn or graduated student) **Re-admit**. `WithdrawDialog`: reason (a list from the client setting plus "Other" with a note), **effective date** (date picker, today or past by default, a future date shows "sign-in stops on that date"), a plain statement of the effect (signs out and blocks sign-in, leaves active lists and class rosters, history and grades stay readable by staff, the guardian keeps read access to history or not per Q4). A **GraduateDialog** is the same with the graduation date and no reason list.
- **Archive** (`/students/archive`, the same list component with `Mode="Archive"`): read-only `ServerTable`, search by name, code, national id; filters year left, outcome (Withdrawn, Graduated, Transferred out), unit; a row opens a **read-only profile** (no edit, no status button except Re-admit for permitted roles), with the history timeline, grades by year and attendance totals as the server allows.
- `ReadmitDialog`: choose the year and class (only classes with room and in an active or planned year), the effective date, and a reason; the student code is **kept** (D25: codes are never reused), the sign-in is restored and the guardian links return. A re-admitted student shows a "Re-admitted" row in the placement history.
- **Sign-in message** (FR-SEC-07): the blocked-login page for withdrawn, graduated or expired users is a distinct plain message that does not say why in detail ("This account is not active. Contact your school.") in ar and en; a test asserts the same words for withdrawn and for expired so nothing leaks.
- **Bulk path:** a withdrawal of many students at once is **not** in v1 (Q9); the promotion run (H3/H4) covers year-end graduation.
- Error mapping: `student_already_inactive`, `effective_date_invalid`, `readmit_class_full`, `readmit_year_closed`, `placement_conflict`.
**Tests:** `Withdraw_requires_a_reason_and_an_effective_date`, `Future_effective_date_explains_when_sign_in_stops`, `Withdrawn_student_leaves_active_lists_and_appears_in_the_archive`, `Archive_is_read_only_for_a_supervisor`, `Readmit_keeps_the_code_and_restores_sign_in`, `Blocked_sign_in_message_is_the_same_for_withdrawn_and_expired`, `Archive_renders_rtl_in_arabic`, `Archive_is_a_stacked_card_list_at_360px`.
**Keys:** `Lifecycle_*`, `Archive_*`, `Login_Blocked_*`.

### H6: Open a new year from copy or template (M)
**PRD:** FR-YEAR-05, FR-ORG-07/08, D22. **Depends:** H1, BE new-year task.
- `AcademicYearsPage`: on a Closed (or Active) year row, **Open next year** starts `NewYearWizard` (a full-screen dialog under 600 px, as `NewConversationDialog` does): **1. Dates** (name suggested from the previous year, start and end, terms copied with shifted dates, a preview of the term dates), **2. Structure** (radio: **Copy class structure from `<year>`**, **Apply a template**, **Both**; when copying: pick the units, tick what to copy: classes, class courses, blueprints, timetable periods; "students are never copied", said in words), **3. Review** (counts: classes, class courses, periods that will be created; skipped items such as locked settings, listed), **4. Create** (job, `JobProgressPanel`, result with links to the new year, "Create more classes" and "Start the promotion run").
- Templates: the existing list and preview (FR-ORG-07) with the `{Year}` placeholder defaulted to the new year name; `ApplyTemplateResultDto.SkippedLockedSettings` shown as an info list, as the template page does today.
- The new year is created **Planned**; activating it stays the existing "Activate" button (one Active year per defining unit, D22: the server says `active_year_exists` and the UI offers "Close `<year>` first" as a link to H2).
- Error mapping: `year_overlap`, `active_year_exists`, `template_not_found`, `copy_nothing_selected`.
**Tests:** `Wizard_suggests_the_next_year_name_and_shifted_term_dates`, `Copy_step_says_students_are_never_copied`, `Review_lists_skipped_locked_settings`, `Active_year_exists_offers_the_close_year_link`, `Wizard_is_full_screen_below_600px`, `Wizard_renders_rtl_in_arabic`.
**Keys:** `NewYear_*`.

### H7: SAML 2.0 and LDAP providers: set-up and sign-in (M)
**PRD:** FR-SEC-08 (part 2), FR-SEC-07. **Depends:** BE SAML and LDAP task. Last in the plan, by the PRD note.
- `IdentityProviderDialog`: the kind select gets **SAML 2.0** and **LDAP / Active Directory**; each kind shows only its own field group (the existing pattern for Azure AD).
  - **SAML:** metadata **URL or XML upload** (`AssetUploader` transport, XML only, size cap), entity id, SSO URL, signing certificate (read-only fingerprint after import, with the expiry date and a warning 30 days before), name-id format, attribute mapping for email and national id (plain text inputs, left to right), "require signed assertions" (on, not editable in v1, Q10). Read-only **SP values** the school gives the identity team: **ACS URL, entity id, metadata URL**, each with a copy button (the existing `Idp_Copy` pattern).
  - **LDAP:** host, port, transport (LDAPS or StartTLS, **no plain option in v1**), bind DN, bind password (write-only, never read back, shown as "stored"), user search base and filter, attribute for email and national id, **optional** group-to-role mapping is out of v1 (Q10).
  - **Test connection** button for both (`POST …/test`): shows a result card with the checks that passed or failed in plain words, never a stack trace or the bind password; a failed test does not block saving a disabled provider.
- The providers list gets the two kind icons, a status chip (Enabled, Disabled, **Needs attention** for an expiring or failing certificate), and the existing delete and enable actions.
- **Sign-in page:** the buttons for enabled providers (the existing pattern) use the provider display name in ar and en; **LDAP shows an inline username and password form** (the only password the LMS ever types for an external account, `autocomplete="username"` and `current-password`, no password in any URL, error text generic: "Sign-in failed", the same words for a wrong user and a wrong password). SAML is a redirect button. A user who is not matched sees the existing "your account was not found, contact your school" message (no auto-provisioning, FR-SEC-08), the same words for every failure.
- First-login linking: the existing link flow is reused; the matching key shown to the admin is the provider's `MatchBy`.
- Tests are bUnit against the fake provider API; the real SAML and LDAP exchange is a QA E2E against a test IdP container (QA task, not here).
**Tests:** `Saml_kind_shows_only_saml_fields_and_the_sp_values_with_copy`, `Metadata_upload_accepts_xml_only_and_shows_the_certificate_fingerprint_and_expiry`, `Ldap_has_no_plain_transport_option`, `Bind_password_is_write_only_and_never_rendered`, `Test_connection_shows_plain_results_and_no_secret`, `Ldap_sign_in_form_uses_one_generic_error_for_wrong_user_and_wrong_password`, `Provider_list_flags_an_expiring_certificate`, `Dialog_renders_rtl_in_arabic`, `Dialog_is_full_screen_below_600px`.
**Keys:** `Idp_Saml_*`, `Idp_Ldap_*`, `Login_Ldap_*`.

### H8: Year-end home, navigation and notifications (S)
**PRD:** FR-YEAR-04, FR-STU-07. **Depends:** H2 to H6.
- A **Year-end** section in `NavCatalog` (a new `NavSection_YearEnd`, shown only when the caller holds at least one of its permissions): Close year (links to the years page), Grade changes, Promotion, Archive.
- A **checklist card** on the Org Admin and Manager home while a year is Active and within 60 days of its end date (server flag, Q11): the four steps (review open grades, close year, run promotion, open next year) with the state of each; every step is a link; nothing blocks, it only guides.
- Notification events: grade change requested, approved, rejected; promotion finished, failed, undone; new year created; certificate expiring (admin). All in ar and en with the parity test.
**Tests:** `Year_end_section_is_hidden_without_any_of_its_permissions`, `Checklist_shows_the_state_of_each_step`, `New_events_have_ar_and_en_labels`.
**Keys:** `NavSection_YearEnd`, `YearEnd_*`, `Notif_*`.

### H9: Release pass: accessibility, 360 px, RTL, hooks for the acceptance suite (M)
**PRD:** X-10, X-09 (frontend part), PRD §9. **Depends:** H1 to H8, M5 and M6 pages.
- **X-10 sweep over the pages added since M5** (M5 gradebook, timetable, attendance, excuses; M6 messaging, announcements, moderation, settings; M7 above): one scan test per area, reusing the M6 `Problems()` helper (labelled controls, `aria-labelledby` that resolves to text, accessible names on icon buttons, one `h1`, no duplicate ids), `Every_dialog_returns_focus`, reduced motion, forced colors, 44 px targets, stacked layout under 600 px for every table, live regions that are `status` or `alert` as intended.
- **Keyboard walk** for the three new flows (close year, promotion run, withdrawal) written as bUnit key sequences, plus a **manual checklist** in the PR for screen reader behaviour (NVDA and VoiceOver, ar and en) that QA signs.
- **Large data checks (X-09, frontend side):** the seeded 50,000-student client is used to confirm that **no page loads all rows** (a test fails if a list API is called without a page size), the promotion preview and archive stay server-paged, and a Playwright timing check (QA) is given stable hooks.
- **PRD §9 hooks:** the `data-testid` table in section 6, written into `docs/m7-e2e-testids.md` like the M5 and M6 files, agreed with QA before the E2E tasks start; the §9.3 scenario "Year close → promotion run → new year opened" needs, from the UI: `year-close-*`, `promo-*`, `newyear-*` ids and nothing time-based (the undo countdown has a fixed `data-until` attribute QA can read).
- **ar and en parity and RTL screenshots** for every M7 page (light and dark), and the designer's sign-off against the M7 design spec (a UX task, Q12).
**Tests:** `Every_m7_dialog_returns_focus`, `Every_table_has_a_stacked_layout_class_below_600px`, `Every_icon_button_has_a_label`, `No_list_page_requests_an_unpaged_list`, `M7_resources_have_ar_en_parity`, `Every_e2e_hook_in_the_testids_file_exists_in_markup`.

## 4. Client contracts needed (for the Lead's M7 contract PRs)

### 4.1 Routes and DTOs (proposed names; the Lead decides)
| Area | Route | DTOs (ids and plain data only) | Notes |
|---|---|---|---|
| Close year | `GET /api/academic-years/{id}/close-preview`, `POST /api/academic-years/{id}/close` | `YearClosePreviewDto` (counts, `Blocking[]`, `Warnings[]`), `CloseYearRequest(Confirm)` | 409 `year_close_blocked` carries the blocking list |
| Closing report | `GET /api/academic-years/{id}/closing-report` | counts per class | read-only |
| Grade changes | `POST /api/gradebook/entries/{id}/change-requests`, `GET /api/grade-change-requests`, `POST …/{id}/approve`, `POST …/{id}/reject` | `GradeChangeRequestDto`, `CreateGradeChangeRequest(NewValue, Reason)`, `DecideGradeChangeRequest(Note)` | requester cannot decide, 403 `grade_change_not_allowed`; audited |
| Promotion | `POST /api/promotion/runs`, `GET …/{id}`, `GET …/{id}/rows`, `PUT …/{id}/decisions`, `PUT …/{id}/decisions/filter`, `POST …/{id}/execute`, `POST …/{id}/undo`, `GET /api/promotion/runs` | `PromotionRunDto` (state, counts, `UndoUntilUtc`, `ServerNowUtc`, `JobId`), `PromotionRowDto` (outcome, target class, problem code), `SetOutcomeForFilterRequest` | job state through the common job route; one run per source unit |
| Lifecycle | `POST /api/students/{id}/withdraw`, `…/graduate`, `…/readmit`, `GET /api/students/archive` | `WithdrawStudentRequest(ReasonKey, Note, EffectiveDate)`, `ReadmitStudentRequest(ClassId, EffectiveDate, Reason)`, `ArchivedStudentDto` | the archive is server-paged and read-only |
| New year | `POST /api/academic-years/{id}/open-next/preview`, `POST …/open-next` | `OpenNextYearRequest(Name, Start, End, CopyFrom, Include[], TemplateId?)`, `OpenNextYearPreviewDto` | job; creates a Planned year |
| Providers | `/api/identity-providers` (existing) + `POST …/{id}/test`, `POST …/saml/metadata` | `IdentityProviderKinds.Saml`, `Ldap`; SAML and LDAP fields; `ProviderTestResultDto` (checks by plain code) | secrets write-only; `SpMetadataDto` for the SP values |

### 4.2 Server rules the fakes must mimic
Close: locks grades and attendance (409 `gradebook_locked`, `year_closed`); one Active year per defining unit. Promotion: preview is deterministic from the level mapping; one run per unit; undo only inside the window **and** before any activity in the new year (two distinct codes); execution is per student and idempotent; history stays readable. Lifecycle: withdrawn means inactive, sign-in blocked, hidden from active lists, history kept; re-admission keeps the code (D25). Grade changes: reason required, requester cannot approve, every decision audited. Providers: secrets never returned; a failed test says which check failed without the secret.

### 4.3 New problem codes (proposed)
`year_close_blocked`, `grade_change_pending`, `grade_change_not_allowed`, `promotion_in_progress`, `promotion_conflict`, `promotion_undo_window_over`, `promotion_undo_activity_exists`, `student_already_inactive`, `effective_date_invalid`, `readmit_class_full`, `readmit_year_closed`, `copy_nothing_selected`, `active_year_exists`, `provider_test_failed`. Each gets an `Error_<code>` resource in ar and en.

## 5. States, ar/en, RTL and 360 px

- **States for every page and dialog:** loading (`LoadingState`), empty (`EmptyState` with the next step: "No closed years yet", "No promotion runs", "The archive is empty"), error (outlined `MudAlert` with what to do and a retry), forbidden (the standard "You do not have access" page), offline (the M6 banner pattern), **running** (job panel), **partial failure** (promotion with failed students), **stale** (409 `concurrency_conflict`: reload the row and say so), **window over** (undo).
- **Dates and numbers:** ISO dates left to right inside RTL text, numbers in the Latin digits the app already uses, the effective date and the undo clock shown with the client time zone.
- **RTL:** every table column order follows the document direction; direction icons are mirrored (`lms-icon-flip`); the wizard step indicator reverses; certificate fingerprints, URLs, DNs and filters are `dir="ltr"` blocks.
- **360 px:** every table is a labelled stacked card list; wizards are full-screen dialogs; the promotion preview's outcome and target selects sit under the student name inside the card; primary actions are at the bottom of the viewport in dialogs; no sideways scroll; 44 px targets.
- **Plain words:** Arabic first, then English; no jargon ("preview", "run" become "معاينة", "تشغيل الترقية" in the resources the designer reviews).

## 6. `data-testid` hooks (to agree with QA before the E2E tasks)

| Page | Ids |
|---|---|
| Years | `year-close-{id}`, `year-open-next-{id}`, `year-close-dialog`, `year-close-blocking`, `year-close-confirm-input`, `year-close-confirm`, `year-closing-report-{id}` |
| Grade changes | `grade-request-{entryId}`, `grade-request-reason`, `grade-request-submit`, `gc-queue`, `gc-row-{id}`, `gc-approve-{id}`, `gc-reject-{id}`, `gc-reject-note`, `gc-state-{id}` |
| Promotion | `promo-wizard`, `promo-source-year`, `promo-source-unit`, `promo-target-year`, `promo-preview-table`, `promo-row-{studentId}`, `promo-outcome-{studentId}`, `promo-target-class-{studentId}`, `promo-bulk-set`, `promo-summary-{outcome}`, `promo-blocking`, `promo-continue`, `promo-confirm-input`, `promo-execute`, `promo-progress`, `promo-result`, `promo-undo-banner` (with `data-until`), `promo-undo`, `promo-history`, `promo-run-{id}` |
| Lifecycle | `student-record-menu`, `withdraw-open`, `withdraw-reason`, `withdraw-date`, `withdraw-confirm`, `graduate-open`, `readmit-open`, `readmit-class`, `readmit-confirm`, `archive-list`, `archive-search`, `archive-row-{id}`, `archive-readonly` |
| New year | `newyear-wizard`, `newyear-name`, `newyear-start`, `newyear-end`, `newyear-mode-copy`, `newyear-mode-template`, `newyear-template-{id}`, `newyear-review`, `newyear-create`, `newyear-progress`, `newyear-result` |
| Providers | `idp-kind`, `idp-saml-metadata-url`, `idp-saml-upload`, `idp-saml-cert`, `idp-sp-acs`, `idp-sp-entity`, `idp-ldap-host`, `idp-ldap-transport`, `idp-ldap-bind-dn`, `idp-ldap-bind-password`, `idp-test`, `idp-test-result`, `login-ldap-user`, `login-ldap-password`, `login-ldap-submit`, `login-ldap-error`, `login-provider-{slug}` |
| Year-end home | `nav-yearend-*`, `yearend-checklist`, `yearend-step-{n}` |

## 7. Order, sizes and the team split

| Step | Tasks | Who (proposed) | Waits for |
|---|---|---|---|
| 1 | Contracts for close, grade change, promotion, lifecycle, new year, providers | Lead | M5 and M6 merged |
| 2 | H1 clients, fakes, job panel | FE2 | step 1 (or fakes first) |
| 3 | H2 close year and approvals, H5 lifecycle | FE2 and FE1 in parallel | H1 |
| 4 | H3 preview, then H4 execute and undo | FE2 | H1; BE promotion tasks |
| 5 | H6 new year, H7 providers | FE1 (H6), FE2 (H7), the latter last | H1; BE SAML and LDAP |
| 6 | H8 nav and checklist, H9 release pass | FE1 and FE2, designer review | all |

Total: 2 S, 4 M, 3 L. A small UX task (M7 design spec on the Stitch system: wizard, undo banner, close dialog, archive) precedes H2, H3 and H6 pages, as the M5 spec did (gate: FE dialogs wait for it unless the Lead waives it).

## 8. Risks

- **Promotion at 50,000 students** is the heaviest screen: server paging and filter-based bulk actions are mandatory, so the contract must offer both (section 4.1). A client that downloads all rows is a bug.
- **Undo is time-sensitive**: the window and the "any activity" rule are server facts; the UI must show the clock time, not only a countdown, and must not trust the browser clock.
- **Locks are everywhere** after close: every M3 to M6 page that writes can now answer `year_closed`; H2 and H9 sweep for pages that do not map it.
- **LDAP password handling** is the only place the app types an external password: no logging, no URL, generic error, no autocomplete off hack that breaks the password manager.
- **Dependence on M5 and M6 merge order**: the M5 FE chains are still open (contracts 2 and 3); H2 and H9 touch M5 pages and cannot start before those reach `main`.

## 9. Open questions for the product owner (the Team Lead asks)

These are **decisions only the owner can make**; each has a proposed default so work can start.

| # | Question | Proposed default |
|---|---|---|
| Q1 | Who may run the promotion: Org Admin only, or Manager inside their units too? | Org Admin and Manager (inside their units) |
| Q2 | Must a year be **Closed before** the promotion can run, and can a Closed year be **reopened**? Does closing require every term's grades to be published? | Close first; no reopen in v1 (grade changes by approval instead); closing needs no published-grade rule, but warns |
| Q3 | Who approves a grade change after close: Manager only (FR-GRD-06), or Org Admin too? May a Manager approve a change for a class course they teach? | Manager and Org Admin; never the requester |
| Q4 | After withdrawal or graduation: may the **guardian** keep read access to the child's history? Is the withdrawal reason ever shown to the guardian or student? | Guardian keeps read access to history for the closed years; the reason is staff-only |
| Q5 | Who may withdraw a student: Org Admin and Manager only, or Counselor too? Is a withdrawal reason from a fixed list or free text? | Org Admin, Manager and Counselor; a client-managed list plus "Other" with a note |
| Q6 | Does the promotion suggest an outcome from **grades** (pass or fail rule) or only from the level mapping? | Level mapping only in v1; every row stays editable |
| Q7 | Which blockers does the promotion flag per student (unpaid fees are out of scope; open excuse requests, incomplete grades)? | Open grade-change requests and open excuse requests, as warnings |
| Q8 | If part of the run fails, may the run complete with the rest (partial success), or must it roll back entirely? | Per-student success with a failure list; undo restores the successful ones |
| Q9 | Do we need bulk withdrawal (a whole class at once) in v1? | No; the promotion's "transfer out" and "graduate" cover year-end |
| Q10 | SAML and LDAP scope for v1: SP-initiated SAML only; LDAP search-bind only with LDAPS or StartTLS; **no** group-to-role mapping; no signed-assertion toggle? | Yes to all of those limits |
| Q11 | Should the year-end checklist appear on the home pages, and how many days before the year end? | Yes, 60 days |
| Q12 | The accessibility pass target: WCAG 2.1 AA as in X-10, or also AAA contrast for the attendance and grade colours? | AA, as stated |

## 10. What this proposal asks the Lead to do next

1. Confirm the split (section 7) and the permission names (section 2).
2. Open the M7 contract PRs for the sections 4.1 and 4.3 routes; the fakes follow them.
3. Ask the owner the Q1 to Q12 list (by popup, as the project rule says) or confirm the defaults.
4. Ask the UX designer for the M7 design spec before H2, H3 and H6 page work.
