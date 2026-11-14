// Service worker for Web Push. The payload is a short generic notice from the server (no message text); a click opens the link it carries.
self.addEventListener("push", (event) => {
    const data = event.data ? event.data.json() : {};
    event.waitUntil(self.registration.showNotification(data.title || "LMS", { body: data.body || "", data: { url: data.url || "/" }, tag: data.tag }));
});

self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    event.waitUntil(clients.openWindow(event.notification.data.url));
});
