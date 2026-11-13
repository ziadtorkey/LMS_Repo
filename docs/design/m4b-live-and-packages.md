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
- **Live regions.** Countdowns use `role="timer"` with `aria-live="off"`, plus a polite status that announces at 15 min, 5 min and 1 min before the start and at the start. Save state and package checking use `role="status"` (`aria-live="polite"`). Errors use `role="alert"`.
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
| Session | Title (`Culture.Pick`). For a series, a caption under it: Rounded.Repeat icon (aria-hidden) and "Weekly · 3 of 12" |
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
   - Duration: a `MudSelect` of 15, 30, 45, 60, 90, 120, 180, 240, 360 and 480 min (default 60). A select avoids typing errors; the 15–480 limit then holds by construction.
   - Caption under the grid: "Times are in your time zone: **Asia/Riyadh**" (the zone id LTR).
3. **Repeat**
   - `MudRadioGroup`, inline: **Once** (default) | **Weekly**.
   - Weekly adds:
     - "Every [1 ▾] week(s)": a select of 1–4.
     - Day toggles (`lms-day-toggles`): seven small toggle chips in the culture's week order (`FirstDayOfWeek`, as the calendar does), using abbreviated day names. Each is a real toggle button with `aria-pressed`. The start date's day is pre-selected.
     - Ends: `MudRadioGroup` with **On date** [date picker] or **After** [number field] **sessions**. One is required (`recurrence_invalid` maps here).
   - **`RecurrencePreview`** (`lms-recurrence-preview`): a soft panel under the repeat fields.
     - Line 1 (`role="status"`, polite): "12 sessions" / "12 جلسة". Above 52 it turns red: "A series can have at most 52 sessions. Choose an earlier end date or fewer sessions." Schedule is disabled until fixed.
     - Then the first 6 dates as `yyyy-MM-dd HH:mm` (LTR) in a two-column list (one column at 360 px), plus "and 6 more" when longer.
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
  - **This and following sessions**, caption "2026-11-24 and 8 later sessions".
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
- **Table:** `MudSimpleTable lms-simple-table lms-stack-table` with the columns Name, Joined, Left, Time in session ("45 min") and Match.
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

- The countdown text uses the largest two units: "in 2 days 3 h", "in 2 h 10 min", "in 8 min", and "in less than a minute". ar: "بعد يومين و3 ساعات", "بعد ساعتين و10 دقائق", "بعد 8 دقائق" and "بعد أقل من دقيقة". **Arabic needs plural forms:** keep separate resx keys for 1, 2, 3–10 and 11+ per unit (`Live_Unit_Hour_One`, `_Two`, `_Few`, `_Many`), with a small helper in `Components/LiveSessions/CountdownText.cs`. Don't put "2 ساعة" on screen.
- When the window opens (or the session starts) while the page is open, the panel moves to the next state without a reload, and the polite status announces it ("You can join now").
- `409 live_session_not_open` reloads the panel and shows the new state; no toast.
- **Staff in the viewer** (`LiveSessions.Manage`): the action is **Start the meeting** (same pattern, start URL), shown from the window opening. A second outlined action, **Participants**, appears once the session is Live or Ended. Staff also see the recording link action, which opens §2.5.

### 3.2 Home and course home cards (Task 18)

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

**Step 2, check.**
- An indeterminate progress bar and the line "Checking the package. This usually takes under a minute." (`role="status"`).
- After the polling cap: "Still checking. This can take a few minutes for large packages." plus a **Check again** outlined button.
- Closing the dialog stops polling. See open question Q1.

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

Mockups: `screen=player` (running, plus the preview banner) and `screen=player-states` (start card, timed out, save failed, finished).

### 5.1 Before launch: start card

The start card is a `SectionCard`. Nothing loads in a frame until the learner presses Start.

- **Summary row:** a `lms-kind-tile` with Rounded.Inventory2, the module title (`subtitle1`) and a status chip.
  - Statuses: Not started (Default) / In progress (Info) / Completed (Success).
  - With a score, it adds "Score 85 / 100" (or "85 %", as the server reports it).
- **Third-party note** (Q-G8), `lms-muted` caption with Rounded.Info (aria-hidden): "This activity comes from another provider. Some parts may not work with a keyboard or screen reader."
- **Phone hint** (below 600 px only): "Works best on a larger screen."
- **Actions:**
  - **Start** (filled primary, Rounded.PlayArrow), or **Resume** when the server reports an attempt in progress.
  - **Start again** (text), only where the contract allows a new attempt.
  - **Preview** (outlined, Rounded.Visibility), staff with `Packages.View` only. Students never see it.

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

**Save state** (`PackageSaveState`, `role="status"`). It's quiet, with no colour except for failures:

| State | Look | en | ar |
|---|---|---|---|
| Idle | nothing | — | — |
| Saving | small spinner + muted text | Saving… | جارٍ الحفظ… |
| Saved | Rounded.CloudDone, muted | Saved | تم الحفظ |
| Failed | Rounded.CloudOff, Error text, **Retry** text button | Couldn't save. Retrying… | تعذّر الحفظ. جارٍ إعادة المحاولة… |

- The frame background is white in both themes. Packages are authored for a light page, and a dark frame edge looks broken. The container border keeps the frame visually separate in dark mode.
- **Phones:** the player is full width, the toolbar keeps the icon buttons and hides the title, and the frame is `min-height: 70vh`.
- **Preview banner** (staff preview): an Info-tint strip above the toolbar, inside the container, with Rounded.Visibility: "Preview: nothing is recorded." / "معاينة: لا يُسجَّل أي شيء." In preview the save state is hidden.

### 5.3 States over or instead of the frame

| State | Presentation | Text (en / ar) | Action |
|---|---|---|---|
| Launching | `LoadingState Lines="3"` inside the container | — | — |
| Token expired | `lms-player-overlay` over the frame (surface at 94 %, centred, `role="alert"`) | This activity timed out. Reload to continue where you left off. / انتهت مهلة النشاط. أعد التحميل لتكمل من حيث توقفت. | **Reload** filled primary (same attempt) |
| Finished | the frame is replaced by a result card in the container | Completed / مكتمل, Success chip, "Score 85 / 100" | **Open again** outlined; **Start again** text where allowed |
| Not ready (`409 package_not_ready`) | outlined Info alert instead of the start card | This activity isn't ready yet. Try again later. / هذا النشاط غير جاهز بعد. حاول لاحقاً. | — |
| Host not configured | outlined Info alert | Packages aren't set up on this platform yet. / لم تُفعَّل الحزم على هذه المنصة بعد. | — |
| Hidden module (404) | the standard not-found state | — | — |

Focus: on Start, focus moves to the frame container (`tabindex="-1"`), so the next Tab goes into the frame. Exit and Full screen sit before the frame in the tab order, so keyboard users can always reach them by Shift+Tab from the frame's first control. The third-party note documents that we can't guarantee more.

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
| `lms-player`, `lms-player-bar`, `lms-player-frame`, `lms-player-overlay`, `lms-preview-strip` | §5.2–5.3 |

The reference CSS for each is in the mockup (`docs/design/mockups/m4b.html`, the `/* lms-theme additions */` block). It's written with logical properties and palette variables only, so it can be copied into `lms-theme.css` as is.

## 7. Open questions for the Team Lead

- **Q1. Reopening a package after the dialog closes.** Task 16 has no list of a course's packages. If a teacher closes the dialog while the package is still being checked, there's no screen to come back to and attach it. Options:
  - (a) Keep the dialog open until Ready or Rejected and say so: "Keep this window open until the check finishes."
  - (b) Add a small "Packages" list (status chips from §4.3, Attach and Delete actions) to the lesson plan's add-module menu.
  - I recommend (a) for M4b, with a caption, and (b) later.
- **Q2. Arabic plurals in the countdown.** This needs four resx forms per unit (§3.1). It's a small helper; please confirm FE1 may add it in Task 15.
