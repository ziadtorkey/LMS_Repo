// The message bridge of the package player (M4b Task 17, ADR 0005 section 4). The ONLY script that touches package messages.
//
// The package runs in a sandboxed frame (sandbox="allow-scripts", never allow-same-origin), so its origin is opaque: every window
// message from it arrives with event.origin === "null" and origin carries no trust. The bridge therefore trusts exactly one thing:
// the MessageChannel port it transfers to the frame after the frame's "load" event. Window messages are counted and dropped, never
// acted on. Port messages are untrusted data: their shape and size are checked here and again in .NET, they are never rendered as
// HTML and never used as an id; the attempt id comes from the launch response, not from a message.
//
// Handshake (page -> frame, once per load): window.postMessage({ lms: 'package-port', v: 1 }, '*', [port2]). From an opaque origin the
// only possible targetOrigin is '*', so nothing sensitive is ever posted: the handshake carries no token, user id, name or answers.
// Port message (frame -> page): { type: 'commit' | 'finish', seq: <non-negative integer>, format: 'Scorm' | 'Xapi', data: { key: 'value' } }.
(function () {
    const MAX_MESSAGE_BYTES = 8 * 1024;
    const MAX_VALUE_BYTES = 4 * 1024;
    const MAX_KEYS = 256;
    const bridges = {};
    let nextId = 1;

    function byteLength(text) {
        return new TextEncoder().encode(text).length;
    }

    // Returns the clean message, or null when it is not one the contract allows.
    function validate(raw) {
        if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return null;
        if (raw.type !== 'commit' && raw.type !== 'finish') return null;
        if (!Number.isSafeInteger(raw.seq) || raw.seq < 0) return null;
        const format = raw.format === 'Xapi' ? 'Xapi' : 'Scorm';
        const data = {};
        if (raw.data !== undefined && raw.data !== null) {
            if (typeof raw.data !== 'object' || Array.isArray(raw.data)) return null;
            const keys = Object.keys(raw.data);
            if (keys.length > MAX_KEYS) return null;
            for (const key of keys) {
                const value = raw.data[key];
                if (typeof value !== 'string' || byteLength(value) > MAX_VALUE_BYTES) return null;
                data[key] = value;
            }
        }
        const clean = { type: raw.type, seq: raw.seq, format: format, data: data };
        if (byteLength(JSON.stringify(clean)) > MAX_MESSAGE_BYTES) return null;
        return clean;
    }

    window.lmsPackageBridge = {
        validate: validate,

        // Starts a bridge for one player. The port is transferred when the frame has loaded (immediately if it already has).
        // Returns the bridge id; dispose(id) removes every listener and closes the port.
        attach: function (iframe, dotNetRef) {
            const id = nextId++;
            const bridge = { iframe: iframe, ref: dotNetRef, port: null, dropped: 0 };

            // Window messages are never acted on: counted and dropped. (origin is "null" and means nothing.)
            bridge.onWindowMessage = function (event) {
                bridge.dropped++;
                dotNetRef.invokeMethodAsync('OnWindowMessageDropped').catch(function () {});
            };
            window.addEventListener('message', bridge.onWindowMessage);

            bridge.onPageHide = function () { dotNetRef.invokeMethodAsync('OnPageHide').catch(function () {}); };
            window.addEventListener('pagehide', bridge.onPageHide);

            const transfer = function () {
                if (bridge.port) { bridge.port.close(); bridge.port = null; }
                const channel = new MessageChannel();
                bridge.port = channel.port1;
                channel.port1.onmessage = function (event) {
                    const message = validate(event.data);
                    if (message === null) return; // malformed or oversize: dropped, nothing reaches .NET
                    dotNetRef.invokeMethodAsync('OnPortMessage', message.type, message.seq, message.format, message.data).catch(function () {});
                };
                iframe.contentWindow.postMessage({ lms: 'package-port', v: 1 }, '*', [channel.port2]);
            };
            bridge.onLoad = transfer;
            iframe.addEventListener('load', bridge.onLoad);
            bridges[id] = bridge;
            return id;
        },

        // Posts a non-sensitive control message over the port (for example { control: 'pause' }).
        control: function (id, name) {
            const bridge = bridges[id];
            if (bridge && bridge.port) bridge.port.postMessage({ control: String(name) });
        },

        dispose: function (id) {
            const bridge = bridges[id];
            if (!bridge) return;
            delete bridges[id];
            window.removeEventListener('message', bridge.onWindowMessage);
            window.removeEventListener('pagehide', bridge.onPageHide);
            bridge.iframe.removeEventListener('load', bridge.onLoad);
            if (bridge.port) { bridge.port.onmessage = null; bridge.port.close(); bridge.port = null; }
        },

        count: function () { return Object.keys(bridges).length; },
    };

    // The container (toolbar and frame) goes full screen, so Exit and Full screen stay reachable.
    window.lmsPackageFullscreen = {
        toggle: async function (container) {
            if (document.fullscreenElement) await document.exitFullscreen();
            else if (container && container.requestFullscreen) await container.requestFullscreen();
            return !!document.fullscreenElement;
        },
        exit: async function () { if (document.fullscreenElement) await document.exitFullscreen(); },
    };
})();
