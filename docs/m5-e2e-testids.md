# M5 FE2 pages: `data-testid` map for the E2E demos

For QA (demos 2 and 3). Ids are stable; ask FE2 before renaming one. `{id}` is a GUID, `{n}` a period order, day `0` = Sunday.

## Timetable (demo 2)
| Page / control | Test id |
|---|---|
| Builder `/classes/{classId}/timetable`: title, grid, live region | `tt-title`, `tt-grid`, `tt-live` |
| Term picker, state chip, mode chip | `tt-term`, `tt-state`, `tt-mode` |
| Actions: publish, copy term, validate, export | `tt-publish`, `tt-copy`, `tt-validate`, `tt-export` |
| Cell, lesson card, room, add button, lesson menu button | `tt-cell-{day}-{n}`, `tt-slot-{slotId}`, `tt-room-{slotId}`, `tt-add-{day}-{n}`, `tt-menu-{slotId}` |
| Badges: draft, clash, changed on a date | `tt-draft-{slotId}`, `tt-warn-{slotId}`, `tt-exception-{slotId}` |
| Refusal / clash sentence, conflicts panel | `tt-conflict`, `tt-conflicts-list`, `tt-conflicts-none` |
| Lesson menu: move selects, move, edit, delete, changes on a date | `tt-move-day`, `tt-move-period`, `tt-move-confirm`, `tt-edit`, `tt-delete`, `tt-exceptions` |
| Lesson dialog | `slot-course`, `slot-teacher`, `slot-room`, `slot-save`, `slot-error` |
| Publish dialog, copy dialog | `publish-message`, `publish-confirm`; `copy-source`, `copy-run`, `copy-done`, `copy-skipped` |
| Exception dialog | `exception-date`, `exception-kind`, `exception-substitute`, `exception-period`, `exception-note`, `exception-save`, `exception-list`, `exception-error`, `exception-close` |
| Set-up `/org-units/{unitId}/timetable-setup` | `setup-title`, `period-new`, `periods-table`, `period-row-{id}`, `period-edit-{id}`, `period-delete-{id}`, `wd-{Sun..Sat}`, `wd-save`, `wd-saved` |
| Unit class list `/timetable` | `tt-classes-title`, `tt-class-{classId}` |
| My / child timetable | `my-tt-title`, `my-tt-print`, `week-prev`, `week-next`, `week-today`, `week-range`, `tt-view`, `tt-week`, `tt-days`, `tt-card-{slotId}-{yyyy-MM-dd}`, `tt-ex-{slotId}-{yyyy-MM-dd}` |

**Keyboard move (M):** focus a cell (`tt-cell-0-1`), press `M` (or `Enter`), arrows, `Enter` to drop, `Escape` to cancel. The pick-up key is matched by `event.code` (`KeyM`), so it works on an Arabic layout; `F2` opens the lesson menu.

## Attendance and excuses (demo 3)
| Page / control | Test id |
|---|---|
| Today `/attendance`: sessions, date, unit picker, switch | `attendance-sessions`, `attendance-date`, `attendance-unit`, `attendance-unit-mode` |
| Session card: state, progress, primary action, history, proposal, locked, substitute | `session-state-{id}`, `session-progress-{id}`, `session-primary-{id}`, `session-history-{id}`, `session-proposal-{id}`, `session-locked-{id}`, `session-substitute-{id}` |
| History drawer | `history-drawer`, `history-list`, `history-item`, `history-close` |
| Take `/attendance/sessions/{id}`: roster, row, status button, tick | `att-roster`, `att-row-{studentId}`, `att-{studentId}-{Present\|Absent\|Late\|Excused}`, `att-tick-{studentId}-{status}` |
| Take: mark all, undo, next unmarked, search, note, save, summary | `att-mark-all`, `att-mark-all-undo`, `att-next`, `att-search`, `att-note-add-{studentId}`, `att-note-{studentId}`, `att-save`, `att-summary`, `att-count-{status}`, `att-count-unmarked` |
| Take: states | `att-future`, `att-window-closed`, `att-read-only`, `att-draft-banner`, `att-draft-restore`, `att-conflict`, `att-save-error`, `att-save-retry`, `att-saved`, `att-reason` |
| Live proposal | `proposal-banner`, `proposal-unavailable`, `proposal-hint-{studentId}`, `unmatched`, `unmatched-select-{participantId}`, `unmatched-save` |
| Own / child attendance | `own-title`, `own-stats`, `own-percent`, `own-table`, `own-row-{sessionId}`, `own-explain-{sessionId}`, `own-submit-excuse`, `own-excuses-link`, `own-guardian-sentence` |
| Excuse dialog | `excuse-policy-hint`, `excuse-from`, `excuse-to`, `excuse-sessions`, `excuse-reason`, `excuse-text`, `excuse-file`, `excuse-file-error`, `excuse-submit` |
| My excuses | `excuses-title`, `excuses-list`, `excuse-{id}`, `excuse-status-{id}`, `excuse-withdraw-{id}`, `excuse-history-{id}` |
| Review queue `/excuses/review` | `review-page-title`, `review-filter-status`, `review-filter-unit`, `review-select-{id}`, `review-open-{id}`, `review-status-{id}`, `bulk-approve`, `bulk-confirm` |
| Review drawer | `review-drawer`, `review-comment`, `review-approve`, `review-reject`, `review-escalate`, `review-error`, `attachment-image`, `attachment-download`, `escalate-target`, `escalate-send` |
| Alerts `/attendance/alerts` | `alerts-title`, `alerts-active-only`, `alert-formula-{id}`, `alert-status-{id}`, `alert-since-{id}` |
| Reports `/attendance/reports` | `reports-title`, `reports-kind-student`, `reports-kind-class`, `reports-class`, `reports-table`, `reports-asof`, `reports-export` |

Keyboard on the take page: digits `1`-`4` set the focused row (by `event.code`, so any layout works), arrows move inside a row's radio group.

---

# M5 FE1 pages: `data-testid` map for the E2E demos (demo 1: weights, manual score, publish)

Section owned by FE1. `{id}` is a GUID. Grid cells also carry `data-r` / `data-c` (row and column index, the key map's coordinates) and `data-state` (`Event`, `Manual`, `Override`, `NewerScoreExists`, `Excused`, `Empty`). Columns of a gradebook are each category's items then its result column, then Final and Band.

## Gradebook `/class-courses/{id}/gradebook?termId=`
| Control | Test id |
|---|---|
| Tab, title, card, forbidden, load error and retry, locked banner | `tab-gradebook`, `gradebook-title`, `gradebook-card`, `gradebook-forbidden`, `gradebook-error`, `gradebook-retry`, `gradebook-locked` |
| Term picker, one-term label, weights chip, settings button, pager | `term-picker`, `term-{termId}`, `term-chip`, `weights-chip`, `gradebook-settings`, `gradebook-paging` |
| Empty states | `gradebook-no-students`, `gradebook-enrollment-link`, `gradebook-no-items` |
| Grid, wrapper, legend and key hint (the grid's `aria-describedby`) | `gradebook-grid`, `gradebook-grid-wrap`, `gradebook-legend`, `gradebook-hint` (`data-mode` on the grid: `nav` or `edit`) |
| Headers | `category-{categoryId}`, `item-{itemId}`, `item-state-{itemId}` |
| Row, student button, withdrawn marker | `grade-row-{studentId}`, `student-{studentId}`, `withdrawn-{studentId}` |
| Score cell, its input while editing | `cell-{studentId}-{itemId}`, `edit-{studentId}-{itemId}` |
| Results from the server | `catres-{studentId}-{categoryId}`, `final-{studentId}`, `band-{studentId}` |
| Live regions (polite saves, assertive rejections) | `grid-live-polite`, `grid-live-assertive` |
| Cell menu (Shift+F10) | `cell-menu`, `menu-override`, `menu-excuse`, `menu-clear`, `menu-keep`, `menu-drop` |
| Override dialog | `override-about`, `override-points`, `override-reason`, `override-error`, `dialog-save`, `dialog-cancel` |
| Student preview drawer | `student-drawer`, `drawer-title`, `drawer-note`, `drawer-items`, `drawer-item-{itemId}`, `drawer-final`, `drawer-empty`, `drawer-error`, `drawer-close` |
| Phone list (below 600 px; the grid is not in the DOM) | `student-first-list`, `phone-note`, `student-search`, `student-search-none`, `student-prev`, `student-next`, `student-name`, `student-position`, `phone-item-{itemId}`, `phone-edit-{itemId}`, `phone-override-{itemId}`, `phone-retry-{itemId}`, `phone-final`, `phone-band`, `phone-preview`, `phone-withdrawn` |

**Keys (grid):** arrows move, Home / End / Ctrl+Home jump, Enter or F2 edits (then Enter saves and stays, Tab saves and moves on, Esc cancels), Shift+F10 or the menu key opens the cell menu, Tab in navigation mode leaves the grid. Keys are matched by `event.key`.

## Settings panel (the gear in the header)
| Control | Test id |
|---|---|
| Panel, error, categories block | `gradebook-config`, `config-error`, `config-categories` |
| Category row n, names, weight, up, down, remove | `cat-row-{n}`, `cat-name-ar-{n}`, `cat-name-en-{n}`, `cat-weight-{n}`, `cat-up-{n}`, `cat-down-{n}`, `cat-remove-{n}` |
| Add, apply template, save categories | `cat-add`, `cat-template`, `cat-save` |
| Template dialog | `template-option-{templateId}`, `template-copy-note`, `templates-none`, `dialog-save` |
| Scale and calculation | `config-settings`, `config-scale`, `config-places`, `config-method`, `config-missing`, `config-save` |
| Items table, add, edit, delete, "from an activity" label | `config-items`, `config-item-{itemId}`, `item-add`, `item-edit-{itemId}`, `item-delete-{itemId}`, `item-auto-{itemId}` |
| Item dialog | `item-title-ar`, `item-title-en`, `item-category`, `item-max`, `item-error` |
| Uncategorised alert | `uncategorised-alert`, `uncategorised-target`, `uncategorised-move` |

## Grading scales `/grading/scales` and weight templates `/grading/weight-templates`
| Control | Test id |
|---|---|
| Scales: title, unit, new, preset menu and entries, table, empty, forbidden | `scales-title`, `scales-unit`, `scale-new`, `preset-menu`, `preset-{seedKey}`, `scales-table`, `scales-empty`, `scales-forbidden` |
| Scale row, chips | `scale-{id}`, `scale-default-{id}`, `scale-provisional-{id}`, `scale-update-{id}`, `scale-inherited-{id}`, `scale-inuse-{id}` |
| Scale row actions | `scale-edit-{id}`, `scale-confirm-{id}`, `scale-update-action-{id}`, `scale-makedefault-{id}`, `scale-delete-{id}` |
| Scale dialog | `scale-name-ar`, `scale-name-en`, `scale-kind`, `scale-error`, `scale-readonly`, `band-editor`, `band-add`, `band-{n}`, `band-code-{n}`, `band-min-{n}`, `band-max-{n}`, `band-points-{n}`, `band-label-ar-{n}`, `band-label-en-{n}`, `band-desc-ar-{n}`, `band-desc-en-{n}`, `band-remove-{n}` |
| Coverage and server problems | `band-coverage`, `band-coverage-ok`, `band-coverage-issues`, `band-issue-{Gap\|Overlap\|StartsAbove\|EndsBelow\|Inverted}`, `band-server-issues`, `band-server-{Kind}`, `band-problem-{n}` |
| Templates: title, new, table, row, total, edit, delete, empty | `templates-title`, `template-new`, `templates-table`, `template-{id}`, `template-total-{id}`, `template-edit-{id}`, `template-delete-{id}`, `templates-empty` |
| Template dialog | `template-name-ar`, `template-name-en`, `template-error`, `template-readonly`, `weights-editor`, `weight-row-{n}`, `weight-name-ar-{n}`, `weight-name-en-{n}`, `weight-value-{n}`, `weight-remove-{n}`, `weight-add`, `weights-total`, `weights-hint` |
| Shared | `dialog-save`, `dialog-cancel`, `confirm-yes`, `confirm-no` |
