// Class course tabs (lms-course-tabs): on a narrow screen the row scrolls inside itself; bring the current tab into view
// without moving the page (only the row scrolls, so the page never jumps on load).
window.lmsTabs = {
    revealCurrent: function (nav) {
        if (!nav) return;
        var current = nav.querySelector('[aria-current="page"]');
        if (!current || nav.scrollWidth <= nav.clientWidth) return;
        var navBox = nav.getBoundingClientRect();
        var box = current.getBoundingClientRect();
        if (box.left < navBox.left) nav.scrollLeft += box.left - navBox.left - 8;
        else if (box.right > navBox.right) nav.scrollLeft += box.right - navBox.right + 8;
    }
};
