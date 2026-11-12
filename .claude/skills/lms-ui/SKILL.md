---
name: lms-ui
description: Build or restyle LMS Blazor pages in the approved LMS design (brand green, light sidebar, Cairo/Inter, dense tables, ar RTL + en, light + dark). Use whenever you create or change a page, dialog or component under src/Lms.Client, or review an FE PR for design.
---

# LMS UI

The source of truth is `docs/DESIGN.md`; read it once. The reference pages are `Pages/Staff/StaffListPage.razor`, `Pages/Staff/StaffProfilePage.razor` and `Pages/Staff/EditStaffDialog.razor`. Copy their structure.

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

- **Colours:** use `Color.*` or `var(--mud-palette-*)` only, never hex. The brand is green `#00663D` (dark mode `#4CC38A`).
- **Type:** use `MudText Typo=...` only, with no inline font sizes or families. Cairo (ar) and Inter (en) come from the theme.
- **Icons:** use `Icons.Material.Rounded.*`. Icon-only buttons need an `aria-label`. Mirror direction arrows in RTL (`lms-icon-flip`).
- **Actions:** risky actions use `Variant.Text` with `Color.Warning` or `Color.Error`, and come last. Permissions decide whether an action is shown, not looks.
- **Surfaces:** keep them flat. Don't use `Elevation` on page surfaces, and don't add new CSS files or UI libraries.
- **Spacing:** use the MudBlazor classes (`mb-4`, `gap-2`) and logical CSS (`ms-`/`me-`, `inset-inline-*`).
- **Owned files:** don't edit `Theme/`, `lms-theme.css` or `Components/Ui/` outside a `lane:ux` PR. Propose the change instead.

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
