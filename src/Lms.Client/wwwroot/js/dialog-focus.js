// Returns keyboard focus to the control that opened a dialog once the dialog closes (MudBlazor moves focus into the dialog
// but does not give it back). Works for every dialog of the app; nothing to wire per page.
(function () {
    let last = null;
    let opener = null;
    let open = false;
    document.addEventListener('focusin', function (e) {
        if (!e.target.closest || !e.target.closest('.mud-dialog')) last = e.target;
    }, true);
    new MutationObserver(function () {
        const has = !!document.querySelector('.mud-dialog');
        if (has && !open) {
            open = true;
            opener = last;
        } else if (!has && open) {
            open = false;
            const target = opener;
            opener = null;
            if (target && document.contains(target)) setTimeout(function () { target.focus(); }, 0);
        }
    }).observe(document.body, { childList: true, subtree: true });
})();
