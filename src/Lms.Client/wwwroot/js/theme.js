// Colour scheme preference (lms.theme = "dark" | "light"); without a stored choice, follow the device.
window.lmsTheme = {
    isDark: function () {
        var stored = null;
        try { stored = localStorage.getItem('lms.theme'); } catch (e) { }
        if (stored === 'dark') return true;
        if (stored === 'light') return false;
        return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    },
    set: function (dark) {
        try { localStorage.setItem('lms.theme', dark ? 'dark' : 'light'); } catch (e) { }
        document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
    }
};
document.documentElement.setAttribute('data-theme', window.lmsTheme.isDark() ? 'dark' : 'light');
