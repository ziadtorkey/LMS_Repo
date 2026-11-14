// Attendance taking page: row keys and focus. All marks stay in .NET; this module only
//  - turns the digits 1 to 4 (matched by event.code, so any keyboard layout works) into "set this row", active only while a row has focus,
//  - moves focus inside a row's radio group with the arrow keys (they follow what the user sees, so they are mirrored in RTL),
//  - focuses a row on request ("next unmarked").
const DIGITS = { Digit1: 1, Digit2: 2, Digit3: 3, Digit4: 4, Numpad1: 1, Numpad2: 2, Numpad3: 3, Numpad4: 4 };

export function attach(root, dotNetRef) {
    function onKey(e) {
        if (e.altKey || e.ctrlKey || e.metaKey) return;
        const row = e.target.closest('[data-row]');
        if (!row) return;
        // Typing a number in a note field must stay typing.
        if (/^(INPUT|TEXTAREA)$/.test(e.target.tagName)) return;

        if (e.code in DIGITS) {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('OnDigit', row.dataset.row, DIGITS[e.code]).catch(() => { });
            return;
        }

        if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
            const radios = Array.from(row.querySelectorAll('[role=radio]:not([disabled])'));
            const at = radios.indexOf(e.target.closest('[role=radio]'));
            if (at < 0 || radios.length === 0) return;
            e.preventDefault();
            const rtl = getComputedStyle(row).direction === 'rtl';
            const step = (e.key === 'ArrowRight') !== rtl ? 1 : -1;
            const next = radios[(at + step + radios.length) % radios.length];
            next.focus();
            next.click(); // a radio group selects as it moves
        }
    }

    root.addEventListener('keydown', onKey);
    return { dispose() { root.removeEventListener('keydown', onKey); } };
}

export function focusRow(root, studentId) {
    const row = root.querySelector(`[data-row="${studentId}"]`);
    if (!row) return;
    const target = row.querySelector('[role=radio][aria-checked=true]') || row.querySelector('[role=radio]');
    if (target) { target.focus(); row.scrollIntoView({ block: 'center' }); }
}
