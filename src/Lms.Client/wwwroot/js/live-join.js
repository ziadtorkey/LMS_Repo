// Opens the meeting tab for a live session (M4b Task 15). The tab must be opened synchronously inside the click, before any
// await, or Safari and Firefox block it; the URL is only known after the API answers, so a blank tab is opened first and
// pointed at the URL later. No "noopener" feature here: with it window.open returns null and the tab could neither be
// pointed nor closed. Instead the opener is cleared at once, before the blank tab can run anything.
window.lmsLiveJoin = {
    tabs: {},
    open: function () {
        const tab = window.open('about:blank', '_blank');
        if (!tab) return null;
        try { tab.opener = null; } catch (e) { /* cross-origin: nothing to clear */ }
        const id = (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : String(Date.now()) + Math.random();
        this.tabs[id] = tab;
        return id;
    },
    navigate: function (id, url) {
        const tab = this.tabs[id];
        delete this.tabs[id];
        if (!tab || tab.closed) return false;
        tab.location = url;
        return true;
    },
    close: function (id) {
        const tab = this.tabs[id];
        delete this.tabs[id];
        if (tab && !tab.closed) tab.close();
    }
};
