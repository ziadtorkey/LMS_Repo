// Web Push for this browser (M6 Task 26). Nothing here asks for permission by itself: the page calls subscribe only after a click (L11).
window.lmsPush = (function () {
    const consentKey = "lms.push.consent";

    function supported() {
        return "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
    }

    function urlBase64ToUint8Array(base64) {
        const padding = "=".repeat((4 - (base64.length % 4)) % 4);
        const raw = atob((base64 + padding).replace(/-/g, "+").replace(/_/g, "/"));
        return Uint8Array.from(raw, (c) => c.charCodeAt(0));
    }

    function encode(buffer) {
        return btoa(String.fromCharCode(...new Uint8Array(buffer))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }

    async function registration() {
        return (await navigator.serviceWorker.getRegistration("push-sw.js")) || (await navigator.serviceWorker.register("push-sw.js"));
    }

    return {
        supported,
        permission: () => (supported() ? Notification.permission : "unsupported"),
        async hasSubscription() {
            if (!supported()) return false;
            const reg = await navigator.serviceWorker.getRegistration("push-sw.js");
            return !!reg && !!(await reg.pushManager.getSubscription());
        },
        // Called from a click handler only: this is what makes the browser show its permission prompt.
        async subscribe(publicKey) {
            if (!supported()) return null;
            if ((await Notification.requestPermission()) !== "granted") return null;
            const reg = await registration();
            const sub = (await reg.pushManager.getSubscription()) || (await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(publicKey) }));
            return { endpoint: sub.endpoint, p256dh: encode(sub.getKey("p256dh")), auth: encode(sub.getKey("auth")) };
        },
        async unsubscribe() {
            const reg = await navigator.serviceWorker.getRegistration("push-sw.js");
            const sub = reg && (await reg.pushManager.getSubscription());
            if (sub) await sub.unsubscribe();
        },
        hasConsent: () => localStorage.getItem(consentKey) !== null,
        recordConsent: () => localStorage.setItem(consentKey, new Date().toISOString()),
    };
})();
