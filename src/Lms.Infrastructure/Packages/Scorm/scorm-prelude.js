/* LMS SCORM prelude (M4b Task 10, Design A). Runs first in every page of a SCORM package inside the sandboxed frame, after the content-side
   port shim (_lms/port-shim.js, which owns the private MessagePort handshake, the sequence numbers and the bounded queue).
   Defines window.API (SCORM 1.2) and window.API_1484_11 (2004) on the page's own window, answers from the local data model at once (the initial
   state is in <script id="lms-scorm-data">), and hands every change to window.lmsPort. Nothing here reads a cookie, storage or the parent. */
(function () {
  'use strict';
  var data = {};
  try { data = JSON.parse(document.getElementById('lms-scorm-data').textContent) || {}; } catch (e) { data = {}; }
  var version = data.version === 'Scorm2004' ? 'Scorm2004' : 'Scorm12';
  var model = Object.assign({}, data.model || {});
  var dirty = {}, initialized = false, finished = false, lastError = '0';
  var MAX_VALUE = 4096; // bytes per value: the port contract (and the server) refuse a longer one
  var MAX_CHUNK = 6000; // bytes of JSON per message: the port contract caps a message at 8 KB, the envelope needs the rest

  // A resumed attempt continues the server's sequence: the data block carries the last sequence number the server applied.
  if (window.lmsPort && typeof window.lmsPort.resume === 'function' && typeof data.seq === 'number') { window.lmsPort.resume(data.seq); }

  function size(text) { return new TextEncoder().encode(text).length; }

  // Sends the change set as one or more messages of at most MAX_CHUNK bytes; the final message (when finishing) is a finish.
  function send(changes, final) {
    var port = window.lmsPort;
    if (!port) { return; }
    var chunks = [], current = {}, bytes = 2;
    Object.keys(changes).forEach(function (key) {
      var pair = size(JSON.stringify(key)) + size(JSON.stringify(changes[key])) + 2;
      // A value over the contract is dropped on its own: the rest of the change set and the finish still go out (a refused SetValue cannot get here).
      if (size(String(changes[key])) > MAX_VALUE || pair > MAX_CHUNK) { return; }
      if (bytes + pair > MAX_CHUNK && Object.keys(current).length) { chunks.push(current); current = {}; bytes = 2; }
      current[key] = changes[key];
      bytes += pair;
    });
    if (Object.keys(current).length || final) { chunks.push(current); }
    chunks.forEach(function (chunk, i) {
      if (final && i === chunks.length - 1) { port.finish('Scorm', chunk); } else { port.commit('Scorm', chunk); }
    });
  }
  function takeDirty() { var d = dirty; dirty = {}; return d; }

  window.addEventListener('pagehide', function () { if (initialized && !finished && Object.keys(dirty).length) { send(takeDirty(), false); } });

  function get(key) {
    lastError = '0';
    return Object.prototype.hasOwnProperty.call(model, key) ? String(model[key]) : '';
  }
  function set(key, value) {
    if (!initialized || finished) { lastError = '301'; return 'false'; }
    if (size(String(value)) > MAX_VALUE) { lastError = '405'; return 'false'; } // out of range: the platform keeps at most 4 KB per value
    lastError = '0';
    model[key] = String(value);
    dirty[key] = String(value);
    return 'true';
  }
  function commit() {
    if (!initialized || finished) { lastError = '301'; return 'false'; }
    var d = takeDirty();
    if (Object.keys(d).length) { send(d, false); }
    return 'true';
  }
  function finish() {
    if (!initialized || finished) { lastError = '301'; return 'false'; }
    send(takeDirty(), true);
    finished = true;
    return 'true';
  }
  function init() { if (initialized) { lastError = '101'; return 'false'; } initialized = true; lastError = '0'; return 'true'; }

  var errors = { '0': 'No error', '101': 'General exception', '301': 'Not initialized', '405': 'Value out of range (too long)' };
  var common = {
    GetLastError: function () { return lastError; },
    GetErrorString: function (code) { return errors[String(code)] || ''; },
    GetDiagnostic: function (code) { return errors[String(code)] || ''; }
  };
  var api12 = {
    LMSInitialize: function () { return init(); }, LMSFinish: function () { return finish(); },
    LMSGetValue: get, LMSSetValue: set, LMSCommit: function () { return commit(); },
    LMSGetLastError: common.GetLastError, LMSGetErrorString: common.GetErrorString, LMSGetDiagnostic: common.GetDiagnostic
  };
  var api2004 = {
    Initialize: function () { return init(); }, Terminate: function () { return finish(); },
    GetValue: get, SetValue: set, Commit: function () { return commit(); },
    GetLastError: common.GetLastError, GetErrorString: common.GetErrorString, GetDiagnostic: common.GetDiagnostic
  };
  // Defined non-writable before any package script runs: the prelude's object wins over one the package defines, and a package that
  // catches the SecurityError of the parent walk still finds it on its own window.
  [['API', api12], ['API_1484_11', api2004]].forEach(function (p) {
    try { Object.defineProperty(window, p[0], { value: p[1], writable: false, configurable: false, enumerable: true }); } catch (e) { /* already defined */ }
  });
})();
