// Focus management for the gradebook grid (L24). It never touches data: it stops the page from scrolling, tabbing or opening the browser menu on the keys
// the .NET key map handles, and it moves the browser focus to the cell .NET chose. The key map itself is GridKeyboard.cs (tested without a browser).
const NAVIGATION = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'Enter', 'F2', 'ContextMenu'];
const EDITING = ['Enter', 'Tab', 'Escape', 'F2'];

export function attach(table) {
    if (table.__lmsGrid) return;
    table.__lmsGrid = true;
    table.addEventListener('keydown', e => {
        const editing = table.dataset.mode === 'edit';
        const inMenu = e.target.closest && e.target.closest('[role=menu]');
        if (inMenu) return; // the cell menu keeps its own keys
        const handled = editing ? EDITING.includes(e.key) : (NAVIGATION.includes(e.key) || (e.key === 'F10' && e.shiftKey));
        if (handled && !e.altKey) e.preventDefault();
    }, true);
}

// The cell menu is a popover outside the table: its first item gets the focus when it opens, the arrows move between its items, and Esc (handled in .NET)
// closes it and returns the focus to the cell.
document.addEventListener('keydown', e => {
    const menu = e.target.closest && e.target.closest('[role=menu]');
    if (!menu || (e.key !== 'ArrowDown' && e.key !== 'ArrowUp')) return;
    const items = [...menu.querySelectorAll('[role=menuitem]')];
    const at = items.indexOf(document.activeElement);
    const next = items[(at + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length];
    if (next) { e.preventDefault(); next.focus(); }
});

export function focusMenu() {
    document.querySelector('[role=menu] [role=menuitem]')?.focus();
}

export function focusCell(table, row, col, edit) {
    const cell = table.querySelector(`[data-r="${row}"][data-c="${col}"]`);
    if (!cell) return;
    const target = edit ? (cell.querySelector('input') ?? cell) : cell;
    target.focus({ preventScroll: true });
    // The wrapper's scroll-padding keeps the focused cell clear of the sticky header rows and the student column (WCAG 2.4.11).
    cell.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    if (edit && target.select) target.select();
}
