using System.Text;
using System.Text.Json;
using Jint;
using Lms.Api.PackageHost;
using Lms.Shared.Packages;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>
/// Runs <c>package-port-shim.js</c> for real in an in-process JavaScript engine (Jint) with a hand-made frame: a <c>window</c> whose
/// <c>message</c> listeners the test fires, a port that records what it is sent, and traps on everything that could hold the learner
/// or reach the network. It pins the handshake, the queue before the port, the seq order and the limits of the page's
/// <c>package-bridge.js</c> by behaviour, not by looking at the text (M4b Task 9b, #576 security review).
/// </summary>
public sealed class PackageShimRuntimeTests
{
    private const string Harness = """
        var __listeners = [];
        var __touched = [];
        var __sent = [];
        var __controls = [];
        var window = {
            parent: { name: 'parent' },
            addEventListener: function (type, fn) { if (type === 'message') { __listeners.push(fn); } else { __touched.push('listener:' + type); } },
        };
        // The frame's own origin is opaque: nothing that could hold the learner or reach the network may be touched.
        ['document', 'localStorage', 'sessionStorage', 'indexedDB', 'navigator', 'location', 'fetch', 'XMLHttpRequest', 'WebSocket', 'caches', 'cookieStore']
            .forEach(function (name) { Object.defineProperty(globalThis, name, { get: function () { __touched.push(name); return undefined; } }); });
        function TextEncoder() {}
        TextEncoder.prototype.encode = function (text) { return { length: __utf8(text) }; };
        function __port() {
            var port = { messages: [], onmessage: null, postMessage: function (m) { this.messages.push(m); __sent.push(m); }, closed: false };
            return port;
        }
        function __handshake(data, source, ports) {
            __listeners.forEach(function (fn) { fn({ data: data, source: source === undefined ? window.parent : source, ports: ports }); });
        }
        """;

    private static Engine NewEngine()
    {
        var engine = new Engine();
        engine.SetValue("__utf8", new Func<string, int>(s => Encoding.UTF8.GetByteCount(s)));
        engine.Execute(Harness);
        engine.Execute(PackageShimEndpoint.Source);
        return engine;
    }

    private static JsonElement Json(Engine engine, string expression)
    {
        using var doc = JsonDocument.Parse(engine.Evaluate($"JSON.stringify({expression})").AsString());
        return doc.RootElement.Clone();
    }

    private static void Handshake(Engine engine, string port = "p") =>
        engine.Execute($"var {port} = __port(); __handshake({{ lms: 'package-port', v: 1 }}, undefined, [{port}]);");

    [Fact]
    public void The_handshake_needs_the_parent_the_marker_the_version_and_a_port_and_only_the_first_one_counts()
    {
        var engine = NewEngine();
        engine.Execute("var p = __port(); var q = __port();");
        engine.Execute("lmsPort = window.lmsPort;"); // exists once the script ran
        Assert.True(engine.Evaluate("window.lmsPort.commit('Scorm', { a: '1' })").AsBoolean()); // queued

        engine.Execute("__handshake({ lms: 'package-port', v: 1 }, { name: 'other-frame' }, [p]);"); // not the parent
        engine.Execute("__handshake({ type: 'lms-port' }, undefined, [p]);"); // wrong marker
        engine.Execute("__handshake({ lms: 'package-port', v: 2 }, undefined, [p]);"); // wrong version
        engine.Execute("__handshake({ lms: 'package-port', v: 1 }, undefined, []);"); // no port
        engine.Execute("__handshake(null, undefined, [p]);");
        Assert.Equal(0, (int)engine.Evaluate("p.messages.length").AsNumber()); // nothing was handed over

        engine.Execute("__handshake({ lms: 'package-port', v: 1 }, undefined, [p]);");
        engine.Execute("__handshake({ lms: 'package-port', v: 1 }, undefined, [q]);"); // a second handshake is ignored
        engine.Execute("window.lmsPort.commit('Scorm', { b: '2' });");

        Assert.Equal(2, (int)engine.Evaluate("p.messages.length").AsNumber());
        Assert.Equal(0, (int)engine.Evaluate("q.messages.length").AsNumber());
    }

    [Fact]
    public void Messages_made_before_the_port_wait_and_are_delivered_in_order_when_it_arrives()
    {
        var engine = NewEngine();
        engine.Execute("window.lmsPort.commit('Scorm', { 'cmi.core.lesson_status': 'incomplete' }); window.lmsPort.commit('Scorm', { 'cmi.suspend_data': 'abc' });");
        Assert.Equal(0, (int)engine.Evaluate("__sent.length").AsNumber());

        Handshake(engine);

        var sent = Json(engine, "__sent");
        Assert.Equal(2, sent.GetArrayLength());
        Assert.Equal("incomplete", sent[0].GetProperty("data").GetProperty("cmi.core.lesson_status").GetString());
        Assert.Equal("abc", sent[1].GetProperty("data").GetProperty("cmi.suspend_data").GetString());
        engine.Execute("window.lmsPort.commit('Scorm', { c: '3' });");
        Assert.Equal(3, (int)engine.Evaluate("__sent.length").AsNumber()); // after the handshake messages go straight out
    }

    [Fact]
    public void Messages_have_the_shape_of_the_bridge_and_seq_rises_by_one_across_commit_and_finish()
    {
        var engine = NewEngine();
        Handshake(engine);

        engine.Execute("window.lmsPort.commit('Scorm', { k: 'v' }); window.lmsPort.commit('Xapi', { verb: 'completed' }); window.lmsPort.commit(undefined, undefined); window.lmsPort.finish('Scorm', { z: '1' });");
        var sent = Json(engine, "__sent");

        Assert.Equal([0, 1, 2, 3], sent.EnumerateArray().Select(m => m.GetProperty("seq").GetInt32()).ToArray());
        Assert.Equal(["commit", "commit", "commit", "finish"], sent.EnumerateArray().Select(m => m.GetProperty("type").GetString()!).ToArray());
        Assert.Equal([PackageCommitFormats.Scorm, PackageCommitFormats.Xapi, PackageCommitFormats.Scorm, PackageCommitFormats.Scorm],
            sent.EnumerateArray().Select(m => m.GetProperty("format").GetString()!).ToArray());
        Assert.All(sent.EnumerateArray(), m => Assert.Equal(["data", "format", "seq", "type"], m.EnumerateObject().Select(p => p.Name).Order().ToArray()));
        Assert.Empty(sent[2].GetProperty("data").EnumerateObject()); // an empty commit is a valid message
        // After finish nothing more is accepted or sent.
        Assert.False(engine.Evaluate("window.lmsPort.commit('Scorm', { late: '1' })").AsBoolean());
        Assert.False(engine.Evaluate("window.lmsPort.finish('Scorm', {})").AsBoolean());
        Assert.Equal(4, (int)engine.Evaluate("__sent.length").AsNumber());
    }

    [Fact]
    public void The_limits_are_the_contract_limits_by_bytes_and_a_refused_message_uses_no_seq()
    {
        var engine = NewEngine();
        Handshake(engine);
        var atLimit = new string('a', PackageTrackingLimits.MaxValueBytes);
        var overLimit = new string('a', PackageTrackingLimits.MaxValueBytes + 1);
        var threeByte = string.Concat(Enumerable.Repeat("€", PackageTrackingLimits.MaxValueBytes / 3 + 1)); // 3 bytes per character: over 4 KB in bytes, under it in characters

        Assert.True(engine.Evaluate($"window.lmsPort.commit('Scorm', {{ k: '{atLimit}' }})").AsBoolean());
        Assert.False(engine.Evaluate($"window.lmsPort.commit('Scorm', {{ k: '{overLimit}' }})").AsBoolean());
        Assert.False(engine.Evaluate($"window.lmsPort.commit('Scorm', {{ k: '{threeByte}' }})").AsBoolean());
        Assert.False(engine.Evaluate("window.lmsPort.commit('Scorm', { k: 5 })").AsBoolean()); // not a string
        Assert.False(engine.Evaluate("window.lmsPort.commit('Scorm', { k: { nested: '1' } })").AsBoolean());
        Assert.False(engine.Evaluate("window.lmsPort.commit('Scorm', ['x'])").AsBoolean());

        // More keys than the page accepts.
        Assert.True(engine.Evaluate("var d = {}; for (var i = 0; i < 256; i++) { d['k' + i] = 'v'; } window.lmsPort.commit('Scorm', d)").AsBoolean());
        Assert.False(engine.Evaluate("var e = {}; for (var i = 0; i < 257; i++) { e['k' + i] = 'v'; } window.lmsPort.commit('Scorm', e)").AsBoolean());

        // The whole message may not pass 8 KB even when every value fits.
        Assert.False(engine.Evaluate($"window.lmsPort.commit('Scorm', {{ a: '{atLimit}', b: '{atLimit}', c: '{atLimit}' }})").AsBoolean());

        // Only the accepted messages were sent, with unbroken seq (a refusal consumes none).
        var seqs = Json(engine, "__sent.map(function (m) { return m.seq; })").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        Assert.Equal(Enumerable.Range(0, seqs.Length).ToArray(), seqs);
        Assert.Equal(2, seqs.Length);
    }

    [Fact]
    public void The_shim_reads_nothing_about_the_learner_and_touches_no_storage_cookie_or_network()
    {
        var engine = NewEngine();

        Handshake(engine);
        engine.Execute("window.lmsPort.commit('Scorm', { a: '1' }); window.lmsPort.finish('Scorm', { b: '2' });");
        engine.Execute("window.lmsPort.onControl(function (name) { __controls.push(name); }); p.onmessage({ data: { control: 'pause' } }); p.onmessage({ data: { control: 5 } }); p.onmessage({ data: null });");

        Assert.Equal("[]", engine.Evaluate("JSON.stringify(__touched)").AsString()); // no document, cookie, storage, location, fetch or XHR
        Assert.Equal("[\"pause\"]", engine.Evaluate("JSON.stringify(__controls)").AsString()); // only a string control message reaches the content
        // The only things the shim registered or exposed.
        Assert.Equal(1, (int)engine.Evaluate("__listeners.length").AsNumber());
        Assert.Equal("[\"commit\",\"finish\",\"onControl\",\"resume\"]", engine.Evaluate("JSON.stringify(Object.keys(window.lmsPort).sort())").AsString());
        Assert.True(engine.Evaluate("Object.isFrozen(window.lmsPort)").AsBoolean());
        // Nothing in the messages carries anything but what the content passed in.
        Assert.DoesNotContain("parent", engine.Evaluate("JSON.stringify(__sent)").AsString());
    }

    [Fact]
    public void Loading_the_shim_twice_keeps_the_first_port_object()
    {
        var engine = NewEngine();
        engine.Execute("var first = window.lmsPort;");

        engine.Execute(PackageShimEndpoint.Source);

        Assert.True(engine.Evaluate("window.lmsPort === first").AsBoolean());
        Assert.Equal(1, (int)engine.Evaluate("__listeners.length").AsNumber()); // one listener, not two
    }
}
