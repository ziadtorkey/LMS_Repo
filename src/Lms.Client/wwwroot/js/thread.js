// Message thread: keeps the reading position. Focus and scrolling only; no message text passes through here.
export function scrollToEnd(selector) {
    const el = document.querySelector(selector);
    if (el) el.scrollTop = el.scrollHeight;
}

// True when the reader is at (or near) the end: a new message then follows the end; otherwise the position is kept and a jump control is offered.
export function isNearBottom(selector, threshold = 80) {
    const el = document.querySelector(selector);
    return !el || el.scrollHeight - el.scrollTop - el.clientHeight <= threshold;
}
