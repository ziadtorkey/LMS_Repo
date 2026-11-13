# LMS design system

The LMS should look like a calm, current product for school groups and universities. It must work for admins, teachers, students and parents, in Arabic (RTL, the default) and English, at 360 px and on desktop, in light and dark mode.

**Source of truth: the Stitch design system "Serene Academic Emerald"** (`docs/design/stitch/`: 9 screens with their HTML, and `serene_academic_emerald/DESIGN.md`).
- The owner adopted it on 2026-11-13 (#553). It replaces the earlier direction from #327/#334 wherever the two differ.
- The non-negotiables still hold: Arabic RTL and English, light and dark mode, WCAG AA, and MudBlazor.
- Where Stitch is silent (dark mode on most screens, RTL, 360 px), this file extends its patterns and says so.
- Where a Stitch mock screen contradicts Stitch's own DESIGN.md or WCAG, see "Deviations from the Stitch mocks" below.

Where things live:

| What | File |
|---|---|
| Palette, typography, radius, shadows (MudTheme) | `src/Lms.Client/Theme/LmsTheme.cs` |
| Anything the theme cannot express (shell, tables, states, forms) | `src/Lms.Client/wwwroot/css/lms-theme.css` |
| The Stitch foundation layer (cards, tables, inputs, segmented tabs, chips, stat tiles, save bar) | `src/Lms.Client/wwwroot/css/lms-stitch.css`, loaded after `lms-theme.css` |
| Light/dark preference | `src/Lms.Client/wwwroot/js/theme.js` (`lms.theme` in localStorage; otherwise the device setting) |
| Shared presentational components | `src/Lms.Client/Components/Ui/` |
| Page-specific old rules (calendar, org tree, import) | `src/Lms.Client/wwwroot/css/app.css` |
| Live session and package surfaces (M4b) | `src/Lms.Client/wwwroot/css/lms-activities.css` (spec: `docs/design/m4b-live-and-packages.md`) |

The UI/UX designer (`lane:ux`) owns these files. Frontend developers use them and do not fork them. If you need a new token or component, ask in your PR or open a `lane:ux` issue.

---

## 1. Tokens

### Colour

Use palette colours through MudBlazor (`Color.Primary`, `Color.Error` and so on) or CSS variables (`var(--mud-palette-primary)`). **Never hard-code hex values in pages.**

| Role | Light | Dark | Use |
|---|---|---|---|
| Primary (institutional emerald) | `#00663D` | `#4CC38A` | The one filled action per page, active nav (filled), links |
| Text on primary | `#FFFFFF` | `#032013` | Set automatically by `Color.Primary` |
| Secondary (slate) | `#3E4C5E` | `#94A3B8` | Quiet chrome, metadata |
| Accent (vivid emerald) | `#10B981` (`--lms-accent`) | same | **Dots, live beacons, active indicators only.** It's 2.5:1 on white, so never use it for text. |
| Success | `#1D7344` | `#34D399` | Active, completed, passed |
| Warning | `#B45309` | `#FBBF24` | Pending, expiring, needs attention |
| Error | `#B42318` | `#F87171` | Delete, failed, validation |
| Info | `#1E40AF` | `#60A5FA` | Informational chips and alerts |
| Canvas (level 0) | `#F8FAFC` | `#090D10` | `var(--mud-palette-background)` |
| Surface (level 1: cards, sidebar) | `#FFFFFF` | `#121A20` | `var(--mud-palette-surface)` |
| Soft panel / table header | `#F1F5F9` | `#16202A` | `var(--mud-palette-background-gray)` |
| Recessed field / tab track | `#EFF4FF` | `#16202A` | `var(--lms-recessed)` |
| Text primary / secondary | `#0B1C30` / `#475569` | `#E2E8F0` / `#94A3B8` | Secondary for labels, hints, meta |
| Borders / row dividers | `#E2E8F0` / `#F1F5F9` | `#1E293B` / `#1E293B` | `--mud-palette-lines-default` / `--mud-palette-table-lines` |
| Control borders | `#7A8793` | `#6A7B85` | `--mud-palette-lines-inputs` (3:1, WCAG 1.4.11) |

Every text colour above reaches **WCAG AA (4.5:1)** on the surface and the canvas in both modes, and control borders reach 3:1. `LmsThemeTests` asserts this; if you add a colour, add it there.

### Typography

- **Headings** (page and section titles) use **Cairo in both languages**, following Stitch's headline tokens.
- **Body text** uses Cairo in the Arabic UI and Inter in the English UI. Latin codes, emails and URLs inside Arabic go in `<span dir="ltr">`.
- Both fonts are free (OFL) and load from Google Fonts in `index.html`.
- Use `MudText` with a `Typo`. Never set `font-size` or `font-family` inline.

| Typo | Size / weight | Stitch token | Use |
|---|---|---|---|
| `h4` (rendered as `h1`) | 32 px / 700 (26 px below 600 px) | headline-xl | Page title, through `PageHeader` only |
| `h5` | 24 px / 600 | headline-lg | Large card titles, stat numbers |
| `h6` (rendered as `h2`) | 18 px / 600 | headline-md | Section title, through `SectionCard` only |
| `subtitle1` | 16 px / 500 | subtitle-lg | Emphasised line, empty-state title |
| `subtitle2` | 13 px / 500 | label-default | Labels, overlines |
| `body1` | 15 px / 400 | body-default | Default text |
| `body2` | 14 px / 400 | body-table | Tables, subtitles, secondary text |
| `caption` | 12 px | caption | Helper text, small meta |

- Use **sentence case everywhere**. Nothing is uppercased: Stitch's DESIGN.md forbids it, and Arabic has no case.
- Keep a single h1 per page, and don't skip heading levels.

### Spacing, radius, elevation and density

- **Grid:** 8 px base. The spacing scale is 4, 8, 12, 16, 20, 24, 32, 48 px.
- **Page padding:** 32 px on desktop (from 1025 px), 24 px on tablets and 16 px below 600 px. The layout sets it; pages add none.
- **Radius:** 8 px on controls (buttons, inputs, selects, tab pills, alerts, nav items), 12 px on surfaces (cards, dialogs, table containers), and fully round for status chips, avatars and live dots.
- **Elevation:**
  - **Level 0:** the canvas.
  - **Level 1:** cards, tables and the sidebar. White (or dark surface) with a 1 px border and a micro-shadow (`--lms-shadow-1`).
  - **Level 2:** menus, popovers and dialogs, with a diffused shadow (`--lms-shadow-2`).
  - **Hover:** clickable cards (`lms-card-link`) lift by 1 px with an emerald-tinted border. Static cards don't move.
- **Tables:** relaxed rows of about 52 px, with 16 px cell padding, a soft tinted header (13 px / 600, sentence case), hairline dividers and a neutral row hover. This replaces the earlier dense tables. Keep `Dense="true"` on `MudTable`; the padding comes from CSS.
- **Inputs:** 40 px high, with a recessed fill, an 8 px radius and a 1 px control border. Focus turns the border emerald and adds a soft glow. All of this is applied in CSS to the standard MudBlazor inputs, so pages don't change their variant.
- **Focus:** a visible two-tone ring (`--lms-focus-ring`: a 2 px surface gap and a 2 px emerald ring) on every interactive element.

### Icons

- Use MudBlazor **Material Rounded** (`Icons.Material.Rounded.*`) in new UI. `NavCatalog` still uses Filled icons; leave them.
- An icon next to text is decorative. An icon-only button needs an `aria-label`; a tooltip alone is not enough.
- Direction icons (back, forward, next) must mirror in RTL. Add `Class="lms-icon-flip"`, or pick `ArrowBack`/`ArrowForward` by `Culture.IsRightToLeft`.

---

### Deviations from the Stitch mocks (and why)

| Stitch mock | What we do | Why |
|---|---|---|
| ALL-CAPS overlines, sidebar group labels and table headers | Sentence case | Stitch's own DESIGN.md forbids uppercase; Arabic has no case |
| A 15 % emerald glow as the only focus indicator | A two-tone 2 px ring plus the glow on inputs | The glow alone is under 3:1 (WCAG 2.4.7 / 1.4.11) |
| Vivid emerald `#10B981` text ("→ Active") | `#10B981` only for dots and indicators; text uses Success | 2.5:1 on white fails AA |
| A top navigation bar on the roles screen | The sidebar on every page | 8 of the 9 screens use the sidebar; one navigation pattern |
| "Serene LMS" brand | Our app name (`App_Title`) | Placeholder in the mocks |
| Dark mode, RTL and 360 px (mostly not drawn) | Extended from the tokens (table above), mirrored with logical CSS | Stitch is silent |

---

## 2. Layout rules

1. **Each page starts with `PageHeader`.** It holds the title, an optional one-line subtitle that says what the page is for, and the actions.
2. **Use one primary action per page:** `Variant.Filled`, `Color.Primary`. Secondary actions use `Variant.Outlined`. Risky actions (deactivate, delete, remove) use `Variant.Text` with `Color.Warning` or `Color.Error`, placed last. Don't build a wall of equal buttons.
3. **Group content in `SectionCard`s.** A table or list goes in a card with `Flush="true"`, so it reaches the card edges.
4. **Put list filters in a toolbar at the top of the table's card** (`<div class="lms-filters" role="search">`). Each filter goes in a `<div class="lms-filter">`; the search box adds `lms-filter-search`. Filters stack on phones.
5. **Detail pages** use `<div class="lms-profile-grid">`: a details card (`<dl class="lms-details">` with `DetailItem`s) and a main column (`lms-profile-main`) of cards. Two columns from 1100 px, one below. Add a back link to the list (`lms-back-link`, flipped arrow).
6. **Lay out forms and dialogs in sections.** Use `<fieldset class="lms-fieldset">` with a `<legend class="lms-fieldset-legend">` for each group, and `<div class="lms-form-grid">` for side-by-side fields (two columns from 600 px). Put actions in `<div class="lms-dialog-actions">`: Cancel is `Variant.Text`, Save is filled primary and shows a spinner while saving.
7. **Make 360 px work.** The page must never scroll sideways. Tables turn into labelled cards below 600 px, so **give every `MudTd` a `DataLabel`**. Long text wraps (`overflow-wrap: anywhere`), and flex children get `min-width: 0`.

---

## 3. Components (`Components/Ui/`, plus `ServerTable` and the people components)

| Component | Use | Key parameters |
|---|---|---|
| `PageHeader` | Top of every page | `Title`, `Subtitle`, `TitleTestId`, `Leading` (avatar), `Badges` (status chips), `Actions` |
| `SectionCard` | A block of the page | `Title` (h2, labels the section), `Actions`, `Flush`, `TestId` |
| `ServerTable<T>` | Every server-paged list | `ServerData`, `HeaderContent`, `RowTemplate`, `EmptyTitle`, `EmptyText`, `EmptyIcon` (it handles the loading, empty and error-with-retry states) |
| `EmptyState` | Nothing to show (also in `NoRecordsContent` of a plain `MudTable`) | `Title`, `Text` (what to do next), `Icon`, `ChildContent` (an action) |
| `LoadingState` | Page or section loading (skeleton plus a screen-reader text) | `Lines`, `TestId` |
| `DetailItem` | One label and value in `<dl class="lms-details">` | `Label`, `Value` (an empty value shows a dash), `Ltr` (emails, phones, ids), `TestId` |
| `PersonStatusChip` | A person's status | `Status` |
| `LiveSessionStatusChip` (`Components/LiveSessions/`) | A live session's status (spec §2.2): Scheduled, Live now (pulsing dot), Ended, Cancelled, Waiting for Zoom | `Status`, `ShowHint` (shows the "waiting for Zoom" explanation under the chip; use it in lists and the join panel) |
| `StaffAvatar` | A person's photo, or initials when there is none | `PhotoFileId`, `Name`, `Size` |

Stitch patterns (`lms-stitch.css`):
- **Stat tiles** (`lms-stats` > `lms-stat`): a row of KPI cards at the top of dashboards and list pages. Each tile holds `lms-stat-label`, `lms-stat-value` and an `lms-stat-icon` tile.
- **Card header icon** (`lms-section-icon`): a 36 px tinted icon tile before a `SectionCard` title, as on the Stitch settings cards.
- **Segmented tabs:** `MudTabs Class="lms-tabs"` and `ClassCourseTabs` render as a pill track with a raised active tab. `MudToggleGroup` gets the same look.
- **Save bar** (`lms-save-bar` with `lms-save-bar-text`): a sticky bar at the bottom of long settings forms when there are unsaved changes. Discard (text) and Save (filled) go at its end.
- **Active nav item:** filled emerald with white text. Group the sidebar under `lms-nav-section` headings.

Other patterns:
- **Chips:** `Size.Small` renders as a 24 px pill with 12 px padding. Roles use `Variant.Outlined` (`lms-role-chip`). States use `Variant.Text` with a colour, for a soft tint, plus `lms-status-chip`, which adds the 6 px dot. Status chips carry a text label, not only a colour.
- **Small, unpaged lists in `MudSimpleTable`:** use `Elevation="0" Class="lms-simple-table lms-stack-table"` inside a `SectionCard Flush`. Give each `td` a `data-label` attribute so phones show labelled cards. Put the actions cell in `<td class="lms-cell-actions">` with the actions title visually hidden.
- **Row actions:** wrap icon buttons in `<div class="lms-row-actions">`. They render in the quiet action colour. Put risky actions last, in Warning or Error colour, after a `<span class="lms-row-actions-sep">`. Each icon button needs an `aria-label` and, in dense groups, a `MudTooltip` with the same text.
- **Person cell in a table:** `<div class="lms-person-cell">` holds the avatar plus a `MudLink` with class `lms-person-link`.
- **Alerts:** `Variant.Outlined` for page-level info and errors. An error tells the user what to do next.
- **Helpers:** `lms-muted` (secondary text), `lms-cell-strong`, `lms-date-cell`, `lms-visually-hidden`, `lms-chip-row`.

---

### M4b classes (`lms-activities.css`)

| Class | Markup |
|---|---|
| `lms-live-dot` | `<span class="lms-live-dot"></span>` inside the "Live now" chip |
| `lms-when` | `<span class="lms-when"><span class="lms-when-date">2026-11-20</span><span class="lms-when-time">10:00–11:00</span></span>` |
| `lms-host-hint` | `<span class="lms-host-hint">zoom.us</span>` next to an external link |
| `lms-recurrence-preview` | a `div` holding `<div class="lms-recurrence-preview-count" role="status">` (add `lms-invalid` above 52) and a `<ul>` of dates |
| `lms-day-toggles` | a `div role="group"` of `MudButton`s with `aria-pressed` |
| `lms-choice` | a `label` row around a radio, title and caption; add `lms-choice-on` to the selected row |
| `lms-kind-tile`, `lms-date-block` | a 40 px icon tile; a 44 px block with `lms-date-block-day` and `lms-date-block-month` |
| `lms-join-panel` | the soft panel under a live session summary |
| `lms-steps` | an `<ol>`; each `li` holds `lms-step-number` and `lms-step-label`; mark it `lms-step-done`, `aria-current="step"` or `lms-step-failed`; put `<span class="lms-step-of">Step 2 of 3 ·</span>` inside the current label (shown on phones only) |
| `lms-dropzone`, `lms-file-row` | the dashed upload area (add `lms-dropzone-active` while dragging); a file row with `lms-file-row-main` and `lms-file-row-name` |
| `lms-player` | the container, holding an optional `lms-preview-strip`, then `lms-player-bar` (`lms-player-title`, `lms-save-state`, Full screen, Exit), then `lms-player-stage` holding the `iframe.lms-player-frame` and an optional `lms-player-overlay` |
| `lms-save-state` | the save status text; add `lms-save-failed` on failure |

## 4. Bilingual and RTL

- Never hard-code UI text. Add every key to **both** `SharedResource.ar.resx` and `.en.resx`, and keep the wording short and plain in both. Business names come from `NameAr`/`NameEn` through `Culture.Pick`.
- Keep emails, phone numbers, ids, dates and codes left-to-right: use `<span dir="ltr">` or `DetailItem Ltr="true"`. Dates use `yyyy-MM-dd` (invariant), as they do now.
- **Western digits (0–9) in both languages** for grades, points, percentages, dates, times and counts (M5 L20; numbers are culture-invariant since #509). Numbers next to Arabic text sit in an LTR span with tabular numerals.
- Use logical CSS only (`margin-inline-start`, `inset-inline-end`, `text-align: start`). Never use `left`/`right` or `ml-`/`mr-` for direction-dependent spacing. Use `ms-`/`me-`.
- Check every page in Arabic and English, at 1440 and 360 px, in light and dark mode, before you open the PR.

## 5. Accessibility checklist (WCAG 2.1 AA, PRD X-10)

- [ ] One h1 per page (`PageHeader`); sections are h2 (`SectionCard`).
- [ ] Every input has a visible label. Helper text explains rules. Errors show at the field.
- [ ] Icon-only buttons have an `aria-label`. Decorative icons and avatars are `aria-hidden`.
- [ ] Every control is keyboard reachable, with a visible focus ring (the theme provides it; never remove an outline).
- [ ] **Focus is never obscured** (WCAG 2.4.11): inside a scroller with sticky headers or columns, set `scroll-padding` to their size so the focused item is never under them; test in LTR and RTL.
- [ ] **Forced colors:** focus outlines use `Highlight`, and chips and toggle buttons keep a `CanvasText` border, so state survives Windows high-contrast mode.
- [ ] Text contrast is at least 4.5:1 in light and dark mode.
- [ ] Permission rules decide whether an action is hidden or disabled. Never hide an action only for looks.
- [ ] Loading, empty and error states are all present (`ServerTable` handles lists).
- [ ] Nothing scrolls sideways at 360 px; table cells have `DataLabel`s.

## 6. Do and don't

| Do | Don't |
|---|---|
| `PageHeader` with one filled primary button | Several filled buttons side by side |
| `SectionCard` for blocks | `MudPaper Elevation="2"` with custom padding |
| `Color.Primary`, `var(--mud-palette-*)` | Hex colours, `style="color:#..."` |
| `Typo.*` through `MudText` | Inline `font-size`, a new font |
| `EmptyState` with a next step ("Change the filters or add …") | "No data" with nothing else |
| `DataLabel` on every `MudTd` | Tables that overflow at 360 px |
| `Icons.Material.Rounded.*`, mirrored arrows in RTL | Emoji as icons, arrows that point the wrong way in Arabic |
| Sentence-case labels in ar and en | ALL CAPS, hard-coded English |
| Text buttons in warning/error colour for risky actions | Big red filled Delete next to the primary action |

## 7. Changing the design system

- Tokens and shared components change only in `lane:ux` PRs. Frontend developers can propose changes in their PR description.
- Keep `LmsTheme.cs`, `lms-theme.css` and this file in sync. A new rule or component also goes in the `lms-ui` skill (`.claude/skills/lms-ui/SKILL.md`).

## 7a. M4b patterns: live sessions and package player

The full spec is `docs/design/m4b-live-and-packages.md`. Mockups are in `docs/design/mockups/m4b.html` and screenshots in `docs/screenshots/ux-m4b/`. In short:

- **External links** (Zoom, recordings) never show the URL. They show a host hint (`lms-host-hint`, LTR, for example `zoom.us`) and "Opens in a new tab".
- **Server time** drives every countdown and window. Countdowns are `role="timer"` with polite announcements at 15, 5 and 1 min. Arabic countdowns use proper plural forms, never "2 ساعة".
- **Session status chips** (`LiveSessionStatusChip`, `Variant.Text`):
  - Scheduled: Info.
  - Live now: Success, with a pulsing dot (static under reduced motion).
  - Ended and Cancelled: Default.
  - Waiting for Zoom (SyncFailed): Warning, with the explanation shown under the chip, not only in a tooltip.
- **Join panel** (`lms-join-panel`): one primary Join button. It is disabled with a reason before the window opens, and never hidden. A blocked tab falls back to an "Open the meeting" link inside an Info alert.
- **Recurrence preview** (`lms-recurrence-preview`): the count line is live, followed by the first 6 dates. More than 52 sessions blocks Schedule with a plain message.
- **Package upload** uses three numbered steps (`lms-steps`: Upload · Check · Attach) and a dashed drop zone (`lms-dropzone`). Reject reasons are plain words with a next step (spec §4.2).
- **Package player** (`lms-player`):
  - A toolbar outside the frame holds the title, the save state, Full screen and Exit.
  - The frame stays white in both themes.
  - The staff preview has an Info strip: "Preview: nothing is recorded."
  - A timed-out token shows an overlay with Reload.

## 7b. M5 patterns: gradebook, scales, timetable, attendance, excuses

The full spec is `docs/design/m5-assessment-timetable-attendance.md`. Mockups are in `docs/design/mockups/m5.html` and screenshots in `docs/screenshots/ux-m5/`. In short:

- **Gradebook grid** (`lms-gb`): a real `role="grid"` table with a sticky header and student column, category groups, tinted category, Final and Band columns, and text forms for every cell state. The keyboard follows plan L24 (Enter or F2 edits, Shift+F10 opens the menu). Below 600 px it becomes **one** student-first list (`lms-gb-item`).
- **Weights chip** (`lms-weights-chip`): success at 100, warning with the missing amount otherwise.
- **Scales**: a band editor plus a **coverage strip** (`lms-coverage`, LTR, with gaps hatched and also listed as text). The provisional chip appears on admin pages only, and publishing under a provisional scale is blocked with an error alert.
- **Timetable** (`lms-tt`, `lms-slot`): the days mirror in RTL but the times don't. Exceptions and conflicts are chips with words. Moving works by keyboard (M, arrows, Enter, Esc), a menu, or drag. Below 900 px it becomes a day view.
- **Attendance** (`lms-att-*`): four labelled 44 px buttons per row in a radiogroup, digit shortcuts 1–4 on desktop, and a sticky summary bar above the safe area.
- **Excuses**: a stepper on phones, a review panel with the 30-day history strip (letters, not only colours), and raster-only inline previews.

## 8. Phase 2 status (page-by-page passes)

This list was last updated on 2026-11-12. "Waiting" means an open feature PR touches the page; it gets its pass after that PR merges.

| Module / pages | Status | Issue → PR |
|---|---|---|
| Theme, shell, Staff list, Staff profile, Edit staff | Done (pilot, merged) | #327 → #334 |
| Organization: org tree, level types, templates, their dialogs | In review | #402 → #404 |
| Organization: unit settings page and its tabs | Waiting (#287, #291) | — |
| Academic years: years, terms, calendar | In review (on top of #404) | #407 → #409 |
| Access: roles, role editor, SSO providers | In review (on top of #404) | #411 → #412 |
| Access: role assignments | Waiting (#203) | — |
| Audit log and detail drawer | In review (on top of #404) | #413 → #414 |
| Clients (platform admin) | In review (on top of #404) | #416 → #417 |
| Notifications and preferences | In review (on top of #404) | #419 → #420 |
| Staff import: page frame only (back link, loading) | In review | #422 → this PR |
| Import wizard and history (`Components/Imports`), used by Staff, Students, Courses and Classes imports | Waiting (#316) | — |
| Home | Waiting (#309, #392, #396) | — |
| Courses: list, profile, tabs, My courses, dialog, import, blueprint builder | FE1, in progress (on its own PR stack; designer gives an advisory review) | — |
| Students, guardians, change requests, Me pages | Waiting (#303, #309, #316) | — |
| Classes, enrollment, groups | Waiting (#317, #322, #324) | — |
