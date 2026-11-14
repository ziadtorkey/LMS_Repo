// Desktop sidebar: full (260 px) or the 72 px icon rail. lms.nav.rail = "collapsed" | "expanded"; storage may be blocked.
window.lmsNav = {
    isRailCollapsed: function () {
        try { return localStorage.getItem('lms.nav.rail') === 'collapsed'; } catch (e) { return false; }
    },
    setRailCollapsed: function (collapsed) {
        try { localStorage.setItem('lms.nav.rail', collapsed ? 'collapsed' : 'expanded'); } catch (e) { }
    }
};
