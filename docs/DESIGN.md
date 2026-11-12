# LMS design system

The LMS should look like a calm, current product for school groups and universities. It must work for admins, teachers, students and parents, in Arabic (RTL, the default) and English, at 360 px and on desktop, in light and dark mode.

The product owner approved this direction on #327 (pilot PR #334: Staff list, Staff profile, Edit staff dialog). Those pages are the reference implementation.

Where things live:

| What | File |
|---|---|
| Palette, typography, radius, shadows (MudTheme) | `src/Lms.Client/Theme/LmsTheme.cs` |
| Anything the theme cannot express (shell, tables, states, forms) | `src/Lms.Client/wwwroot/css/lms-theme.css` |
| Light/dark preference | `src/Lms.Client/wwwroot/js/theme.js` (`lms.theme` in localStorage; otherwise the device setting) |
| Shared presentational components | `src/Lms.Client/Components/Ui/` |
| Page-specific old rules (calendar, org tree, import) | `src/Lms.Client/wwwroot/css/app.css` |

The UI/UX designer (`lane:ux`) owns these files. Frontend developers use them and do not fork them. If you need a new token or component, ask in your PR or open a `lane:ux` issue.

---

## 1. Tokens

### Colour

Use palette colours through MudBlazor (`Color.Primary`, `Color.Error` and so on) or CSS variables (`var(--mud-palette-primary)`). **Never hard-code hex values in pages.**

| Role | Light | Dark | Use |
|---|---|---|---|
| Primary (brand green) | `#00663D` | `#4CC38A` | One filled action per page, links, active nav, focus ring |
| Text on primary | `#FFFFFF` | `#032013` | Set automatically by `Color.Primary` |
| Secondary (slate) | `#3E4C5E` | `#AAB7C5` | Rare; neutral emphasis |
| Success | `#1D7344` | `#62C08B` | "Active", completed, passed |
| Warning | `#9A5B00` | `#E5A647` | Expiring, deactivate, needs attention |
| Error | `#B42318` | `#F2786D` | Delete, failed, validation |
| Info | `#1F5FA6` | `#77AEEA` | Shared, informational chips/alerts |
| Page background | `#F4F6F7` | `#0F1518` | `var(--mud-palette-background)` |
| Surface (cards, sidebar, app bar) | `#FFFFFF` | `#162024` | `var(--mud-palette-surface)` |
| Text primary / secondary | `#17212B` / `#526070` | `#E5ECEE` / `#9DACB5` | Secondary for labels, hints, meta |
| Lines | `#E1E6EA` | `#28343A` | `var(--mud-palette-lines-default)` |

Every text and background pair meets **WCAG AA (4.5:1)** in both modes. If you add a colour, check its contrast in both modes.

The "Active" status chip is green on purpose; the product owner decided to keep it.

### Typography

- **Arabic UI:** Cairo. **English UI:** Inter. Both are free (OFL) fonts loaded from Google Fonts in `index.html`.
- The theme picks the stack from the UI language. Arabic names inside the English UI fall back to Cairo.
- Use `MudText` with a `Typo`. Never set `font-size` or `font-family` inline.

| Typo | Size / weight | Use |
|---|---|---|
| `h4` (rendered as `h1`) | 26 px / 600 | Page title (through `PageHeader` only) |
| `h6` (rendered as `h2`) | 17 px / 600 | Section title (through `SectionCard` only) |
| `subtitle1` | 16 px / 500 | Emphasised line, empty-state title |
| `body1` | 15 px / 400 | Default text |
| `body2` | 14 px / 400 | Tables, subtitles, secondary text |
| `caption` | 12 px | Helper text, small meta |

- Use sentence case. Buttons are never all-caps; the theme sets `TextTransform = none`.
- Keep a single h1 per page, and don't skip heading levels.

### Spacing, radius, elevation and density

- **Spacing scale:** 4, 8, 12, 16, 20, 24, 32, 48 px. Use MudBlazor spacing classes (`pa-4`, `mb-4`, `gap-2`) or the layout classes below.
- **Page padding:** 24 px on desktop, 16 px at 600 px and below. The layout sets this; pages add none.
- **Radius:** 8 px on controls (buttons, inputs, alerts, nav items) and 12 px on surfaces (cards, dialogs).
- **Elevation:** surfaces are flat, with a 1 px border. Only menus, popovers and dialogs float. Don't use `MudPaper Elevation>0` on a page; use `SectionCard`.
- **Density:** tables are dense (`Dense="true"`, 14 px text, about 10 px row padding). The product owner chose this.

### Icons

- Use MudBlazor **Material Rounded** (`Icons.Material.Rounded.*`) in new UI. `NavCatalog` still uses Filled icons; leave them.
- An icon next to text is decorative. An icon-only button needs an `aria-label`; a tooltip alone is not enough.
- Direction icons (back, forward, next) must mirror in RTL. Add `Class="lms-icon-flip"`, or pick `ArrowBack`/`ArrowForward` by `Culture.IsRightToLeft`.

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
| `StaffAvatar` | A person's photo, or initials when there is none | `PhotoFileId`, `Name`, `Size` |

Other patterns:
- **Chips:** `Size.Small`. Roles use `Variant.Outlined` (`lms-role-chip`). States use `Variant.Text` with a colour, which gives a soft tint. Status chips carry a text label, not only a colour.
- **Person cell in a table:** `<div class="lms-person-cell">` holds the avatar plus a `MudLink` with class `lms-person-link`.
- **Alerts:** `Variant.Outlined` for page-level info and errors. An error tells the user what to do next.
- **Helpers:** `lms-muted` (secondary text), `lms-cell-strong`, `lms-date-cell`, `lms-visually-hidden`, `lms-chip-row`.

---

## 4. Bilingual and RTL

- Never hard-code UI text. Add every key to **both** `SharedResource.ar.resx` and `.en.resx`, and keep the wording short and plain in both. Business names come from `NameAr`/`NameEn` through `Culture.Pick`.
- Keep emails, phone numbers, ids, dates and codes left-to-right: use `<span dir="ltr">` or `DetailItem Ltr="true"`. Dates use `yyyy-MM-dd` (invariant), as they do now.
- Use logical CSS only (`margin-inline-start`, `inset-inline-end`, `text-align: start`). Never use `left`/`right` or `ml-`/`mr-` for direction-dependent spacing. Use `ms-`/`me-`.
- Check every page in Arabic and English, at 1440 and 360 px, in light and dark mode, before you open the PR.

## 5. Accessibility checklist (WCAG 2.1 AA, PRD X-10)

- [ ] One h1 per page (`PageHeader`); sections are h2 (`SectionCard`).
- [ ] Every input has a visible label. Helper text explains rules. Errors show at the field.
- [ ] Icon-only buttons have an `aria-label`. Decorative icons and avatars are `aria-hidden`.
- [ ] Every control is keyboard reachable, with a visible focus ring (the theme provides it; never remove an outline).
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
