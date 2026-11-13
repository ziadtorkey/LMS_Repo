# M4b design spec: live sessions and package player

**Plan:** `docs/superpowers/plans/2026-11-16-m4b-live-and-packages.md`, Task 12. This spec gates the dialogs and pages of Tasks 14–17 (and guides 18 and 19).
**PRD:** X-01 (bilingual), X-10 (accessibility), FR-VC-01, FR-STD-01, FR-STD-02.
**Mockups:** `docs/design/mockups/m4b.html` (static HTML in the LMS tokens; open it with `?screen=…&lang=ar|en&theme=light|dark`). Screenshots: `docs/screenshots/ux-m4b/` at 1440 and 360 px, in ar and en, light and dark.

Everything in `docs/DESIGN.md` applies. This file only adds what M4b needs. The new CSS classes listed in §6 go into `lms-theme.css` in a small `lane:ux` PR before Tasks 14–17 start. Frontend developers use those classes and don't write their own.

---

## 1. Shared rules for M4b

- **Server time only.** Every countdown, window and "ended" state comes from `ServerNowUtc` plus the time elapsed since the response arrived (the quiz `AttemptTimer` pattern from #406). The browser clock never decides anything.
- **Dates and times** use `yyyy-MM-dd` and `HH:mm` (24 h), invariant, inside `<span dir="ltr">`, as everywhere else. A time range is written `10:00–11:00`. Durations are a number and a unit: "60 min" / "60 دقيقة".
- **Time zone.** Authoring shows the browser's IANA zone once, in the dialog's time section ("Times are in your time zone: Asia/Riyadh"). Lists don't repeat it.
- **External links** (Zoom, recordings) never show the full URL. They show a **host hint** next to the link (`lms-host-hint`: an LTR caption chip with the host name, for example `zoom.us`) and "Opens in a new tab" for screen readers (visually hidden text, or the visible caption in the join panel).
- **Disabled, not hidden.** Permissions decide whether an action exists. State (outside the join window, package not ready, provider not configured) disables it, with a caption that says why and when it changes.
- **Live regions.** Countdowns use `role="timer"` with `aria-live="off"`, plus a polite status that announces at 15 min, 5 min and 1 min before the start and at the start. Package checking uses `role="status"` (`aria-live="polite"`). The player's save state announces only failures and the recovery after a failure (§5.2), never routine saving. Errors use `role="alert"`.
- **Counted text in Arabic** (every number next to a noun, not only countdowns) uses the shared Arabic plural helper with four keys per noun: `_One` (1), `_Two` (2), `_Few` (3–10), `_Many` (11+, and 0). The en resx has the same four keys: `_One` holds the singular, the other three the plural. The full key list is in §8.
- **Icons:** Rounded only. VideoCall (schedule), VideoCameraFront (live session kind), Event (date), Schedule (scheduled), SyncProblem (waiting for Zoom), PlayCircle (recording), Group (participants), UploadFile (upload), Inventory2 (package), Fullscreen / FullscreenExit, Close (exit), Refresh (reload), Visibility (preview).

---

## 2. Live sessions: authoring (Task 14)

### 2.1 Page `/class-courses/{id}/live-sessions`

Mockup: `screen=live-list`.

1. `ClassCourseTabs` gains **Live sessions** after Plan, Forum and Announcements. The active tab gets `aria-current="page"`.
2. `PageHeader`: title "Live sessions", subtitle "Online classes for this course. Students join from the lesson." One primary action, **Schedule a session** (`Icons.Material.Rounded.VideoCall`), shown with `LiveSessions.Manage`.
3. `ProviderStatusBanner` sits between the header and the list card (outlined `MudAlert`, `Dense`, `mb-4`):
   - `Configured = false`: Severity.Info. "Live sessions aren't set up yet. Ask your platform administrator." No link. **Schedule a session is disabled.**
   - `Healthy = false`: Severity.Warning. "Zoom isn't responding right now. New sessions may fail; existing ones keep working." Schedule stays enabled.
   - Both fine: no banner.
4. The list is in `SectionCard Flush`, with a filter toolbar (`lms-filters`): a two-option `MudToggleGroup` **Upcoming | Past** (Upcoming is the default; past is newest first). There's no search; one course rarely has more than a few dozen sessions.
5. `ServerTable` columns, each `MudTd` with a `DataLabel`:

| Column | Content |
|---|---|
| When | `lms-when`: date on line 1 (`yyyy-MM-dd`, `lms-cell-strong`), time range on line 2 (`lms-muted`), both LTR |
| Session | Title (`Culture.Pick`). For a series, a caption under it: Rounded.Repeat icon (aria-hidden) and "Weekly · 3 of 12" ("of N" has no noun, so it needs no plural form) |
| Lesson | Lesson title, `lms-muted` |
| Status | `LiveSessionStatusChip` (§2.2). For `SyncFailed`, the explanation caption sits **under the chip** (not only in a tooltip, so it shows on touch) |
| Recording | Rounded.PlayCircle and "Recording" when there is one (a link that opens the recording); otherwise a dash |
| Actions | `lms-row-actions`: Edit (Rounded.Edit), Recording link (Rounded.Link), Participants (Rounded.Group; only once the session is Live or Ended), separator, **Cancel session** (Rounded.EventBusy, Color.Error). Each has an `aria-label` and a tooltip with the same text. Ended and Cancelled rows keep only Recording link and Participants |

6. **Empty states** (`EmptyTitle` / `EmptyText`, icon Rounded.VideoCall):
   - Upcoming, for staff: "No upcoming sessions" / "Schedule a session so students can join from the lesson."
   - Upcoming, for students (who never reach this tab without `Manage`, but keep it anyway): "No upcoming sessions" / "Your teacher hasn't scheduled any yet."
   - Past: "No past sessions" / "Sessions appear here after they end."
7. At 360 px the table becomes labelled cards (standard). The status chip stays next to the "Status" label, and the actions go to the bottom row of the card.

### 2.2 `LiveSessionStatusChip`

`Size.Small`, `Variant.Text` (soft tint), always with a text label. It's a shared component (`Components/LiveSessions/LiveSessionStatusChip.razor`) used by the list, the join panel and the Home cards.

| Status | Colour | Icon | en | ar |
|---|---|---|---|---|
| Scheduled | Info | Schedule | Scheduled | مجدولة |
| Live | Success | dot `lms-live-dot` (pulses; static under `prefers-reduced-motion`) | Live now | مباشرة الآن |
| Ended | Default | none | Ended | انتهت |
| Cancelled | Default (and the whole row text is `lms-muted`) | EventBusy | Cancelled | ملغاة |
| SyncFailed | Warning | SyncProblem | Waiting for Zoom | بانتظار Zoom |

The SyncFailed explanation (`Live_SyncFailedHint`) reads: "Zoom couldn't be reached. The LMS will retry automatically; you don't need to do anything." / "تعذّر الاتصال بـ Zoom. سيعيد النظام المحاولة تلقائياً ولا يلزمك فعل شيء." There's no retry button (Decision 16).

### 2.3 `LiveSessionDialog` (schedule / edit)

Mockup: `screen=live-dialog`. `MaxWidth.Medium`, `FullWidth`. The title is "Schedule a live session" or "Edit live session". Sections are `lms-fieldset`s:

1. **Session**
   - `BilingualNameFields` (title ar/en, 1–200 characters). Helper under the pair: "Participants see this title in Zoom."
   - Lesson picker (`MudSelect`, the lessons of the plan, grouped by section with `MudListSubheader`-style disabled items). Required.
2. **Time**: `lms-form-grid` with three cells.
   - Date (`MudDatePicker`, `yyyy-MM-dd`).
   - Start time (`MudTimePicker`, 24 h).
   - Duration: a `MudSelect` of 15, 30, 45, 60, 90, 120, 180, 240, 360 and 480 min (default 60), labelled with `Live_Duration_Minute_*` ("60 دقيقة"). A select avoids typing errors; the 15–480 limit then holds by construction.
   - Caption under the grid: "Times are in your time zone: **Asia/Riyadh**" (the zone id LTR).
3. **Repeat**
   - `MudRadioGroup`, inline: **Once** (default) | **Weekly**.
   - Weekly adds:
     - "Every [1 ▾] week(s)": a select of 1–4. The option texts are whole phrases from `Live_EveryWeeks_*` ("Every week", "Every 2 weeks" / "كل أسبوع", "كل أسبوعين", "كل 3 أسابيع"), not a number plus a fixed word.
     - Day toggles (`lms-day-toggles`): seven small toggle chips in the culture's week order (`FirstDayOfWeek`, as the calendar does), using abbreviated day names. Each is a real toggle button with `aria-pressed`. The start date's day is pre-selected.
     - Ends: `MudRadioGroup` with **On date** [date picker] or **After** [number field] **sessions**. One is required (`recurrence_invalid` maps here).
   - **`RecurrencePreview`** (`lms-recurrence-preview`): a soft panel under the repeat fields.
     - Line 1 (`role="status"`, polite): "12 sessions" / "12 جلسة" (`Live_Unit_Session_*`, §8). Above 52 it turns red: "A series can have at most 52 sessions. Choose an earlier end date or fewer sessions." Schedule is disabled until fixed.
     - Then the first 6 dates as `yyyy-MM-dd HH:mm` (LTR) in a two-column list (one column at 360 px), plus "and 6 more" / "و6 جلسات أخرى" when longer (`Live_MoreSessions_*`).
4. **Module**: the shared `ModuleMetadataForm` (visibility, target, completion rule from `CompletionRules.ForActivity(LiveSession)`: None / Manual / View, with the existing labels).

**Actions** (`lms-dialog-actions`): Cancel (text), then **Schedule** (or **Save**) as filled primary with a spinner while saving.

**Error mapping:** `provider_unavailable` and `feature_disabled` show as an outlined error alert at the **top** of the dialog, in plain words:
- `provider_unavailable` with `Retry-After`: "Zoom is busy. Try again in about 5 minutes." The minutes are rounded up; below a minute it says "in a minute".
- `feature_disabled`: "Live sessions are turned off for this school."

`recurrence_invalid` goes on the Ends field; `module_visibility_range_invalid` and `module_target_invalid` go on their fields.

### 2.4 `SeriesScopeDialog`

Mockup: `screen=live-small`. `MaxWidth.ExtraSmall`. It opens before saving an edit, or cancelling, on a session that belongs to a series.

- Title: "Edit a repeating session" / "Cancel a repeating session".
- A `MudRadioGroup` stacked as two `lms-choice` rows (the whole row is clickable, with the label and a caption):
  - **This session only**, caption "2026-11-24 10:00".
  - **This and following sessions**, caption "2026-11-24 and 8 later sessions" / "2026-11-24 و8 جلسات لاحقة" (`Live_LaterSessions_*`; with none later the row says only the date).
- Actions: Back (text) and Continue (filled primary). When cancelling: **Cancel sessions** (filled Error, in this confirm dialog only).

### 2.5 `RecordingLinkDialog`

`MaxWidth.Small`. Title: "Recording link".

- A URL field, LTR, with a Rounded.Link adornment and the helper "Paste a link that starts with https://."
- While the teacher types, a valid http/https value shows the **host hint** under the field: "Opens on `drive.google.com`". An invalid scheme shows the field error "Use a link that starts with https:// or http://." (`ModuleLinks.IsAllowed`).
- When a link exists, a caption above the field shows its source: "Added by Zoom" or "Added by a teacher" (`RecordingSources`).
- Actions: Cancel (text) and Save (filled, with a spinner).

### 2.6 `LiveParticipantsDialog`

`MaxWidth.Medium`. Title: "Participants · {session title}".

- **Intro** (`lms-muted`, body2): "Only people who signed in to Zoom with their school account are matched to students. Everyone else is listed as a guest."
- **Table:** `MudSimpleTable lms-simple-table lms-stack-table` with the columns Name, Joined, Left, Time in session ("45 min", `Live_Duration_Minute_*`) and Match.
- **Match chip:**
  - **Student** / طالب: Success text, Rounded.VerifiedUser.
  - **Guest** / ضيف: Default text, Rounded.PersonOutline.
  - A guest's display name is shown as typed, in `<bdi>`.
- **Paging:** a "Show more" outlined button under the table when there are more rows.
- **Empty states:**
  - Before the session ends: "Participants appear after the session ends."
  - After it ends: "No one joined this session."

---

## 3. Live sessions: student side (Task 15)

### 3.1 `LiveSessionPanel` in the module viewer

Mockup: `screen=live-join` (one board with every state). The panel is a `SectionCard` without a title (the module title is already the page heading) and has two parts:

1. **Summary row.**
   - A 40 px icon tile (`lms-kind-tile`, primary tint, Rounded.VideoCameraFront, aria-hidden).
   - Session title (`subtitle1`) with the status chip after it.
   - Meta line (`lms-muted`): `2026-11-20 · 10:00–11:00 · 60 min` (LTR parts in spans).
2. **Join zone** (`lms-join-panel`): a soft panel (`background-gray` surface, 8 px radius, 16 px padding) that holds the state below. Its content goes in a `role="region"` labelled "Join".

| State (from server time) | Main line | Action | Caption |
|---|---|---|---|
| **Before the window** | "Starts in 2 h 10 min" (`h6`, `role="timer"`) | **Join the session**, filled primary, **disabled** | "You can join from 15 minutes before the start." (the window from settings, in minutes) |
| **Window open, not started** | "Starts in 8 min" | **Join the session**, enabled | "Opens Zoom in a new tab." |
| **Live** | "Started 12 min ago · ends at 11:00" | **Join the session**, enabled | "Opens Zoom in a new tab." |
| **Joining** (after the click, until the API answers) | as before | button shows a spinner and "Opening Zoom…", disabled | — |
| **Tab blocked** (`window.open` returned null) | outlined Info alert: "Your browser blocked the new tab." | **Open the meeting** outlined button inside the alert (`target="_blank" rel="noopener noreferrer"`) | — |
| **Join failed** | outlined Error alert, the mapped message (`provider_unavailable` → "Zoom is busy. Try again in a minute.") | **Try again** text button | — |
| **Ended, with recording** | "Ended on 2026-11-20 at 11:00." | **Watch the recording**, outlined, Rounded.PlayCircle, `rel="noopener noreferrer nofollow"` | host hint `zoom.us` and "Opens in a new tab." |
| **Ended, no recording** | "Ended on 2026-11-20 at 11:00." | — | "No recording yet. Your teacher may add one later." |
| **Cancelled** | outlined Warning alert: "This session was cancelled." | — | — |
| **Waiting for Zoom** (SyncFailed) | "Starts in 2 h 10 min" | Join, disabled | "The meeting is still being set up. Check back shortly." |
| **Feature off** (`feature_disabled` on join) | outlined Info alert: "Live sessions are turned off for this school." | — | — |

- The countdown text uses the largest two units: "in 2 days 3 h", "in 2 h 10 min", "in 8 min", and "in less than a minute". ar: "بعد يومين و3 ساعات", "بعد ساعتين و10 دقائق", "بعد 8 دقائق" and "بعد أقل من دقيقة". **Arabic needs plural forms:** keep separate resx keys for 1, 2, 3–10 and 11+ per unit (`Live_Unit_Hour_One`, `_Two`, `_Few`, `_Many`), using the shared Arabic plural helper (§7, Q2; every key in §8). Don't put "2 ساعة" on screen.
- When the window opens (or the session starts) while the page is open, the panel moves to the next state without a reload, and the polite status announces it ("You can join now").
- `409 live_session_not_open` reloads the panel and shows the new state; no toast.
- **Hidden or removed module (404).** When the module read answers 404, or `POST …/join` answers 404 because the module was hidden after the page loaded, the whole panel (summary row included) is replaced by the shared **module not-available state**: `EmptyState` with Rounded.SearchOff, "This activity isn't available" / "هذا النشاط غير متاح", and "It may have been hidden or removed. Go back to the course to see what's open to you." / "ربما أُخفي أو حُذف. ارجع إلى المقرر لترى المتاح لك.", plus a **Back to the course** text link. Keys `Module_NotAvailableTitle` and `Module_NotAvailableText`; the package player (§5.3) uses the same state. It never says whether the module was hidden or deleted (one identical 404). Test: `Invisible_session_module_is_not_found` asserts this state and that no title, time or join action of the session is rendered. If the blank tab was already opened for the join, it is closed first (the Task 15 join-failure rule).
- **Staff in the viewer** (`LiveSessions.Manage`): the action is **Start the meeting** (same pattern, start URL), shown from the window opening. A second outlined action, **Participants**, appears once the session is Live or Ended. Staff also see the recording link action, which opens §2.5.

### 3.2 Reminder notification (Tasks 15 and 18)

Event `Live.Reminder`, params `Title` (sent as `TitleAr` / `TitleEn`), `Course` (`CourseAr` / `CourseEn`) and `StartsAtUtc`. It's in-app only (the bell); opening it goes to the module.

| Key | en | ar |
|---|---|---|
| `Notif_Live_Reminder` | Live session {Title} in {Course} starts at {StartsAtUtc}. Join from the lesson. | تبدأ الجلسة المباشرة {Title} في {Course} الساعة {StartsAtUtc}. انضم من الدرس. |
| `NotifEvent_Live_Reminder` | Live session reminder | تذكير بجلسة مباشرة |

- `{StartsAtUtc}` must not appear as a raw UTC string. `NotificationText` (Task 15 modifies it) formats a parameter whose name ends in `Utc` as local time: `HH:mm` when it's today, else `yyyy-MM-dd HH:mm`, invariant digits, wrapped in U+2066 … U+2069 (LTR isolate) so the date and time keep their order inside Arabic text.
- The text doesn't say "in 30 minutes": `Live.ReminderMinutes` is a setting (5–1440), and the bell may be read later.
- `Notification_types_have_both_languages` covers both keys.

### 3.3 Home and course home cards (Task 18)

- **Upcoming live sessions** (student) and **Sessions today** (teacher with `Manage`), each as a `SectionCard` with a title.
- Each card holds a list of at most 5 `lms-item` rows:
  - Date block (`lms-date-block`: day number over the abbreviated month, 44 px, primary tint).
  - Title link to the module, with a caption of course · `HH:mm`.
  - At the end: the status chip, or for a Scheduled session in the next 24 h, the short countdown "in 2 h".
- Empty texts: "No live sessions in the coming days." / "No sessions today."
- On Home the cards follow the one-primary-action rule: rows are links, with no filled buttons.

---

## 4. Packages: upload, check, attach (Task 16)

### 4.1 `PackageUploadDialog`

Mockup: `screen=package-upload` (three dialogs: uploading/checking, rejected, ready). `MaxWidth.Small`, `FullWidth`. The title is "Add a SCORM package" or "Add an H5P package".

**Steps** (`PackageSteps`, `lms-steps`): **1 Upload · 2 Check · 3 Attach**, shown at the top of the dialog content.
- Each step is a circle (24 px) with its number, plus a label. Done steps show Rounded.Check on primary; the current step has a primary ring and bold label; upcoming steps are muted; a failed step shows Rounded.PriorityHigh on Error.
- The current step has `aria-current="step"`, and the list is an `<ol>`.
- At 360 px the labels hide except the current one: "Step 2 of 3 · Check".

**Step 1, upload.**
- A `lms-dropzone`: dashed 1 px `lines-inputs` border, 12 px radius, 24 px padding, centred.
  - Rounded.UploadFile icon (40 px, muted).
  - Line: "Drop a .zip file here or" plus a **Choose a file** outlined button (the real `<input type="file">` is behind the button and keyboard reachable).
  - Caption: "SCORM 1.2 or 2004, one learning object, up to 200 MB." (the size from `GET /api/assets/limits`). For H5P: "An .h5p file, up to 200 MB."
- Client-side rejects (extension, size) show as an error caption under the zone. Nothing is sent.
- While uploading, the zone is replaced by a file row:
  - Rounded.Inventory2 icon, the file name (LTR, ellipsis), the size.
  - A determinate `MudProgressLinear` with the percent as text.
  - A **Cancel upload** icon button with an aria-label.

**Upload failed** (the request didn't succeed; the file row stays, the progress bar turns Error, step 1 shows as failed, and the message is an error caption under the row with `role="alert"`):

| Cause | en | ar | Actions |
|---|---|---|---|
| Network error, timeout, or 5xx | The upload stopped. Check your connection and try again. | توقّف الرفع. تحقّق من اتصالك وحاول مرة أخرى. | **Retry** (outlined primary; sends the same file again from the start, the `File` is kept in memory) and **Choose another file** (text) |
| `package_too_large` (the server limit is lower than the one the client checked) | This file is larger than the {Size} MB limit. | حجم هذا الملف أكبر من الحد المسموح ({Size} ميغابايت). | **Choose another file** only (a retry would fail the same way). `{Size}` comes from a fresh `GET /api/assets/limits` |
| `feature_disabled` or `package_host_not_configured` mid-upload | the blocking alert below replaces the row | — | Close only |
| any other code | `Error_package_invalid` (§4.2) | — | **Choose another file** |

Retry and Choose another file are buttons in the dialog actions (`lms-dialog-actions`), Close stays. Focus moves to **Retry** (or to Choose another file when there is no Retry). Test names to add in Task 16: `Upload_network_failure_offers_retry_with_the_same_file`, `Server_too_large_shows_the_limit_without_retry`, `Feature_disabled_during_upload_shows_the_blocking_alert`.

**Step 2, check.**
- An indeterminate progress bar and the line "Checking the package. This usually takes under a minute." (`role="status"`).
- After the polling cap: "Still checking. This can take a few minutes for large packages." plus a **Check again** outlined button.
- A failed status read doesn't end the check: polling keeps its backoff. After 3 failed reads in a row the caption becomes "Can't reach the server right now. We'll keep trying." / "تعذّر الوصول إلى الخادم الآن. سنواصل المحاولة." (`role="status"`) with the same **Check again** button; it goes back to the normal line on the next good read.
- **Keep the dialog open** (decided on Q1). A caption under the progress line says: "Keep this window open until the check finishes." / "أبقِ هذه النافذة مفتوحة حتى ينتهي الفحص." Closing the dialog stops polling.
- **Close during upload or check asks first.** Close, Escape and the dialog's close icon open the standard `ConfirmDialog` (`MaxWidth.ExtraSmall`); the backdrop click is off for this dialog (`BackdropClick = false`):
  - While checking: title "Close this window?" / "إغلاق هذه النافذة؟", text "Keep this window open until the check finishes. If you close it, you can continue from the lesson's module list." / "أبقِ هذه النافذة مفتوحة حتى ينتهي الفحص. إن أغلقتها فيمكنك المتابعة من قائمة وحدات الدرس.", actions **Keep open** (filled primary, default focus) and **Close anyway** (text).
  - While uploading: the same title, text "Closing stops the upload." / "الإغلاق يوقف الرفع.", actions **Keep open** and **Stop upload** (text, Error). Stop upload cancels the request.
  - In every other step Close closes at once.
- **Pending row in the lesson.** A package still being checked shows in the lesson's module list as a placeholder row, so closing the tab isn't a dead end:
  - The row has the package kind icon, the file name (LTR) and a **Checking…** chip (Info text, small spinner).
  - Its single action, **Continue**, reopens this dialog at step 2, or at step 3 if the package is Ready by then.
  - A Rejected package shows a **Can't be used** chip (Error text), with **See why** (reopens the dialog on the reason) and **Remove** (deletes the upload).
  - A Ready package that was never attached shows a **Ready to attach** chip (Success text), with **Continue** (step 3) and **Remove** (standard delete confirm). This is the screen that frees quota: without it an unattached package would count against `quota_exceeded` with no way to delete it.
  - The row is visible to staff only. Students never see it.
  - **Data needed:** the row has to survive a reload, so the server must be able to list the pending uploads of a class course together with the lesson they were started from. See §7.

**Rejected.**
- Step 2 is shown as failed, with an outlined Error alert titled **This package can't be used** and the plain reason from §4.2 below it.
- Actions: **Choose another file** (outlined primary; back to step 1, after which the rejected upload is deleted) and Close (text).

**Ready (step 3, attach).**
- A summary card (`lms-details` in a bordered box):
  - Title (from the manifest).
  - Type: "SCORM 2004", "SCORM 1.2" or "H5P · {MainLibrary}", with the library name LTR.
  - File name.
- The attach form below, in `lms-fieldset`s:
  - **Module:** lesson picker, and titles ar/en prefilled from the manifest title. The manifest title goes into both the ar and en fields, and the teacher edits them.
  - **Settings:** `ModuleMetadataForm` with the completion rule `None` / `Submit` / `Score`. The helper for Score: "Complete when the package reports a score."
  - **Grading** (D57, contract #549 `AttachPackageModuleRequest.GradingMethod`). It's a `MudRadioGroup` stacked as three `lms-choice` rows, under the legend "When a student opens it again" / "عند فتح الطالب له مرة أخرى". Each row has a label and a one-line caption:

    | Value | Label (en / ar) | Caption (en / ar) |
    |---|---|---|
    | `Highest` (default, pre-selected) | Keep the highest score / احتساب أعلى درجة | Students can try again; their score never goes down. / يمكن للطلاب المحاولة مجددًا ولا تنخفض درجتهم. |
    | `Last` | Use the latest score / احتساب آخر درجة | Each new attempt replaces the score, even if it's lower. / كل محاولة جديدة تحلّ محل الدرجة حتى لو كانت أقل. |
    | `First` | Keep the first score / احتساب الدرجة الأولى | Later attempts are practice and aren't graded. / المحاولات اللاحقة للتدريب ولا تُحتسب. |

    - The group shows only when the completion rule is **Score**. With None or Submit there's no score to keep, so the request sends the default.
    - Keys: `Package_Grading`, `Package_Grading_{Highest|Last|First}` and `Package_Grading_{…}_Help`.
    - **Changing it later (staff):** the package module's edit dialog shows the same group, pre-selected from `PackageUploadDto.GradingMethod`. Saving calls `PUT /api/packages/{id}/grading-method`; changing is allowed at any time. When the value changes, an outlined Info alert above the actions says "Students' graded scores will be recalculated with the new rule." / "ستُعاد حساب درجات الطلاب وفق القاعدة الجديدة." There's no confirm, because the server recomputes and nothing is lost.
- Actions (`lms-dialog-actions`):
  - Inline-start: **Delete package** (text, Error; opens the standard confirm).
  - Inline-end: Cancel (text) and **Attach to lesson** (filled primary, with a spinner).
  - Attach is disabled until the status is Ready.

**Blocking states** replace the drop zone with an outlined alert and keep only Close:
- `feature_disabled`: Info, "Packages are turned off for this school."
- `package_host_not_configured`: Info, "Packages aren't set up on this platform yet. Ask your platform administrator."

### 4.2 Reject reasons (plain words)

These are the `Error_package_reason_<code>` resx values. Each says what happened and, where possible, what to do. They go in the Task 2 contract labels. If the contract already has other wording, this table is the designer's proposal.

| Code | en | ar |
|---|---|---|
| unsafe_path | Some files in the package point outside it. Export the package again. | بعض الملفات في الحزمة تشير إلى خارجها. صدّر الحزمة من جديد. |
| too_many_entries | The package has too many files. | تحتوي الحزمة على عدد كبير جداً من الملفات. |
| too_large | The package is too large once unpacked. | حجم الحزمة بعد فكّ الضغط أكبر من المسموح. |
| compression_ratio | The package is compressed in an unsafe way. Export it again. | طريقة ضغط الحزمة غير آمنة. صدّرها من جديد. |
| symlink | The package contains links to other files, which aren't allowed. | تحتوي الحزمة على روابط لملفات أخرى، وهذا غير مسموح. |
| duplicate_name | Two files in the package have the same name. | يوجد ملفان بالاسم نفسه في الحزمة. |
| bad_name_encoding | Some file names can't be read. Rename them with plain letters and digits. | تعذّرت قراءة بعض أسماء الملفات. أعد تسميتها بحروف وأرقام بسيطة. |
| executable | The package contains programs or old plug-in files (such as Flash), which aren't allowed. | تحتوي الحزمة على برامج أو ملفات إضافات قديمة (مثل Flash)، وهذا غير مسموح. |
| encrypted | The package is password-protected. Upload it without a password. | الحزمة محمية بكلمة مرور. ارفعها دون كلمة مرور. |
| nested_archive | The package has another .zip file inside it. Upload the inner package instead. | تحتوي الحزمة على ملف مضغوط آخر بداخلها. ارفع الحزمة الداخلية بدلاً منها. |
| manifest_invalid | This isn't a valid SCORM or H5P package: its description file is missing or broken. | هذه ليست حزمة SCORM أو H5P صالحة: ملف الوصف مفقود أو تالف. |
| xml_unsafe | The package's description file uses features that aren't allowed. Export it again. | يستخدم ملف وصف الحزمة خصائص غير مسموحة. صدّرها من جديد. |
| multi_sco | The package has more than one learning object. Export each one as its own package. | تحتوي الحزمة على أكثر من وحدة تعلّم. صدّر كل وحدة في حزمة مستقلة. |
| needs_nested_frames | The package shows its content inside frames, which can't run safely here. Export it with a single-page player if your tool offers one. | تعرض الحزمة محتواها داخل إطارات، ولا يمكن تشغيلها بأمان هنا. صدّرها بمشغّل من صفحة واحدة إن كانت أداتك توفّر ذلك. |
| needs_relaxed_policy | The package needs browser features that aren't allowed for security reasons. | تحتاج الحزمة إلى خصائص في المتصفح غير مسموحة لأسباب أمنية. |
| h5p_invalid | This H5P file is incomplete or uses a content type we don't support. | ملف H5P غير مكتمل أو يستخدم نوع محتوى غير مدعوم. |
| validation_timeout | Checking the package took too long. Try again, or upload a smaller package. | استغرق فحص الحزمة وقتاً طويلاً. حاول مرة أخرى أو ارفع حزمة أصغر. |
| quota_exceeded | Your school has used all its storage for packages. Delete packages you no longer need, or ask your administrator. | استُهلكت مساحة التخزين المخصصة للحزم في مدرستك. احذف حزماً لم تعد تحتاجها أو تواصل مع المسؤول. |
| scan_failed | The package didn't pass the security check. | لم تجتز الحزمة الفحص الأمني. |

An unknown code falls back to `Error_package_invalid`: "This package can't be used. Check that it's a SCORM or H5P export." / "لا يمكن استخدام هذه الحزمة. تأكّد أنها مُصدَّرة بصيغة SCORM أو H5P."

### 4.3 Package statuses (wherever a status is shown)

| Status | Chip | en | ar |
|---|---|---|---|
| Uploaded | Default text | Uploaded | تم الرفع |
| Validating | Info text, small spinner | Checking | جارٍ الفحص |
| Ready | Success text | Ready | جاهزة |
| Rejected | Error text | Can't be used | غير صالحة |

---

## 5. Package player (Task 17)

Mockups: `screen=player` (running, plus the preview banner) and `screen=player-states` (start card, timed out, save failed, finished). The mockup frame is a drawn placeholder, not an `<iframe>`; the frame attributes are in §5.2.

### 5.1 Before launch: start card

The start card is a `SectionCard`. Nothing loads in a frame until the learner presses Start.

- **Summary row:** a `lms-kind-tile` with Rounded.Inventory2, the module title (`subtitle1`) and a status chip.
  - Statuses: Not started (Default) / In progress (Info) / Completed (Success).
  - With a score, it adds "Score 85 / 100" (or "85 %", as the server reports it).
- **Third-party note** (Q-G8), `lms-muted` caption with Rounded.Info (aria-hidden): "This activity comes from another provider. Some parts may not work with a keyboard or screen reader, and buttons that print, download or open a new window don't work here." / "هذا النشاط من مزوّد خارجي. قد لا تعمل بعض أجزائه بلوحة المفاتيح أو قارئ الشاشة، ولا تعمل هنا الأزرار التي تطبع أو تنزّل ملفًا أو تفتح نافذة جديدة."
  - **Why the second half:** the frame's sandbox is exactly `allow-scripts` (ADR 0005 §2–3). So inside a package, pop-ups, new windows, form posts, downloads, `alert()`/`confirm()` and `print()` are all silently blocked, and the learner should know it isn't their fault.
- **Phone hint** (below 600 px only): "Works best on a larger screen."
- **Actions, students:**
  - **Start** (filled primary, Rounded.PlayArrow), or **Resume** when an attempt is in progress.
  - In both cases the action is `POST …/launch`, which continues the open attempt (`Resumed = true`) or opens a new one. Only the label differs.
  - **No "Start again" in v1.** The contract has no call that abandons an open attempt, so the button is not shown. If a later contract adds one, it comes with a confirm: "Start again? Your saved progress in this activity will be replaced." / "البدء من جديد؟ سيُستبدل تقدّمك المحفوظ في هذا النشاط." The confirm button is in Warning colour.
  - On a **Completed** module the re-open action depends on the module's grading method (D57). It always launches, which opens a new attempt.

    | Grading method | Button (outlined) | Caption under it (`lms-muted`) | Confirm before launching |
    |---|---|---|---|
    | `Highest` | Try again / حاول مرة أخرى (Rounded.Replay) | Your score won't go down. / لن تنخفض درجتك. | none |
    | `Last` | Try again / حاول مرة أخرى (Rounded.Replay) | Your new score replaces this one. / ستحلّ درجتك الجديدة محلّ هذه الدرجة. | yes: "Try again? Your new score will replace your current score of 85 / 100, even if it's lower." / "المحاولة مجددًا؟ ستحلّ درجتك الجديدة محلّ درجتك الحالية 85 / 100 حتى لو كانت أقل." Buttons: Cancel (text) and **Try again** (filled, Warning) |
    | `First` | Review (not graded) / مراجعة (غير محتسبة) (Rounded.Visibility) | Your first score stays; this attempt is practice. / تبقى درجتك الأولى، وهذه المحاولة للتدريب. | none |

    - The completed result (chip and score) stays above the button in every case. A module with no score (rule None or Submit) uses the `Highest` wording.
    - **While a practice attempt (`First`) is open**, the toolbar shows an Info chip "Practice" / "تدريب" next to the title, so the learner knows it doesn't count. The result card at the end says "Practice finished. Your graded score is still 85 / 100."
    - **Data:** the viewer reads `GET /api/packages/{id}/my-state` (`PackageStudentStateDto`: `GradingMethod`, `LatestAttempt`, `HasCompletedAttempt`, `GradedScore`; #549).
      - The re-open button appears when `HasCompletedAttempt` is true and no attempt is in progress. Its wording comes from `GradingMethod`.
      - The result card shows **`GradedScore`**, the score that counts, as "Score 85 / 100". When the latest attempt differs (Highest or First after a lower or practice attempt), a second `lms-muted` line adds "Latest attempt: 70 / 100" / "آخر محاولة: 70 / 100".
      - The `Last` confirm quotes `GradedScore`.
- **Actions, staff:** **Preview** only (outlined, Rounded.Visibility). Launch answers 404 for staff (contract and ADR 0005 §6), so staff never see Start, Resume or Open again. Students never see Preview.

### 5.2 Running: `lms-player`

```
┌ lms-player (1px border, 12px radius, fills the content column) ───────────────┐
│ [Preview: nothing is recorded]  ← preview only, Info strip, full width          │
│ Title of the module · Saved ✓            [⛶ Full screen]  [✕ Exit]  ← toolbar  │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│                  <iframe>  min-height 70vh, white background                    │
│                                                                                 │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Toolbar** (`PackagePlayerToolbar`, `lms-player-bar`): it's outside the frame, in the app DOM, 48 px high, surface background, bottom border.
- **Start side:** the module title (body2, `lms-cell-strong`, single line, ellipsis) and `PackageSaveState`.
- **End side:**
  - **Full screen** icon button (Rounded.Fullscreen / FullscreenExit). The aria-label is "Full screen" / "ملء الشاشة", or "Exit full screen" / "إنهاء ملء الشاشة".
  - **Exit**, a text button with Rounded.Close. It ends the player view (flush first) and returns to the start card.
  - Both stay reachable in full screen, because the container (toolbar included) is what goes full screen.
  - **Full screen is offered only when it works.** Show the button only when `document.fullscreenEnabled` is true. iPhone Safari, for example, can't make a container full screen, and a button that does nothing is worse than none.
  - **Leaving full screen:** Esc and the button both leave full screen. The button's label and icon follow the `fullscreenchange` event, not a local flag, so they stay right when the browser exits full screen by itself. Focus goes back to the Full screen button.
  - **Exit while in full screen** leaves full screen first, then flushes and returns to the start card.

**Frame attributes** (Task 17, Decisions 12 and 14; the mockup draws a placeholder and doesn't relax any of them):

```html
<iframe sandbox="allow-scripts" title="{module title}" referrerpolicy="no-referrer" allow="" src="{LaunchUrl}">
```

- `sandbox` is exactly `allow-scripts`: no `allow-forms`, no `allow-same-origin`, no `allow-top-navigation*`, no `allow-popups*`, no `allow-modals`.
- `title` is the module title in the current language (`Culture.Pick`), so screen readers announce the frame by name.
- `allow=""` grants no feature permissions. Full screen is the container's (the toolbar button), never the frame's.

**Save state** (`PackageSaveState`). It's quiet, with no colour except for failures:

| State | Look (in the toolbar) | en | ar |
|---|---|---|---|
| Idle | nothing | — | — |
| Saving | small spinner + muted text | Saving… | جارٍ الحفظ… |
| Saved | Rounded.CloudDone, muted | Saved | تم الحفظ |
| Failed (retrying) | Rounded.CloudOff, Error text | Not saved | لم يُحفظ |
| Refused (`package_commit_rejected`, attempt still open) | Rounded.CloudOff, Error text | Not saved | لم يُحفظ |

- **The failure message goes under the toolbar**, not in it, at every width: a full-width strip `lms-player-msg` (Error tint, body2, `role="alert"`) between the toolbar and the frame.
  - Failed: "Couldn't save your progress. Retrying…" / "تعذّر حفظ تقدّمك. جارٍ إعادة المحاولة…" with a **Retry** text button at the end of the strip.
  - Refused: "Some of your progress couldn't be saved. Your earlier progress is kept." / "تعذّر حفظ جزء من تقدّمك. تقدّمك السابق محفوظ." No Retry (the same commit would be refused again); later commits keep going.
  - The strip goes away on the next successful commit.
- **Announcements:** the toolbar text is not a live region. Only the strip announces (`role="alert"`, once per failure episode), and after a failure the first successful save announces "Saved" once through a visually hidden polite status. Routine Saving / Saved is never announced.
- **Retry** sends the pending commit now, skipping the rest of the backoff wait. It keeps the pending commit's `seq`: `seq` is never reset and never reused for other data. If more changes arrived while it was failing, they are coalesced into one commit under the next `seq`. `seq` continues per attempt across re-launch and Reload (same attempt), so a new bridge starts after the last `seq` of the old one. Automatic retries wait 2, 5 and 10 s, then every 30 s. Test to add: `Retry_resends_the_pending_commit_without_resetting_seq` (next to `Save_failure_shows_state_and_retries`).

- The frame background is white in both themes. Packages are authored for a light page, and a dark frame edge looks broken. The container border keeps the frame visually separate in dark mode.
- **Phones (below 600 px):** the player is full width and the frame is `min-height: 70vh`. The toolbar is one row that never wraps (`flex-wrap: nowrap`): the save state (icon and short text) at the start, Full screen and Exit at the end; the title is hidden. Longer messages live in the `lms-player-msg` strip below it.
- **Preview banner** (staff preview): an Info-tint strip above the toolbar, inside the container, with Rounded.Visibility: "Preview: nothing is recorded." / "معاينة: لا يُسجَّل أي شيء." In preview the save state is hidden.

### 5.3 Token lifetime: silent re-launch and the expired overlay

The frame is on the content host (another site), so the app can't see what the content host answers inside it: an expired token gives a 404 page in the frame that the app never learns about. **The app decides from time it already knows, never from the frame.**

- **Clock.** The launch answer carries `TokenExpiresAtUtc` and `ServerNowUtc`. Lifetime = `TokenExpiresAtUtc − ServerNowUtc`. The time left is computed like the quiz `AttemptTimer` (#406): server time at the response plus the time elapsed since it arrived; the browser clock is never compared with `TokenExpiresAtUtc`. Background tabs throttle timers and a sleeping laptop stops them, so the player re-checks at once on `visibilitychange` (to visible), `pageshow` and `focus`, as well as on its timer.
- **Silent re-launch at 80 % of the lifetime** (Task 17, `Relaunch_swaps_the_frame_only_after_a_flush`):
  1. Flush: drain the port, send the pending commit, wait for the answer. The save state shows "Saving…" meanwhile, as for any save.
  2. `POST /api/packages/{id}/launch` again (same attempt, fresh token and `TokenExpiresAtUtc`).
  3. Load the new frame hidden behind the old one. When it fires `load`, swap them and drop the old frame and its bridge. No overlay, no spinner over the content, nothing announced. The package returns to its last saved point (its bookmark); that is expected.
  4. If focus was inside the old frame, it moves to the frame container (`tabindex="-1"`), so it isn't lost on `<body>`.
  5. A network error or 5xx retries quietly (after 30 s, then every 60 s) while the old token is still valid. Nothing is shown unless the token then runs out.
  6. A final answer replaces the player: 404 → the module not-available state (§3.1); `feature_disabled` / `package_host_not_configured` → their alerts (§5.4).
- **Expired overlay.** It appears when the time left reaches 0 without a successful re-launch (the re-launch kept failing, or the tab slept past the expiry and wakes up late). It's the `lms-player-overlay` over the frame area; the toolbar stays usable.
  - Markup: `role="alertdialog"`, `aria-labelledby` its heading "This activity timed out" / "انتهت مهلة النشاط", `aria-describedby` its text "Reload to continue where you left off." / "أعد التحميل لتكمل من حيث توقفت." No `aria-modal`, because Exit and Full screen in the toolbar stay available.
  - The frame container gets `inert` while the overlay is shown, so Tab can't go behind the overlay into the frame.
  - Focus moves to **Reload** (filled primary, Rounded.Refresh). The tab order is Reload, then the toolbar (Full screen, Exit).
  - Reload calls launch for the same attempt (after a flush, if the port still answers), then removes the overlay and `inert` and puts focus on the frame container. While it runs, the button shows a spinner. If it fails, a caption under the button (`role="alert"`) says "Couldn't reload. Check your connection and try again." / "تعذّرت إعادة التحميل. تحقّق من اتصالك وحاول مرة أخرى." and Reload stays.
  - Tests to add in Task 17: `Expired_overlay_comes_from_token_expiry_not_from_the_frame`, `Expired_overlay_makes_the_frame_inert_and_focuses_reload`, `Relaunch_failure_before_expiry_shows_nothing`, `Waking_after_expiry_shows_the_overlay_at_once`.

### 5.4 Other states over or instead of the frame

| State | Presentation | Text (en / ar) | Action |
|---|---|---|---|
| Launching | `LoadingState Lines="3"` inside the container | — | — |
| Finished | the frame is replaced by a result card in the container | Completed / مكتمل, Success chip, "Score 85 / 100" | the re-open button for the module's grading method (§5.1, D57) |
| Attempt already closed (`409 package_commit_rejected`, and a fresh read says the attempt is no longer in progress, for example it was finished in another tab) | the frame is replaced by the result card, with an outlined Info alert on top | This attempt has already ended, maybe in another tab. Your result is below. / انتهت هذه المحاولة، ربما في تبويب آخر. نتيجتك أدناه. | as Finished |
| Commit refused, attempt still open (`409 package_commit_rejected` for size or rate) | the frame keeps running; save state "Not saved" and the Refused strip (§5.2) | §5.2 | — |
| Module hidden during the attempt (404 on commit or re-launch) | the module not-available state (§3.1) replaces the player | §3.1 | Back to the course |
| Not ready (`409 package_not_ready`) | outlined Info alert instead of the start card | This activity isn't ready yet. Try again later. / هذا النشاط غير جاهز بعد. حاول لاحقاً. | — |
| Host not configured | outlined Info alert | Packages aren't set up on this platform yet. / لم تُفعَّل الحزم على هذه المنصة بعد. | — |
| Feature off (`feature_disabled`) | outlined Info alert | Packages are turned off for this school. / الحزم معطّلة في هذه المدرسة. | — |
| Hidden module at launch (404) | the module not-available state (§3.1) | §3.1 | Back to the course |
| Launch failed: network error or 5xx | outlined Error alert in the start card; the card stays | Couldn't open the activity. Check your connection and try again. / تعذّر فتح النشاط. تحقّق من اتصالك وحاول مرة أخرى. | **Try again** text button; the Start / Resume button stays |

`package_commit_rejected` is one code for two causes (closed attempt, or size / rate), so the player tells them apart by re-reading the attempt (`PackageAttemptDto` status), not by the code. Test to add: `Commit_rejected_on_a_closed_attempt_shows_the_result`.

**Focus**
- On Start, focus moves to the frame container (`tabindex="-1"`), so the next Tab goes into the frame. Exit and Full screen sit before the frame in the tab order, so keyboard users can always reach them by Shift+Tab from the frame's first control. The third-party note documents that we can't guarantee more.
- When the frame is replaced by the **Finished** card (or the "already ended" card), the frame is removed and focus moves to the card's heading (the module title, `tabindex="-1"`); the status and score are read next. Focus doesn't go to **Open again**, so a stray Enter doesn't start the activity again.
- When any other state replaces the player (not available, alerts), focus moves to that state's heading or alert.

### 5.5 Staff preview (ADR 0005 §6)

- **Entry:** **Preview** on the staff start card calls `POST …/preview`. It shares the 409 `package_not_ready` and host-not-configured states (§5.4) with launch.
- **Running:**
  - The player is the same as §5.2, with the **Preview strip** above the toolbar ("Preview: nothing is recorded.").
  - **No save state**, because the server answers preview commits 204 and discards them.
  - The token clock, silent re-launch and expired overlay (§5.3) work the same way. Re-launch calls `…/preview` again, not `…/launch`.
- **When the content finishes:** the frame is replaced by a preview card instead of the result card. It has an Info chip "Preview", the text "Preview ended. Nothing was recorded." / "انتهت المعاينة. لم يُسجَّل أي شيء.", and the actions **Preview again** (outlined) and **Close preview** (text). No score is shown: a preview has no attempt to judge.
- **Exit** returns to the staff start card. Focus follows the §5.4 rules.
- **Tests to add in Task 17:**
  - `Staff_see_preview_and_never_start_or_resume`
  - `Preview_finish_shows_the_preview_card_without_a_score`
  - `Preview_relaunch_calls_preview_not_launch`

---

## 6. New patterns and CSS classes (designer adds them to `lms-theme.css`)

| Class / component | Use |
|---|---|
| `LiveSessionStatusChip` | §2.2 |
| `lms-live-dot` | 8 px success dot in the Live chip; a 2 s pulse, none under `prefers-reduced-motion` |
| `lms-when` | Two-line date/time cell, LTR |
| `lms-host-hint` | LTR caption chip with an external host name (`background-gray`, 4 px radius, caption size) |
| `lms-recurrence-preview` | Soft panel with the session count and the date list (2 columns ≥ 600 px) |
| `lms-day-toggles` | A row of seven toggle chips (`aria-pressed`) |
| `lms-choice` | A clickable radio row with a label and a caption (series scope) |
| `lms-kind-tile` | 40 px rounded tile with a primary-tint icon (live session, package) |
| `lms-join-panel` | Soft panel holding the join state, its action and its caption |
| `lms-date-block` | 44 px day/month block for Home rows |
| `lms-steps` | Horizontal numbered steps (`<ol>`), compact at 360 px |
| `lms-dropzone` | Dashed upload area |
| `lms-player`, `lms-player-bar`, `lms-player-frame`, `lms-player-overlay`, `lms-player-msg`, `lms-preview-strip` | §5.2–5.4 |

The reference CSS for each is in the mockup (`docs/design/mockups/m4b.html`, the `/* lms-theme additions */` block). It's written with logical properties and palette variables only, so it can be copied into `lms-theme.css` as is.

## 7. Decisions on the open questions (Team Lead, 2026-11-13)

- **Q1. Reopening a package (decided).**
  - The upload dialog stays open until the check ends, with the caption in §4.1.
  - A pending package shows as a "Checking…" row in the lesson's module list (§4.1).
  - The full list of a course's packages is a follow-up issue.
  - **Contract need for the pending row:** `POST /api/class-courses/{id}/packages` should accept an optional `LessonKey`, and a read such as `GET /api/class-courses/{id}/packages?status=pending` should return the uploads that aren't attached yet with that key. The Lead decides whether this joins the Task 2 contract.
- **Purge of unattached packages (request to the Lead for BE3, Task 8b).** A package that is `Ready` but never attached, or `Uploaded` / `Rejected` and abandoned, should be purged after N days (proposal: a setting `Packages.UnattachedRetentionDays`, default 7) by the existing `PackagePurgeJob`, so abandoned uploads don't fill the client's quota. The pending row above shows a caption on such rows: "Removed automatically after 7 days if not attached." / "تُحذف تلقائياً بعد 7 أيام إن لم تُرفق." (`Package_PendingRetention`, the number and noun from `Live_Unit_Day_*`, §8).
- **Q2. Arabic plurals (decided).** FE1 adds a small Arabic plural helper as a shared client utility (not only for live sessions), with unit tests for 0, 1, 2, 3–10 and 11+. Countdowns (§3.1) and any later counted text use it.

- **Q3. Opening a completed package again (decided, D57, owner).** The teacher picks the grading method per module: **Highest** (default), **Last** or **First** (later attempts are practice). The attach form choice is in §4.1, and the re-open wording and the `Last` confirm are in §5.1. Contract: #549 adds `PackageGradingMethods` and `AttachPackageModuleRequest.GradingMethod`.
  - **Contract (#549 at `add777ce`):**
    - The viewer reads `GET /api/packages/{id}/my-state` (`PackageStudentStateDto`: `GradingMethod`, `LatestAttempt`, `HasCompletedAttempt`, `GradedScore`).
    - Staff change the method with `PUT /api/packages/{id}/grading-method` at any time, and the server recomputes the graded score. The UI therefore needs no "locked" state; the edit dialog only shows the recalculation note (§4.1).
    - `PackageUploadDto.GradingMethod` pre-fills the edit form.

## 8. Counted text: plural keys

Every key below exists in both resx files with the four suffixes `_One`, `_Two`, `_Few` (3–10) and `_Many` (11+, and 0). In en, `_One` is the singular and the other three hold the same plural. `{0}` is the number (invariant digits). The shared helper picks the suffix; pages never build "number + noun" themselves.

| Key | Used in | en (One / other) | ar One | ar Two | ar Few (3–10) | ar Many (11+) |
|---|---|---|---|---|---|---|
| `Live_Unit_Day_*` | countdown after "بعد", `Package_PendingRetention` | 1 day / {0} days | يوم | يومين | {0} أيام | {0} يوماً |
| `Live_Unit_Hour_*` | countdown | 1 h / {0} h | ساعة | ساعتين | {0} ساعات | {0} ساعة |
| `Live_Unit_Minute_*` | countdown | 1 min / {0} min | دقيقة | دقيقتين | {0} دقائق | {0} دقيقة |
| `Live_Duration_Minute_*` | duration select, meta line, participants "Time in session" (standalone) | 1 min / {0} min | دقيقة واحدة | دقيقتان | {0} دقائق | {0} دقيقة |
| `Live_Unit_Session_*` | recurrence preview line 1 | 1 session / {0} sessions | جلسة واحدة | جلستان | {0} جلسات | {0} جلسة |
| `Live_MoreSessions_*` | recurrence preview "and N more" | and 1 more / and {0} more | وجلسة أخرى | وجلستان أخريان | و{0} جلسات أخرى | و{0} جلسة أخرى |
| `Live_LaterSessions_*` | series scope caption | and 1 later session / and {0} later sessions | وجلسة لاحقة | وجلستان لاحقتان | و{0} جلسات لاحقة | و{0} جلسة لاحقة |
| `Live_EveryWeeks_*` | repeat interval select (1–4) | Every week / Every {0} weeks | كل أسبوع | كل أسبوعين | كل {0} أسابيع | كل {0} أسبوعاً |

Notes:
- The countdown units always follow "بعد", and a second unit is joined with "و" in the same case, so their Two forms are genitive and the One forms carry no "واحد": "بعد يومين وساعتين", "بعد يوم و3 ساعات", "بعد ساعتين و10 دقائق". That's why the countdown (`Live_Unit_*`) and standalone durations (`Live_Duration_Minute_*`) are separate keys.
- Durations of 11 and more use `_Many`: "60 دقيقة", "120 دقيقة" (the common modern form).
- "3 of 12" (series position) has no noun and needs no plural key.
- The helper's unit tests (Q2) cover 0, 1, 2, 3, 10, 11 and 100.
