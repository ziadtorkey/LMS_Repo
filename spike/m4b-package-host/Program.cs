// THROWAWAY spike: one process, two origins. App = http://app.localhost:5101, package host = http://content.localhost:5102.
// Run: dotnet run --project spike/m4b-package-host   then open http://app.localhost:5101/  (add ?csp=eval for the relaxed CSP).
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string AppOrigin = "http://app.localhost:5101";
const string ContentOrigin = "http://content.localhost:5102";
var signingKey = RandomNumberGenerator.GetBytes(32);   // real design: per-environment secret, rotatable
var tracked = new ConcurrentQueue<JsonElement>();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5101", "http://127.0.0.1:5102");
var app = builder.Build();

static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static byte[] UnB64(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '='));

string MakeToken(string packageId, string studentId, TimeSpan life)
{
    var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new { p = packageId, s = studentId, e = DateTimeOffset.UtcNow.Add(life).ToUnixTimeSeconds() }));
    return payload + "." + B64(HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes(payload)));
}

bool TryToken(string token)
{
    var parts = token.Split('.');
    if (parts.Length != 2)
    {
        return false;
    }

    try
    {
        var expected = HMACSHA256.HashData(signingKey, Encoding.ASCII.GetBytes(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(expected, UnB64(parts[1])))
        {
            return false;
        }

        var doc = JsonDocument.Parse(UnB64(parts[0])).RootElement;
        return doc.GetProperty("e").GetInt64() >= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    catch (Exception)
    {
        return false;
    }
}

// Per-host policy: the app host keeps today's strict headers; the package host gets its own.
app.Use(async (ctx, next) =>
{
    var port = ctx.Connection.LocalPort;
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        if (port == 5101)
        {
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] = $"frame-ancestors 'none'; frame-src {ContentOrigin}";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        }
        else
        {
            var relaxed = ctx.Request.Query["csp"] == "eval";
            h["Content-Security-Policy"] =
                "default-src 'none'; script-src 'self' 'unsafe-inline'" + (relaxed ? " 'unsafe-eval'" : string.Empty) +
                "; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self' data:; " +
                $"connect-src 'none'; frame-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors {AppOrigin}";
            h["Referrer-Policy"] = "no-referrer";
            h["Cache-Control"] = "private, no-store";
            h.Remove("X-Frame-Options");
        }

        return Task.CompletedTask;
    });
    await next();
});

// ---------- app origin ----------
app.MapGet("/", (HttpContext ctx) =>
{
    if (ctx.Connection.LocalPort != 5101)
    {
        return Results.NotFound();
    }

    ctx.Response.Cookies.Append("lms_session", "APP-SESSION-SECRET", new CookieOptions { Path = "/", SameSite = SameSiteMode.Lax });
    var csp = ctx.Request.Query["csp"] == "eval" ? "?csp=eval" : string.Empty;
    var token = MakeToken("pkg-1", "student-1", TimeSpan.FromMinutes(10));
    return Results.Content(PlayerPage.Html(ContentOrigin + "/p/" + token + "/index.html" + csp, token), "text/html");
});
app.MapGet("/secret", (HttpContext ctx) => ctx.Connection.LocalPort == 5101 && ctx.Request.Cookies.ContainsKey("lms_session")
    ? Results.Text("TOP-SECRET-APP-DATA")
    : Results.Unauthorized());
app.MapPost("/track", async (HttpContext ctx) =>
{
    using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
    tracked.Enqueue(doc.RootElement.Clone());
    return Results.NoContent();
});
app.MapGet("/tracked", () => Results.Json(tracked.ToArray()));

// ---------- package host (cookie-less: it never sets, reads or needs a cookie) ----------
app.MapGet("/p/{token}/{**file}", (string token, string? file) =>
{
    if (!TryToken(token))
    {
        return Results.NotFound();            // forged, expired or malformed: one identical 404
    }

    return (file ?? "index.html") switch
    {
        "index.html" => Results.Content(PackageFiles.Index, "text/html; charset=utf-8"),
        "shim.js" => Results.Content(PackageFiles.Shim, "text/javascript; charset=utf-8"),
        "api.json" => Results.Content("{\"x\":1}", "application/json"),
        _ => Results.NotFound(),
    };
});

app.Run();

internal static class PlayerPage
{
    public static string Html(string launchUrl, string token) => PlayerTemplate.Replace("__LAUNCH__", launchUrl).Replace("__TOKEN__", token);

    private const string PlayerTemplate = """
<!doctype html><meta charset="utf-8"><title>spike player</title>
<h1>M4b package host spike (app origin)</h1>
<iframe id="f" sandbox="allow-scripts" src="__LAUNCH__" style="width:100%;height:120px;border:1px solid #888"></iframe>
<pre id="result">running...</pre>
<script>
// Secrets the package must never see.
localStorage.setItem('app-secret', 'APP-LOCALSTORAGE-SECRET');
const frame = document.getElementById('f');
const out = { appCookieVisibleHere: document.cookie, windowMessagesIgnored: 0, commitsAccepted: 0, commitsRejected: [], probe: null };
const ALLOWED_KEYS = new Set(['cmi.core.lesson_status','cmi.core.score.raw','cmi.core.score.min','cmi.core.score.max','cmi.suspend_data']);
const MAX_VALUE = 4096, MAX_COMMITS_PER_10S = 10;
const recent = [];

// (1) Anything arriving as a plain window message is dropped: the only trusted channel is the private port below.
window.addEventListener('message', () => { out.windowMessagesIgnored++; });

// (2) Hand a private MessageChannel port to THIS iframe only, once it has loaded.
frame.addEventListener('load', () => {
  const ch = new MessageChannel();
  ch.port1.onmessage = ev => handle(ev.data);
  // targetOrigin must be '*' because the sandboxed frame has an opaque origin ("null"); the target is pinned by contentWindow.
  frame.contentWindow.postMessage({ type: 'init' }, '*', [ch.port2]);
  setTimeout(render, 1800);
});

function reject(why, m) { out.commitsRejected.push(why + ': ' + JSON.stringify(m).slice(0, 60)); }
function handle(m) {
  if (typeof m !== 'object' || m === null || JSON.stringify(m).length > 8192) return reject('shape/size', String(m));
  if (m.type === 'probe.report') { out.probe = m.results; return; }
  if (m.type !== 'scorm.commit') return reject('type', m);
  const now = Date.now(); while (recent.length && now - recent[0] > 10000) recent.shift();
  if (recent.length >= MAX_COMMITS_PER_10S) return reject('rate', m);
  const data = m.data;
  if (typeof data !== 'object' || data === null) return reject('data', m);
  for (const [k, v] of Object.entries(data)) {
    if (!ALLOWED_KEYS.has(k) || typeof v !== 'string' || v.length > MAX_VALUE) return reject('key/value ' + k, m);
  }
  recent.push(now); out.commitsAccepted++;
  // Same-origin POST with the app session: the server (not the package) decides what a commit means.
  fetch('/track', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Launch-Token': '__TOKEN__' }, body: JSON.stringify(data) });
}
function render() { document.getElementById('result').textContent = JSON.stringify(out, null, 2); document.title = 'DONE'; }
</script>
""";
}

internal static class PackageFiles
{
    public const string Shim = """
// SCORM 1.2 runtime shim: lives INSIDE the package document (window.API), talks to the app only over the private port.
let port = null;
const pending = {};
addEventListener('message', e => { if (e.source === parent && e.data && e.data.type === 'init' && e.ports[0] && !port) port = e.ports[0]; });
window.API = {
  LMSInitialize: () => 'true', LMSFinish: () => 'true', LMSGetLastError: () => '0', LMSGetErrorString: () => '', LMSGetDiagnostic: () => '',
  LMSGetValue: k => pending[k] ?? '',
  LMSSetValue: (k, v) => { pending[k] = String(v); return 'true'; },
  LMSCommit: () => { if (port) port.postMessage({ type: 'scorm.commit', data: { ...pending } }); return 'true'; },
};
window.__sendProbe = results => { if (port) port.postMessage({ type: 'probe.report', results }); };
window.__rawPort = () => port;
""";

    public const string Index = """
<!doctype html><meta charset="utf-8"><title>hostile package</title>
<script src="shim.js"></script>
<script>
const results = {};
const violations = [];
document.addEventListener('securitypolicyviolation', e => violations.push(e.violatedDirective + ' <- ' + e.blockedURI));
async function t(name, fn) { try { const r = await fn(); results[name] = 'ALLOWED: ' + String(r).slice(0, 60); } catch (e) { results[name] = 'blocked: ' + (e && e.name); } }
(async () => {
  results.origin = location.origin;
  // wait for the app to hand over the private port
  for (let i = 0; i < 50 && !__rawPort(); i++) await new Promise(r => setTimeout(r, 50));
  await t('read document.cookie', () => JSON.stringify(document.cookie));
  await t('read localStorage', () => localStorage.getItem('app-secret'));
  await t('read sessionStorage', () => sessionStorage.length);
  await t('indexedDB.open', () => indexedDB.open('x'));
  await t('parent.document', () => parent.document.title);
  await t('top.location = app hijack', () => { top.location.href = 'http://app.localhost:5101/hijack'; return 'navigated?'; });
  await t('window.open', () => { const w = window.open('http://example.com'); if (!w) throw { name: 'null window' }; return 'opened'; });
  await t('fetch app /secret with credentials', async () => (await fetch('http://app.localhost:5101/secret', { credentials: 'include' })).text());
  await t('fetch external (connect-src)', async () => (await fetch('http://example.com/')).status);
  await t('XHR own api.json (connect-src none)', () => new Promise((res, rej) => { const x = new XMLHttpRequest(); x.open('GET', 'api.json'); x.onload = () => res(x.status); x.onerror = () => rej({ name: 'network error' }); x.send(); }));
  await t('eval()', () => eval('1+1'));
  await t('new Function()', () => new Function('return 2')());
  await t('inline script (this page runs one)', () => 'runs');
  await t('external image beacon', () => new Promise((res, rej) => { const i = new Image(); i.onload = () => res('loaded'); i.onerror = () => rej({ name: 'img blocked' }); i.src = 'http://example.com/x.png'; }));
  await t('form submit to external', () => { const f = document.createElement('form'); f.action = 'http://example.com/'; f.method = 'post'; document.body.appendChild(f); f.submit(); return 'submitted?'; });
  // Forged window-level message to the parent: must be ignored (only the private port is trusted).
  await t('forged window.postMessage(commit) to parent', () => { parent.postMessage({ type: 'scorm.commit', data: { 'cmi.core.lesson_status': 'passed' } }, '*'); return 'sent, expect ignored'; });
  // Legit SCORM traffic through the shim, then abusive traffic over the same port.
  API.LMSSetValue('cmi.core.lesson_status', 'completed'); API.LMSSetValue('cmi.core.score.raw', '80'); API.LMSCommit();
  const p = __rawPort();
  p.postMessage({ type: 'scorm.commit', data: { 'cmi.core.student_name': 'x' } });
  p.postMessage({ type: 'scorm.commit', data: { 'cmi.suspend_data': 'A'.repeat(5000) } });
  p.postMessage({ type: 'evil', data: {} });
  for (let i = 0; i < 15; i++) p.postMessage({ type: 'scorm.commit', data: { 'cmi.core.score.raw': String(i) } });
  results.cspViolations = violations;
  __sendProbe(results);
})();
</script>
""";
}
