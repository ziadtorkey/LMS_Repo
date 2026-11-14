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
