// The content side of the private tracking port (M4b Task 9b, ADR 0005 section 4). Served by the content host at
// /p/{token}/_lms/port-shim.js; the SCORM prelude (Task 10) and the H5P bridge (Task 11) load it and call window.lmsPort. It is the
// counterpart of the app page's package-bridge.js and uses exactly its message shape.
//
// Handshake (page -> frame, once per load): window.postMessage({ lms: 'package-port', v: 1 }, '*', [port2]). The frame's origin is opaque,
// so event.origin means nothing; the shim trusts only a message whose source is the parent window and that carries a port. The handshake
// carries no token, user id, name or answer, and nothing about the learner is ever read here.
// Port message (frame -> page): { type: 'commit' | 'finish', seq: <non-negative integer>, format: 'Scorm' | 'Xapi', data: { key: 'value' } }.
// seq rises by one per message (commit and finish share the counter); messages made before the handshake wait in a bounded queue.
(function () {
    'use strict';
    if (window.lmsPort) { return; }

    var MAX_MESSAGE_BYTES = 8 * 1024;
    var MAX_VALUE_BYTES = 4 * 1024;
    var MAX_KEYS = 256;
    var MAX_QUEUE = 256;

    var port = null;
    var queue = [];
    var seq = 0;
    var finished = false;
    var onControl = null;

    function byteLength(text) { return new TextEncoder().encode(text).length; }

    // Returns the clean data map, or null when a value is not a string or is too long for the contract.
    function cleanData(data) {
        var clean = {};
        if (data === undefined || data === null) { return clean; }
        if (typeof data !== 'object' || Array.isArray(data)) { return null; }
        var keys = Object.keys(data);
        if (keys.length > MAX_KEYS) { return null; }
        for (var i = 0; i < keys.length; i++) {
            var value = data[keys[i]];
            if (typeof value !== 'string' || byteLength(value) > MAX_VALUE_BYTES) { return null; }
            clean[keys[i]] = value;
        }
        return clean;
    }

    function send(message) {
        if (port) { port.postMessage(message); return; }
        if (queue.length >= MAX_QUEUE) {
            // Bounded: drop the oldest commit (the next commit carries the newer state); a finish is never dropped.
            for (var i = 0; i < queue.length; i++) {
                if (queue[i].type === 'commit') { queue.splice(i, 1); break; }
            }
        }
        queue.push(message);
    }

    function make(type, format, data) {
        if (finished) { return false; }
        var clean = cleanData(data);
        if (clean === null) { return false; }
        var message = { type: type, seq: seq, format: format === 'Xapi' ? 'Xapi' : 'Scorm', data: clean };
        if (byteLength(JSON.stringify(message)) > MAX_MESSAGE_BYTES) { return false; }
        seq++;
        if (type === 'finish') { finished = true; }
        send(message);
        return true;
    }

    window.addEventListener('message', function (event) {
        var d = event.data;
        if (event.source !== window.parent || !d || d.lms !== 'package-port' || d.v !== 1 || !event.ports || event.ports.length < 1) { return; }
        if (port) { return; } // one port per load: a second handshake is ignored
        port = event.ports[0];
        port.onmessage = function (e) {
            if (onControl && e.data && typeof e.data.control === 'string') { onControl(e.data.control); }
        };
        while (queue.length > 0) { port.postMessage(queue.shift()); }
    });

    window.lmsPort = Object.freeze({
        // Reports changed data; false when the data does not fit the contract (nothing is sent).
        commit: function (format, data) { return make('commit', format, data); },
        // Flushes and ends the run; the page closes the attempt. Nothing is sent after it.
        finish: function (format, data) { return make('finish', format, data); },
        // A resumed attempt continues the server's sequence: the next message gets last + 1 (never lower than the counter already is).
        resume: function (last) { if (typeof last === 'number' && isFinite(last) && last >= -1 && Math.floor(last) + 1 > seq) { seq = Math.floor(last) + 1; } },
        // A non-sensitive control message from the page, for example 'pause'.
        onControl: function (handler) { onControl = typeof handler === 'function' ? handler : null; },
    });
})();
