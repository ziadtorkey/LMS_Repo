// Moves keyboard focus to an element by id (a heading after a page or module change, the "done" state after an action replaced the
// button that had focus). The element carries tabindex="-1" so it can take focus without joining the tab order.
window.lmsFocusById = function (id) {
    const el = document.getElementById(id);
    if (el) el.focus({ preventScroll: false });
};
