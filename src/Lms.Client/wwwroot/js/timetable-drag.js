// Timetable grid interaction helper (M5 spike S3): grid keys and pointer drag. All state and all data stay in .NET; this module only
//  - turns grid keys into calls (so the page can preventDefault for arrows without blocking Tab),
//  - starts a drag with Pointer Events (mouse after a few pixels, touch and pen after a 300 ms hold so a scroll still scrolls),
//  - hands the drop target back as (slot id, day, period).
// Drag is only an accelerator: every move is also possible with the keyboard and the "Move to" menu.
const KEYS = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'Enter', ' ', 'Escape', 'm', 'M', 'F2']);
const HOLD_MS = 300;
const MOVE_PX = 6;

export function attach(grid, dotNetRef) {
    let drag = null;

    function cellAt(x, y) {
        const el = document.elementFromPoint(x, y);
        return el ? el.closest('[data-cell]') : null;
    }

    function clearTargets() {
        grid.querySelectorAll('.lms-tt-drop').forEach(c => c.classList.remove('lms-tt-drop'));
    }

    function cancel() {
        if (!drag) return;
        clearTimeout(drag.timer);
        clearTargets();
        grid.classList.remove('lms-tt-dragging');
        drag = null;
    }

    function begin(d) {
        d.active = true;
        grid.classList.add('lms-tt-dragging');
        try { d.card.setPointerCapture(d.pointerId); } catch (e) { /* the pointer may be gone already */ }
    }

    grid.addEventListener('keydown', e => {
        if (!KEYS.has(e.key) || e.altKey || e.ctrlKey || e.metaKey) return;
        const cell = e.target.closest('[data-cell]');
        if (!cell) return;
        // Space and Enter on a real button (the move menu) keep their normal meaning.
        if ((e.key === 'Enter' || e.key === ' ') && e.target.closest('button[data-menu]')) return;
        e.preventDefault();
        dotNetRef.invokeMethodAsync('OnKey', e.key, Number(cell.dataset.day), Number(cell.dataset.period)).catch(() => { });
    });

    grid.addEventListener('pointerdown', e => {
        const card = e.target.closest('[data-slot]');
        if (!card || e.target.closest('button[data-menu]') || (e.pointerType === 'mouse' && e.button !== 0)) return;
        drag = { card, id: card.dataset.slot, pointerId: e.pointerId, x: e.clientX, y: e.clientY, active: false, touch: e.pointerType !== 'mouse' };
        if (drag.touch) drag.timer = setTimeout(() => { if (drag) begin(drag); }, HOLD_MS);
    });

    grid.addEventListener('pointermove', e => {
        if (!drag || e.pointerId !== drag.pointerId) return;
        if (!drag.active) {
            const moved = Math.hypot(e.clientX - drag.x, e.clientY - drag.y);
            if (drag.touch) { if (moved > MOVE_PX) cancel(); return; } // the finger moved before the hold: it is a scroll
            if (moved < MOVE_PX) return;
            begin(drag);
        }
        e.preventDefault();
        clearTargets();
        cellAt(e.clientX, e.clientY)?.classList.add('lms-tt-drop');
    });

    grid.addEventListener('pointerup', e => {
        if (!drag || e.pointerId !== drag.pointerId) return;
        const d = drag;
        const target = d.active ? cellAt(e.clientX, e.clientY) : null;
        cancel();
        if (target) dotNetRef.invokeMethodAsync('OnDrop', d.id, Number(target.dataset.day), Number(target.dataset.period)).catch(() => { });
    });

    // After the touch hold the finger drags the card, so the page must not scroll under it. This listener has to be non-passive,
    // and works because the hold happened before any movement (the browser has not started a scroll yet).
    grid.addEventListener('touchmove', e => { if (drag && drag.active) e.preventDefault(); }, { passive: false });
    grid.addEventListener('pointercancel', cancel);
    // While a touch drag is active the page must not scroll: touch-action is set by CSS on .lms-tt-dragging.
    grid.addEventListener('contextmenu', e => { if (drag && drag.active) e.preventDefault(); });
    return { dispose: cancel };
}

export function focusCell(grid, day, period) {
    const cell = grid.querySelector(`[data-cell][data-day="${day}"][data-period="${period}"]`);
    if (cell) cell.focus({ preventScroll: false });
}
