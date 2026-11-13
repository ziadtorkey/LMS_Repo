---
name: lms-ui
description: Build or restyle LMS Blazor pages in the Stitch design system "Serene Academic Emerald" (emerald on a slate canvas, white cards with micro-shadows, Cairo headings, relaxed tables, filled inputs, pill tabs; ar RTL + en, light + dark). Use whenever you create or change a page, dialog or component under src/Lms.Client, or review an FE PR for design.
---

# LMS UI

The source of truth is `docs/DESIGN.md`, which follows the Stitch design system in `docs/design/stitch/` (#553); read both once. For a page that has a Stitch screen (`docs/design/stitch/<screen>/screen.png`), match that screen's layout. For the structure of other pages, copy `Pages/Staff/StaffListPage.razor`, `Pages/Staff/StaffProfilePage.razor` and `Pages/Staff/EditStaffDialog.razor`.

## Building a page

1. **Header.** Start the page with `PageHeader`: a title and a one-line subtitle from resx, and at most **one** filled primary button.
2. **Content.** Put the content in `SectionCard`s. A table goes in `SectionCard Flush="true"`.
3. **Small unpaged lists.** Use `MudSimpleTable Elevation="0" Class="lms-simple-table lms-stack-table"` with `data-label` on each `td`. Wrap icon buttons in `lms-row-actions`, risky ones last.
4. **Paged lists.** Use `ServerTable`, with filters in `lms-filters` inside the same card. Give every `MudTd` a `DataLabel`. Pass `EmptyTitle` and `EmptyText` (the next step).
5. **Detail pages.** Use a back link, then `PageHeader` with `Leading` (avatar) and `Badges` (status chips), then `lms-profile-grid` with a `lms-details` card of `DetailItem`s and an `lms-profile-main` column.
6. **Dialogs and forms.** Group fields with `lms-fieldset` and a legend, and put pairs in `lms-form-grid`. End with `lms-dialog-actions`: Cancel is a text button; Save is filled primary with a spinner while saving. Add helper text for rules.
7. **States.** Use `LoadingState` while loading, `EmptyState` for nothing to show, and an outlined `MudAlert` that says what to do on error.
8. **Text.** Add every new string to both `SharedResource.ar.resx` and `.en.resx`, in sentence case. Emails, phones, ids and dates go in `<span dir="ltr">`.
9. **Check.** Look at the page in ar and en, at 1440 and 360 px, in light and dark mode (the theme switch is in the app bar, or the user menu on phones). At 360 px nothing may scroll sideways.

## Rules

- **Colours:** use `Color.*` or `var(--mud-palette-*)` only, never hex. The brand is emerald `#00663D` (dark mode `#4CC38A`). Vivid `--lms-accent` (`#10B981`) is for dots and indicators only, never text.
- **Type:** use `MudText Typo=...` only, with no inline font sizes or families. The theme gives headings Cairo in both languages, and body text Cairo (ar) or Inter (en). Use sentence case; never uppercase, even where a Stitch mock does.
- **Icons:** use `Icons.Material.Rounded.*`. Icon-only buttons need an `aria-label`. Mirror direction arrows in RTL (`lms-icon-flip`).
- **Actions:** risky actions use `Variant.Text` with `Color.Warning` or `Color.Error`, and come last. Permissions decide whether an action is shown, not looks.
- **Surfaces:** use `SectionCard`, which gives a 1 px border and a micro-shadow. Don't set `Elevation` on page surfaces, and don't add CSS files or UI libraries. For KPI rows use `lms-stats` / `lms-stat`; for long settings forms, the sticky `lms-save-bar`; for card headers with an icon, `lms-section-icon`.
- **Tables and inputs** pick up the Stitch look (52 px rows, recessed 40 px fields) from CSS. Keep the standard MudBlazor variants; don't restyle per page.
- **Spacing:** use the MudBlazor classes (`mb-4`, `gap-2`) and logical CSS (`ms-`/`me-`, `inset-inline-*`).
- **Owned files:** don't edit `Theme/`, `lms-theme.css`, `lms-stitch.css` or `Components/Ui/` outside a `lane:ux` PR. Propose the change instead.

## M4b surfaces (live sessions, packages)

Follow `docs/design/m4b-live-and-packages.md`:
- Use `LiveSessionStatusChip` for session states, and `lms-join-panel` for Join. Join is disabled with a reason, never hidden.
- Show external links as a host hint (`lms-host-hint`), never as the full URL.
- Package upload uses `lms-steps` and `lms-dropzone`, with plain-words reject reasons.
- The package player uses `lms-player` with its toolbar outside the frame.

The mockups in `docs/design/mockups/m4b.html` are the reference.

## M5 surfaces (gradebook, scales, timetable, attendance, excuses)

Follow `docs/design/m5-assessment-timetable-attendance.md` (mockups `docs/design/mockups/m5.html`):
- The gradebook is a real `role="grid"` table (`lms-gb`) with the L24 keyboard model, and text forms for every cell state. Below 600 px it becomes one student-first list; never render both layouts.
- Never compute a grade in the client. Band numbers and descriptors come from the server only.
- Attendance rows have four labelled 44 px buttons in a radiogroup, plus a sticky summary bar. Timetable moves work by keyboard, menu and drag.
- Western digits everywhere (L20). The days mirror in RTL; times and percentage axes don't.

## Skeleton

```razor
<PageTitle>@L["X_Title"]</PageTitle>
<PageHeader Title="@L["X_Title"]" Subtitle="@L["X_Subtitle"]" TitleTestId="x-title">
    <Actions>
        @if (PermState.Has(Permissions.X.Create))
        {
            <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Rounded.Add"
                       OnClick="NewAsync" data-testid="x-new">@L["X_New"]</MudButton>
        }
    </Actions>
</PageHeader>

<SectionCard Flush="true">
    <div class="lms-filters" role="search" aria-label="@L["Common_Filters"]">
        <div class="lms-filter lms-filter-search">
            <MudTextField T="string" Label="@L["Common_Search"]" Adornment="Adornment.Start"
                          AdornmentIcon="@Icons.Material.Rounded.Search" ... />
        </div>
        <div class="lms-filter">...</div>
    </div>
    <ServerTable @ref="_table" T="XDto" ServerData="LoadAsync" EmptyTitle="@L["X_EmptyTitle"]" EmptyText="@L["X_EmptyText"]">
        <HeaderContent><MudTh>@L["Common_Name"]</MudTh>...</HeaderContent>
        <RowTemplate Context="row">
            <MudTd DataLabel="@L["Common_Name"]">...</MudTd>
        </RowTemplate>
    </ServerTable>
</SectionCard>
```

## Design review of an FE PR (advisory)

Check the PR against the steps and rules above. Post one comment: a **must** list (RTL broken, 360 px overflow, contrast, missing labels or states) and a **should** list (hierarchy, consistency). Never add labels and never block a merge; QA decides.
