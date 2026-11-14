// Message thread: keeps the reading position. Focus and scrolling only; no message text passes through here.
export function scrollToEnd(selector) {
    const el = document.querySelector(selector);
    if (el) el.scrollTop = el.scrollHeight;
}
