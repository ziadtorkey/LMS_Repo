// Returns keyboard focus to the control that opened a dialog (or the attendance history drawer) once it closes (MudBlazor moves
// focus in but does not give it back). Works for every dialog of the app; nothing to wire per page.
(function () {
    let last = null;
    let opener = null;
    let open = false;
    const OVERLAY = '.mud-dialog, .lms-history-drawer.mud-drawer--open';
    document.addEventListener('focusin', function (e) {
        if (!e.target.closest || !e.target.closest(OVERLAY)) last = e.target;
    }, true);
    new MutationObserver(function () {
        const has = !!document.querySelector(OVERLAY);
        if (has && !open) {
            open = true;
            opener = last;
        } else if (!has && open) {
            open = false;
            const target = opener;
            opener = null;
            if (target && document.contains(target)) setTimeout(function () { target.focus(); }, 0);
        }
    }).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
})();
