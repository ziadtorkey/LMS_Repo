/* LMS SCORM prelude (M4b Task 10, Design A). Runs first in every page of a SCORM package inside the sandboxed frame.
   Defines window.API (SCORM 1.2) and window.API_1484_11 (2004) on the page's own window, answers from the local data model at once (the
   initial state is in <script id="lms-scorm-data">), and queues every change for the app page: commits are numbered (seq) and delivered over
   the private MessagePort once the app hands it over. Nothing here reads a cookie, storage or the parent. */
(function () {
  'use strict';
  var data = {};
  try { data = JSON.parse(document.getElementById('lms-scorm-data').textContent) || {}; } catch (e) { data = {}; }
  var version = data.version === 'Scorm2004' ? 'Scorm2004' : 'Scorm12';
  var model = Object.assign({}, data.model || {});
  var seq = typeof data.seq === 'number' ? data.seq : 0;
  var queue = [], dirty = {}, port = null, initialized = false, finished = false, lastError = '0';

  function flush() {
    if (!port) { return; }
    while (queue.length) { port.postMessage(queue.shift()); }
  }
  function enqueue(format, changes, final) {
    seq += 1;
    queue.push({ type: final ? 'finish' : 'commit', seq: seq, format: format, data: changes });
    flush();
  }
  function takeDirty() { var d = dirty; dirty = {}; return d; }

  window.addEventListener('message', function (event) {
    // The app page transfers one MessagePort; nothing else is trusted (the origin of this frame is opaque).
    if (event.data && event.data.type === 'lms-port' && event.ports && event.ports[0] && !port) {
      port = event.ports[0];
      port.onmessage = function () { /* the app only listens */ };
      flush();
    }
  });
  window.addEventListener('pagehide', function () { if (initialized && !finished && Object.keys(dirty).length) { enqueue('Scorm', takeDirty(), false); } });

  function get(key) {
    lastError = '0';
    return Object.prototype.hasOwnProperty.call(model, key) ? String(model[key]) : '';
  }
  function set(key, value) {
    if (!initialized || finished) { lastError = '301'; return 'false'; }
    lastError = '0';
    model[key] = String(value);
    dirty[key] = String(value);
    return 'true';
  }
  function commit() {
    if (!initialized || finished) { lastError = '301'; return 'false'; }
    var d = takeDirty();
    if (Object.keys(d).length) { enqueue('Scorm', d, false); }
    return 'true';
  }
  function finish() {
    if (!initialized || finished) { lastError = '301'; return 'false'; }
    enqueue('Scorm', takeDirty(), true);
    finished = true;
    return 'true';
  }
  function init() { if (initialized) { lastError = '101'; return 'false'; } initialized = true; lastError = '0'; return 'true'; }

  var errors = { '0': 'No error', '101': 'General exception', '301': 'Not initialized' };
  var common = {
    GetLastError: function () { return lastError; },
    GetErrorString: function (code) { return errors[String(code)] || ''; },
    GetDiagnostic: function (code) { return errors[String(code)] || ''; }
  };
  var api12 = Object.assign({
    LMSInitialize: function () { return init(); }, LMSFinish: function () { return finish(); },
    LMSGetValue: get, LMSSetValue: set, LMSCommit: function () { return commit(); },
    LMSGetLastError: common.GetLastError, LMSGetErrorString: common.GetErrorString, LMSGetDiagnostic: common.GetDiagnostic
  });
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
