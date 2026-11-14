// Removes every attendance draft from localStorage (called on sign-out). Drafts hold only ids and statuses, but they are still removed.
window.lmsAttendanceDrafts = {
    clearAll: function (prefix) {
        try {
            Object.keys(localStorage).filter(function (k) { return k.indexOf(prefix) === 0; }).forEach(function (k) { localStorage.removeItem(k); });
        } catch (e) { /* storage may be blocked */ }
    }
};
