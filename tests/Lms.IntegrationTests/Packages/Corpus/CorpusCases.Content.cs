using System.Text;
using static Lms.IntegrationTests.Packages.Corpus.CorpusReasons;
using static Lms.IntegrationTests.Packages.Corpus.Pkg;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>Launch pages for the prelude injection (S10, Task 9b/10) and for the sandbox probes (S2/S3), plus the H5P cases. All pass validation; the behaviour is exercised in a browser or by the injector.</summary>
public static partial class CorpusCases
{
    private static CorpusCase Page(string name, string intent, byte[] launch, Func<byte[], bool> has, string? notes = null) =>
        Accept(name, intent, () => Pack([RawZipWriter.File("imsmanifest.xml", Manifest12()), RawZipWriter.File("index.html", launch)]), v => has(v.ReadData(v.Find("index.html"))), notes, CorpusKind.Runtime);

    private static CorpusCase Script(string name, string intent, string script, string? notes = null) =>
        Page(name, intent, Utf8($"<!doctype html><html><head><meta charset=\"utf-8\"></head><body><pre id=\"out\"></pre><script>{script}</script></body></html>"), b => Encoding.UTF8.GetString(b).Contains(script, StringComparison.Ordinal), notes);

    private static bool Text(byte[] b, string s) => Encoding.UTF8.GetString(b).Contains(s, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<CorpusCase> Injection()
    {
        const string Body = "<body><script>var a=window.API;</script><p>page</p></body></html>";
        yield return Page("html_utf8_bom", "A launch page with a UTF-8 BOM: the injector must insert after the BOM, not before it.", [.. Encoding.UTF8.GetPreamble(), .. Utf8("<!doctype html><html><head><title>t</title></head>" + Body)], b => b[0] == 0xEF);
        yield return Page("html_meta_charset_latin1", "A page declared as windows-1252 with a non-ASCII byte (0xE9): re-encoding through UTF-8 would corrupt it.", [.. Encoding.Latin1.GetBytes("<!doctype html><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1252\"><title>café</title></head>" + Body)], b => b.Contains((byte)0xE9));
        yield return Page("html_utf16le_bom", "A UTF-16 LE page with a BOM: decode, inject, re-encode as declared.", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<!doctype html><html><head><title>t</title></head>" + Body)], b => b[0] == 0xFF && b[1] == 0xFE);
        yield return Page("html_utf16be_bom", "A UTF-16 BE page with a BOM.", [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("<!doctype html><html><head><title>t</title></head>" + Body)], b => b[0] == 0xFE && b[1] == 0xFF);
        yield return Page("html_no_head", "A page with <html> and <body> but no <head>.", Utf8("<!doctype html><html>" + Body), b => !Text(b, "<head"));
        yield return Page("html_no_html_no_head", "A page that is a bare fragment (no <html>, no <head>).", Utf8("<p>fragment</p><script>var a=window.API;</script>"), b => !Text(b, "<html"));
        yield return Page("html_doctype_and_comments_first", "A doctype and comments (one containing the text <head>) before <html>.", Utf8("<!-- <head> decoy -->\n<!doctype html>\n<!-- another -->\n<html><head><title>t</title></head>" + Body), b => Text(b, "<!-- <head> decoy -->"));
        yield return Page("html_uppercase_and_attributes", "<HEAD profile=\"x\"> in upper case with an attribute.", Utf8("<!DOCTYPE HTML><HTML><HEAD profile=\"x\"><TITLE>t</TITLE></HEAD>" + Body), b => Text(b, "<HEAD profile"));
        yield return Page("html_head_text_in_script", "The string \"<head>\" inside a script before the real <head> closes.", Utf8("<!doctype html><html><head><script>var s='<head>';</script><title>t</title></head>" + Body), b => Text(b, "var s='<head>'"));
        yield return Page("html_conditional_comment", "An IE conditional comment before <head>.", Utf8("<!doctype html><!--[if IE]><html class=\"ie\"><![endif]--><html><head><title>t</title></head>" + Body), b => Text(b, "[if IE]"));

        yield return Accept(
            "scorm_multipage_sco",
            "A SCO of three pages linked to each other (page1 → page2 → page3): the API must be available on every page, with the state kept across navigations.",
            () => Pack(Scorm12().Append(RawZipWriter.File("page2.html", Sco.Replace("Hello", "Page 2 <a href=\"page3.html\">next</a>"))).Append(RawZipWriter.File("page3.html", Sco.Replace("Hello", "Page 3")))),
            v => AnyName(v, n => n == "page2.html"),
            "Every HTML response of the package gets the prelude, not only the launch page.",
            CorpusKind.Runtime);

        yield return Script("sco_defines_its_own_window_api", "The SCO defines window.API itself (an offline-mode stub) before looking for one.", "window.API={LMSInitialize:function(){return 'true'},LMSGetValue:function(){return ''},LMSSetValue:function(){return 'true'},LMSCommit:function(){return 'true'},LMSFinish:function(){return 'true'}};");
        yield return Script("sco_catches_the_parent_walk_error", "The SCO walks window.parent for the API and catches the SecurityError (opaque origin) instead of crashing.", "try{var p=window.parent.API;}catch(e){document.getElementById('out').textContent='blocked:'+e.name}");
        yield return Script("sco_reads_initial_state_synchronously", "The SCO reads cmi.core.lesson_status right after LMSInitialize with no port handshake yet.", "var a=window.API;a&&a.LMSInitialize('');var s=a&&a.LMSGetValue('cmi.core.lesson_status');");
    }

    private static IEnumerable<CorpusCase> Sandbox()
    {
        yield return Script("sco_touches_storage", "localStorage, sessionStorage, document.cookie and indexedDB inside the sandbox (S2 b): each must throw or be a stand-in, never reach the app's data.", "var r={};['localStorage','sessionStorage','cookie','indexedDB'].forEach(function(k){try{r[k]=k=='cookie'?document.cookie:String(window[k])}catch(e){r[k]=e.name}});document.getElementById('out').textContent=JSON.stringify(r);");
        yield return Script("sco_xhr_own_host", "An XHR to a file of its own package (S2 c): needs connect-src 'self' and CORS from an opaque origin.", "var x=new XMLHttpRequest();x.open('GET','data.json');x.onload=function(){document.getElementById('out').textContent=x.status};x.onerror=function(){document.getElementById('out').textContent='blocked'};x.send();");
        yield return Script("sco_fetch_outside", "fetch() to an outside URL and to the app origin with credentials (must fail: connect-src 'self', no cookies).", "fetch('http://127.0.0.1:9/outside',{credentials:'include'}).then(function(){document.getElementById('out').textContent='reached'},function(){document.getElementById('out').textContent='blocked'});");
        yield return Script("sco_navigates_top", "top.location = an outside URL (sandbox without allow-top-navigation).", "try{top.location='http://127.0.0.1:9/top'}catch(e){document.getElementById('out').textContent=e.name}");
        yield return Script("sco_navigates_itself_outside", "location.href = an outside URL: the frame's own navigation, blocked by the app's frame-src.", "location.href='http://127.0.0.1:9/self';");
        yield return Page("sco_meta_refresh_outside", "<meta http-equiv=refresh> to an outside URL: a navigation with no script, also blocked by frame-src.", Utf8("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"0;url=http://127.0.0.1:9/meta\"></head><body>x</body></html>"), b => Text(b, "http-equiv=\"refresh\""));
        yield return Script("sco_opens_popup", "window.open() (sandbox without allow-popups).", "var w=window.open('http://127.0.0.1:9/popup');document.getElementById('out').textContent=String(w);");
        yield return Page("sco_submits_form", "A form that auto-submits to an outside URL (form-action 'none'; allow-forms is dropped).", Utf8("<!doctype html><html><body><form id=\"f\" action=\"http://127.0.0.1:9/form\" method=\"post\"><input name=\"x\" value=\"1\"></form><script>document.getElementById('f').submit()</script></body></html>"), b => Text(b, "form-action") || Text(b, "id=\"f\""));
        yield return Script("sco_forges_postmessage", "Posts forged commit messages to the parent window (the app must trust only the private port).", "parent.postMessage({type:'scorm.commit',key:'cmi.core.score.raw',value:'100'},'*');");
        yield return Script("sco_uses_eval", "eval() and new Function(): needs 'unsafe-eval' (S3), blocked by the strict CSP.", "try{document.getElementById('out').textContent=String(eval('1+1'))+String(new Function('return 2')())}catch(e){document.getElementById('out').textContent=e.name}");
        yield return Script("sco_inline_handler_and_blob_worker", "An inline event handler and a blob: worker (script-src 'unsafe-inline', blob: workers).", "var b=new Blob(['postMessage(1)']);try{new Worker(URL.createObjectURL(b))}catch(e){document.getElementById('out').textContent=e.name}");
        yield return Script("sco_external_font_and_script", "A web font and a script from a CDN (must be blocked: no external origins).", "var s=document.createElement('script');s.src='http://127.0.0.1:9/cdn.js';document.head.appendChild(s);");
    }

    private static IEnumerable<CorpusCase> H5p()
    {
        const string H5pJson = "{\"title\":\"Corpus\",\"language\":\"en\",\"mainLibrary\":\"H5P.Foo\",\"embedTypes\":[\"iframe\"],\"preloadedDependencies\":[{\"machineName\":\"H5P.Foo\",\"majorVersion\":1,\"minorVersion\":0}]}";
        RawEntry[] Valid() =>
        [
            RawZipWriter.File("h5p.json", H5pJson),
            RawZipWriter.File("content/content.json", "{}"),
            RawZipWriter.File("H5P.Foo-1.0/library.json", "{\"title\":\"Foo\",\"machineName\":\"H5P.Foo\",\"majorVersion\":1,\"minorVersion\":0,\"runnable\":1}"),
        ];

        yield return Accept("h5p_valid_minimal", "A structurally valid H5P file (h5p.json, content.json, one library).", () => Pack(Valid()), v => AnyName(v, n => n == "h5p.json"));
        yield return Reject("h5p_missing_json", H5pInvalid, "A package with content/ and a library but no h5p.json.", () => Pack(Valid().Where(e => e.Name != "h5p.json")), v => AnyName(v, n => n == "content/content.json"));
        yield return Reject("h5p_json_not_json", H5pInvalid, "h5p.json is not JSON.", () => Pack(Valid().Select(e => e.Name == "h5p.json" ? RawZipWriter.File("h5p.json", "<<not json>>") : e)), v => AnyName(v, n => n == "h5p.json"));
        yield return Reject("h5p_json_deeply_nested", H5pInvalid, "content.json nested 200 levels deep (depth cap 32).", () => Pack(Valid().Select(e => e.Name == "content/content.json" ? RawZipWriter.File(e.Name, Json(200)) : e)), v => v.Find("content/content.json").UncompressedSize > 1000);
        yield return Reject("h5p_json_huge", H5pInvalid, "content.json of 2 MB (cap 1 MB).", () => Pack(Valid().Select(e => e.Name == "content/content.json" ? RawZipWriter.File(e.Name, "{\"a\":\"" + new string('x', 2 * 1024 * 1024) + "\"}") : e)), v => v.Find("content/content.json").UncompressedSize > 2_000_000);
        yield return Reject("h5p_main_library_traversal", H5pInvalid, "mainLibrary and a dependency machineName that are paths (../../x).", () => Pack(Valid().Select(e => e.Name == "h5p.json" ? RawZipWriter.File(e.Name, H5pJson.Replace("\"mainLibrary\":\"H5P.Foo\"", "\"mainLibrary\":\"../../x\"")) : e)), v => AnyName(v, n => n == "h5p.json"));
        yield return Reject("h5p_library_with_php", Executable, "A server-side script inside a library folder.", () => Pack(Valid().Append(RawZipWriter.File("H5P.Foo-1.0/upgrade.php", "<?php echo 1;"))), v => AnyName(v, n => n.EndsWith(".php", StringComparison.Ordinal)));
    }
}
