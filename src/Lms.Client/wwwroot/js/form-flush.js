// Commits a pending debounced field before a form is saved. MudBlazor commits a debounced text field at once when it loses focus,
// so blurring the focused field (and letting its event reach .NET) is enough. Clicking or tabbing to Save already does that;
// this covers a save that starts without a blur (a programmatic or keyboard-triggered submit).
window.lmsFormFlush = async function () {
    const active = document.activeElement;
    if (active && active !== document.body && typeof active.blur === 'function' && /^(INPUT|TEXTAREA)$/.test(active.tagName)) {
        active.blur();
        await new Promise(resolve => setTimeout(resolve, 0));
    }
};
