# Spike S3: timetable builder interaction (M5 Task 31, #616 H9)

Author FE2. Prototype on mock data (`SpikeTimetable`), page `/dev/timetable-spike`, components `TimetableGrid` and `MoveToMenu`, helper `wwwroot/js/timetable-drag.js`. The real conflict answer comes from the server (wave 2); the mock only stands in for it.

## Verified (real browser, standalone client in fake-API mode, plus 11 bUnit tests)
- **Pointer drag** with the mouse moves a lesson (Mathematics to Wednesday, period 4) through the same `MoveToAsync` path as the keyboard and the menu.
- **Keyboard** works in the Arabic (RTL) grid: M picks up, ArrowLeft goes to the next day (arrows follow what the user sees), Enter drops, focus stays on the dropped cell; the live region announces the result.
- **RTL:** the table takes `dir` from the culture, so the first working day is on the right; day order in the DOM stays logical; times stay left to right.
- **Conflicts:** Block refuses and names the lesson in the way (class clash) or the teacher clash; Warn saves and shows a "Clash" chip with text on the card.
- **Touch logic** (synthetic pointer events with `pointerType: touch` at 375 px): a quick swipe does not start a drag; a 300 ms hold does, marks the drop cell and moves the lesson.

## Not verified (needs a real device; do it in Task 31's Playwright run)
- Real touch scrolling versus drag: the hold-then-drag relies on a non-passive `touchmove` listener that cancels scrolling once the hold fired. It must be checked on iOS Safari and Android Chrome.
- A screen reader pass over `role="grid"` with the roving tabindex.

## Findings
1. **Drag is only an accelerator.** Keyboard and the "Move to" menu are complete on their own; keep them as required features.
2. **Phones need the day view, not this grid.** At 375 px the 640 px grid scrolls inside its wrapper (no page overflow) and shows two days. Task 32 builds the day list; the builder stays desktop and tablet.
3. **Arrow keys need JS** (to prevent the page scrolling without blocking Tab), so `timetable-drag.js` forwards keys; all state stays in .NET.
4. **Physical CSS slipped in once:** the lesson card's padding used physical values and the move button overlapped the title in RTL; fixed with `padding-inline`. Keep logical properties.
5. **The client never predicts conflicts**; the grid shows only what the server returned (ids of the clashing lessons).
6. Slot cards need a larger touch target than 28 px for the move button (44 px on touch) in Task 31.
