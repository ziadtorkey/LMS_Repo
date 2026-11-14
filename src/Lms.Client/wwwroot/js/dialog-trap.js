// Explicit focus handling of a dialog (M7): focus moves into the dialog when it opens, Tab and Shift+Tab stay inside it, and focus goes back to the control
// that opened it when it closes. MudBlazor moves focus in but neither traps nor returns it reliably, and the app-wide dialog-focus.js only restores it after the fact.
const FOCUSABLE = 'input:not([disabled]), button:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])';

export function trap(root) {
    const opener = document.activeElement;
    const items = () => Array.from(root.querySelectorAll(FOCUSABLE)).filter(e => e.offsetParent !== null || e === document.activeElement);
    const onKey = function (e) {
        if (e.key !== 'Tab') return;
        const list = items();
        if (list.length === 0) { e.preventDefault(); return; }
        const first = list[0], last = list[list.length - 1];
        if (e.shiftKey && (document.activeElement === first || !root.contains(document.activeElement))) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && (document.activeElement === last || !root.contains(document.activeElement))) { e.preventDefault(); first.focus(); }
    };
    root.addEventListener('keydown', onKey);
    const start = root.querySelector('input:not([disabled])') || items()[0];
    if (start) start.focus();
    return { root, opener, onKey };
}

export function release(handle) {
    if (!handle) return;
    handle.root.removeEventListener('keydown', handle.onKey);
    const target = handle.opener;
    if (target && document.contains(target)) setTimeout(function () { target.focus(); }, 0);
}
