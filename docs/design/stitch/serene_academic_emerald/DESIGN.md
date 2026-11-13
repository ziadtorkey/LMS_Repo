---
name: Serene Academic Emerald
colors:
  surface: '#f8f9ff'
  surface-dim: '#cbdbf5'
  surface-bright: '#f8f9ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#eff4ff'
  surface-container: '#e5eeff'
  surface-container-high: '#dce9ff'
  surface-container-highest: '#d3e4fe'
  on-surface: '#0b1c30'
  on-surface-variant: '#3f4941'
  inverse-surface: '#213145'
  inverse-on-surface: '#eaf1ff'
  outline: '#6f7a71'
  outline-variant: '#bec9bf'
  surface-tint: '#0e6c42'
  primary: '#004c2c'
  on-primary: '#ffffff'
  primary-container: '#00663d'
  on-primary-container: '#8de1ad'
  inverse-primary: '#84d8a5'
  secondary: '#515f72'
  on-secondary: '#ffffff'
  secondary-container: '#d2e1f7'
  on-secondary-container: '#566477'
  tertiary: '#004c32'
  on-tertiary: '#ffffff'
  tertiary-container: '#006645'
  on-tertiary-container: '#5ae8ac'
  error: '#B42318'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#a0f5bf'
  primary-fixed-dim: '#84d8a5'
  on-primary-fixed: '#002110'
  on-primary-fixed-variant: '#005230'
  secondary-fixed: '#d5e4fa'
  secondary-fixed-dim: '#b9c8dd'
  on-secondary-fixed: '#0e1c2c'
  on-secondary-fixed-variant: '#3a485a'
  tertiary-fixed: '#6ffbbe'
  tertiary-fixed-dim: '#4edea3'
  on-tertiary-fixed: '#002113'
  on-tertiary-fixed-variant: '#005236'
  background: '#f8f9ff'
  on-background: '#0b1c30'
  surface-variant: '#d3e4fe'
  surface-light: '#FFFFFF'
  background-light: '#F8FAFC'
  surface-dark: '#121A20'
  background-dark: '#090D10'
  border-subtle-light: '#E2E8F0'
  border-subtle-dark: '#1E293B'
  success: '#1D7344'
  success-bright: '#34D399'
  warning: '#B45309'
  warning-bright: '#FBBF24'
  error-bright: '#F87171'
  info: '#1E40AF'
  info-bright: '#60A5FA'
typography:
  headline-xl:
    fontFamily: Cairo
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
    letterSpacing: -0.02em
  headline-xl-mobile:
    fontFamily: Cairo
    fontSize: 26px
    fontWeight: '700'
    lineHeight: 34px
    letterSpacing: -0.01em
  headline-lg:
    fontFamily: Cairo
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Cairo
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 26px
  subtitle-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '500'
    lineHeight: 24px
  body-default:
    fontFamily: Inter
    fontSize: 15px
    fontWeight: '400'
    lineHeight: 22px
  body-table:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-table-medium:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 20px
  label-default:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: '500'
    lineHeight: 18px
    letterSpacing: 0.01em
  caption:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  gutter: 1.25rem
  gutter-desktop: 1.5rem
  margin: 1rem
  margin-desktop: 2rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 0.75rem
  space-lg: 1.25rem
  space-xl: 2rem
---

## Brand & Style

This design system establishes a calm, dignified, and academically rigorous environment tailored for modern educational institutions, multi-campus school groups, and universities across the Middle East and globally. Built from the ground up for native bilingual fluency (Arabic RTL first, paired with English LTR), the design reconciles institutional authority with fluid digital refinement.

The visual style blends **Modern Corporate** structure with **tactile, atmospheric depth**. Utilitarian, flat tabular interfaces and harsh outlines are replaced by gentle tonal card elevations, soft zinc-slate backdrops, subtle glassmorphic layers, and warm emerald accents. The interface avoids sensory overload—critical for educators managing large rosters, administrators orchestrating policy settings, and students reviewing coursework—fostering high cognitive clarity and focus.

## Colors

The palette is anchored by the signature institutional emerald (`#00663D`), reinforced with vivid emerald (`#10B981`) for active cues, micro-indicators, and hover highlights. Utilitarian cold grays have been upgraded to tailored slate and zinc neutrals (`#F8FAFC` canvas in light mode and deep atmospheric slate `#090D10` in dark mode).

### Functional Spectrum
- **Primary Emerald (`#00663D` Light / `#4CC38A` Dark):** Reserved for the single most consequential call to action on the screen, active navigation states, and primary focal links.
- **Secondary Slate (`#3E4C5E` Light / `#94A3B8` Dark):** Governs structural secondary chrome, section metadata, and quiet contextual badges.
- **Tertiary Accent (`#10B981`):** Powers live indicators, active segmented switches, verified states, and focus rings.
- **Semantic Feedback:** Muted backgrounds with high-contrast text (`#1D7344` success, `#B45309` warning, `#B42318` error, `#1E40AF` info) guarantee WCAG 2.1 AA compliance (minimum 4.5:1 text-to-background ratio) across both light and dark modes.

## Typography

Typography adapts seamlessly between Arabic (Cairo) and English/Latin scripts (Inter). Cairo provides elegant, authentic calligraphic proportions with strong vertical geometry, preventing cramped line heights common in multi-lingual academic software. Inter provides neutral, hyper-legible figures, codes, and Latin labels.

### Typography Guidelines
- **Font Switching:** Set the root CSS stack based on the current locale (`dir="rtl"` defaults to Cairo; `dir="ltr"` defaults to Inter). When Latin characters appear within an Arabic context (course codes like `MATH101`, email addresses, server URLs), wrap them in `<span dir="ltr">` using Inter.
- **Hierarchy:** Strictly limit pages to one `headline-xl` (page title). Section blocks inside cards use `headline-md`.
- **Styling:** Never apply uppercase transforms (`text-transform: uppercase`) to English strings; use sentence case consistently to maintain visual parity with Arabic, which has no capitalization concept.

## Layout & Spacing

The layout is built upon an 8px base grid rhythm, pairing an adaptive 12-column responsive fluid canvas with a fixed-width collapsable sidebar (260px expanded, 72px icon rail).

### Directional Layout (RTL & LTR)
- **Logical Properties:** All margins, paddings, and absolute coordinate positioning must use logical CSS properties (`margin-inline-start`, `padding-inline-end`, `inset-inline-start`). Physical `left` and `right` coordinates are forbidden.
- **Responsive Breakpoints:**
  - **Phone (< 600px):** Single-column layout. Margin resets to `1rem` (16px). Data tables collapse into structured micro-cards utilizing standard `DataLabel` bindings. Filters stack vertically.
  - **Tablet (600px - 1024px):** 6-column layout with 20px gutters. Filter toolbars compress into responsive horizontal groups.
  - **Desktop (>= 1025px):** 12-column layout. Margin expands to `2rem` (32px). Page details adopt asymmetric two-column splits (320px metadata side panel + main content card stream).

## Elevation & Depth

Visual hierarchy abandons flat gray borders and heavy drop shadows in favor of a clean, layered architectural stacking system:

### Tiers of Depth
1. **Canvas Level (Level 0):** Background foundation (`#F8FAFC` light / `#090D10` dark). Provides a quiet, non-distracting canvas.
2. **Elevated Surfaces (Level 1):** Main cards, tables, and sidebar rail (`#FFFFFF` light / `#121A20` dark). Framed by a delicate 1px boundary (`#E2E8F0` light / `#1E293B` dark) coupled with an ambient micro-shadow (`0 1px 3px 0 rgba(0, 0, 0, 0.04), 0 1px 2px -1px rgba(0, 0, 0, 0.02)` in light mode).
3. **Floating Contexts (Level 2):** Modals, dropdown menus, and popovers. Elevated with an ambient blur filter (`backdrop-filter: blur(12px)`) and a diffused shadow (`0 10px 25px -5px rgba(0, 0, 0, 0.08), 0 8px 10px -6px rgba(0, 0, 0, 0.04)`).
4. **Interactive Hover States:** Cards subtly elevate with `transform: translateY(-1px)` and a soft, low-contrast emerald tint along the border (`rgba(16, 185, 129, 0.25)`).

## Shapes

The interface embraces a balanced shape geometry (`roundedness: 2`) that brings warmth without compromising high-density data requirements:
- **Interactive Controls (8px / `rounded-md`):** Buttons, input fields, selects, segmented control toggles, and status badges.
- **Containers & Surfaces (12px / `rounded-lg`):** Cards, modal dialogs, data table outer containers, and profile callouts.
- **Pills (`rounded-full`):** Reserved exclusively for status indicators, active live pulses, and avatar frames.

## Components

### Buttons & Interactive Controls
- **Primary Action:** One per page or dialog context (`#00663D` background, `#FFFFFF` text, 8px radius). Features a subtle inner top highlight (`box-shadow: inset 0 1px 0 rgba(255,255,255,0.15)`) to eliminate flat visual monotony.
- **Secondary Action:** Outlined with a 1px border (`#E2E8F0` light / `#334155` dark), slate text, and gentle slate hover tint.
- **Destructive/Risky Actions:** Never styled as bold red blocks alongside primary actions. Rendered as quiet text buttons (`Variant.Text`) with warning or error coloration, positioned at the end of the action group.

### Status Chips & Badges
- **Status Indicators:** Pill-shaped (`rounded-full`), 24px height, 12px horizontal padding. Composed of soft tinted backgrounds (10% opacity of the semantic color) with high-contrast text and a 6px circular indicator dot.
  - *Active:* `#ECFDF5` background, `#065F46` text, `#10B981` dot.
  - *Inactive/Draft:* `#F1F5F9` background, `#475569` text, `#94A3B8` dot.
  - *Pending/Warning:* `#FFFBEB` background, `#92400E` text, `#F59E0B` dot.
- **Live Session Indicator:** Houses an animated pulsing beacon dot (`@keyframes pulse`) for active online classrooms.

### Data Tables & Lists
- **Structure:** Encased in a single continuous card container (`rounded-lg`, 1px border). Flat, raw table borders are eliminated.
- **Header:** Elevated with a subtle slate tint (`#F8FAFC` light / `#16202A` dark), 13px bold tracking, and distinct vertical alignment.
- **Rows:** Minimum height of 52px (relaxing dense, cramped legacy rows) with 16px horizontal cell padding. Row dividers use soft hairline borders (`#F1F5F9` light / `#1E293B` dark).
- **Row Hover:** Transitions smoothly with a neutral tint (`#F8FAFC` light / `#1A242D` dark).
- **Actions Cell:** Aligned to the logical inline-end with grouped, muted icon buttons and interactive tooltips.

### Form Inputs & Segmented Controls
- **Input Fields:** 40px height with 8px radius, recessed subtle surface color, and crisp 1px borders. Focus states introduce an emerald glow ring (`0 0 0 3px rgba(16, 185, 129, 0.15)`).
- **Segmented Tabs:** Replaces legacy flat underlined tabs with pill-capsule containers. Active tabs feature a crisp white/dark-slate background card and subtle ambient shadow.
- **Toggle Switches:** Replaces raw checkboxes for feature toggles (such as Hijri date options or SCORM settings), pairing clear labels with instant state updates.