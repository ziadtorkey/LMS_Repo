using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lms.Infrastructure.Packages.Scorm;

namespace Lms.IntegrationTests.Packages.Scorm;

/// <summary>
/// The prelude run under Node together with the real content-side port shim (<c>package-port-shim.js</c>, BE1) and the real handshake message of the
/// app bridge (<c>{ lms: 'package-port', v: 1 }</c> with the port, sent by the parent window), so the two sides cannot drift apart again. The sandbox
/// itself (opaque origin, CSP) is exercised in a real browser by the E2E suite. Skipped where Node is not installed.
/// </summary>
public sealed class ScormPreludeRuntimeTests
{
    private static readonly string Prelude = Encoding.UTF8.GetString(new ScormPreludeAsset().Content);

    private static readonly string Shim = File.ReadAllText(ShimPath());

    private static string ShimPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Lms.Api", "PackageHost", "package-port-shim.js")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new FileNotFoundException("package-port-shim.js") : Path.Combine(dir.FullName, "src", "Lms.Api", "PackageHost", "package-port-shim.js");
    }

    private static bool HasNode()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            p!.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Runs <paramref name="scenario"/> (JavaScript with window, api12, api2004, port, handshake() and posted in scope that returns a value) and gives back its JSON result.</summary>
    private static JsonElement Run(string dataJson, string scenario)
    {
        var harness = """
            const posted = [];
            const listeners = {};
            const parent = { name: 'app page' };
            const window = { parent, addEventListener: (t, f) => { listeners[t] = f; } };
            const document = { getElementById: () => ({ textContent: process.env.LMS_DATA }) };
            globalThis.window = window; globalThis.document = document;
            const port = { postMessage: (m) => posted.push(m), onmessage: null };
            // The exact message the app bridge sends: the parent window posts it with the port transferred.
            const bridgeMessage = () => ({ source: parent, data: { lms: 'package-port', v: 1 }, ports: [port] });
            const handshake = () => listeners['message'](bridgeMessage());
            """
                      + Shim
                      + "\n"
                      + Prelude
                      + """

            const api12 = window.API, api2004 = window.API_1484_11;
            const result = (function () {
            """
                      + scenario
                      + """

            })();
            console.log(JSON.stringify(result));
            """;
        var info = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-");
        info.Environment["LMS_DATA"] = dataJson;
        using var process = Process.Start(info)!;
        process.StandardInput.Write(harness);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        Assert.True(process.ExitCode == 0, error);
        return JsonDocument.Parse(output).RootElement;
    }

    private const string Data12 = "{\"version\":\"Scorm12\",\"entry\":\"resume\",\"seq\":-1,\"model\":{\"cmi.core.lesson_status\":\"incomplete\",\"cmi.suspend_data\":\"page=3\"}}";

    [Fact]
    public void The_scripts_parse_and_define_both_apis()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, "return typeof api12.LMSInitialize + ',' + typeof api2004.Initialize + ',' + typeof window.lmsPort.commit;");

        Assert.Equal("function,function,function", result.GetString());
    }

    [Fact]
    public void Initial_state_is_available_at_initialize_with_no_port()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, "var i = api12.LMSInitialize(''); return [i, api12.LMSGetValue('cmi.core.lesson_status'), api12.LMSGetValue('cmi.suspend_data'), api12.LMSGetValue('cmi.unknown'), api12.LMSGetLastError()];");

        Assert.Equal(["true", "incomplete", "page=3", "", "0"], result.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Commit_before_the_handshake_is_queued_and_delivered_in_seq_order_after_the_real_bridge_message()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, """
            api12.LMSInitialize('');
            api12.LMSSetValue('cmi.core.lesson_status', 'completed');
            api12.LMSCommit('');
            api12.LMSSetValue('cmi.core.score.raw', '80');
            api12.LMSFinish('');
            const before = posted.length;
            handshake();
            return { before: before, after: posted.map(m => [m.type, m.seq, m.format, m.data]) };
            """);

        Assert.Equal(0, result.GetProperty("before").GetInt32());
        var messages = result.GetProperty("after").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal(("commit", 0), (messages[0][0].GetString(), messages[0][1].GetInt32()));
        Assert.Equal("completed", messages[0][3].GetProperty("cmi.core.lesson_status").GetString());
        Assert.Equal(("finish", 1, "Scorm"), (messages[1][0].GetString(), messages[1][1].GetInt32(), messages[1][2].GetString()));
        Assert.Equal("80", messages[1][3].GetProperty("cmi.core.score.raw").GetString());
    }

    [Fact]
    public void A_handshake_of_another_shape_source_or_a_second_port_is_ignored()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, """
            api12.LMSInitialize('');
            api12.LMSSetValue('cmi.core.lesson_status', 'completed');
            api12.LMSCommit('');
            const oldShape = listeners['message']({ source: parent, data: { type: 'lms-port' }, ports: [port] });
            const otherSource = listeners['message']({ source: { name: 'someone else' }, data: { lms: 'package-port', v: 1 }, ports: [port] });
            const wrongVersion = listeners['message']({ source: parent, data: { lms: 'package-port', v: 2 }, ports: [port] });
            const noPort = listeners['message']({ source: parent, data: { lms: 'package-port', v: 1 }, ports: [] });
            const delivered = posted.length;
            handshake();
            const afterReal = posted.length;
            const second = { postMessage: (m) => posted.push(['second', m]), onmessage: null };
            listeners['message']({ source: parent, data: { lms: 'package-port', v: 1 }, ports: [second] });
            api12.LMSSetValue('cmi.core.score.raw', '5');
            api12.LMSCommit('');
            return { delivered, afterReal, finalCount: posted.length, viaSecond: posted.filter(m => Array.isArray(m)).length };
            """);

        Assert.Equal(0, result.GetProperty("delivered").GetInt32());
        Assert.Equal(1, result.GetProperty("afterReal").GetInt32());
        Assert.Equal(2, result.GetProperty("finalCount").GetInt32());
        Assert.Equal(0, result.GetProperty("viaSecond").GetInt32());
    }

    [Fact]
    public void A_resumed_attempt_continues_the_servers_sequence()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run("{\"version\":\"Scorm12\",\"entry\":\"resume\",\"seq\":6,\"model\":{}}", """
            api12.LMSInitialize('');
            api12.LMSSetValue('cmi.core.lesson_status', 'incomplete');
            api12.LMSCommit('');
            api12.LMSFinish('');
            handshake();
            return posted.map(m => [m.type, m.seq]);
            """);

        Assert.Equal([("commit", 7), ("finish", 8)], result.EnumerateArray().Select(m => (m[0].GetString()!, m[1].GetInt32())));
    }

    [Fact]
    public void A_change_set_over_the_message_cap_is_split_into_messages_that_each_fit()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, """
            api12.LMSInitialize('');
            for (let i = 0; i < 6; i++) { api12.LMSSetValue('cmi.suspend_data' + (i === 0 ? '' : '_' + i), 'x'.repeat(3500)); }
            api12.LMSFinish('');
            handshake();
            return posted.map(m => [m.type, m.seq, Object.keys(m.data).length, new TextEncoder().encode(JSON.stringify(m)).length]);
            """);

        var messages = result.EnumerateArray().ToList();
        Assert.True(messages.Count >= 3, "the change set was not split");
        Assert.All(messages, m => Assert.True(m[3].GetInt32() <= 8 * 1024, "a message is over the 8 KB contract"));
        Assert.Equal(Enumerable.Range(0, messages.Count), messages.Select(m => m[1].GetInt32()));
        Assert.Equal("finish", messages[^1][0].GetString());
        Assert.Equal(1, messages.Count(m => m[0].GetString() == "finish"));
    }

    [Fact]
    public void Calls_before_initialize_and_after_finish_fail_with_error_301()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, """
            const early = api12.LMSSetValue('cmi.core.lesson_status', 'completed') + api12.LMSGetLastError();
            api12.LMSInitialize(''); api12.LMSFinish('');
            const late = api12.LMSCommit('') + api12.LMSGetLastError();
            return [early, late];
            """);

        Assert.Equal(["false301", "false301"], result.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Scorm_2004_api_uses_the_2004_function_names()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run("{\"version\":\"Scorm2004\",\"seq\":-1,\"model\":{\"cmi.completion_status\":\"unknown\"}}", """
            return [api2004.Initialize(''), api2004.GetValue('cmi.completion_status'), api2004.SetValue('cmi.success_status', 'passed'), api2004.Terminate('')];
            """);

        Assert.Equal(["true", "unknown", "true", "true"], result.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void A_package_cannot_replace_the_prelude_api()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, """
            const original = window.API;
            try { window.API = { LMSInitialize: () => 'hijacked' }; } catch (e) { }
            return [window.API === original, api12.LMSInitialize('')];
            """);

        Assert.True(result[0].GetBoolean());
        Assert.Equal("true", result[1].GetString());
    }

    [Fact]
    public void A_bad_data_block_falls_back_to_an_empty_model()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run("not json", "api12.LMSInitialize(''); return [api12.LMSGetValue('cmi.core.lesson_status'), api12.LMSGetLastError()];");

        Assert.Equal(["", "0"], result.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void The_asset_name_carries_the_content_hash_and_the_script_uses_the_shared_port_not_its_own_handshake()
    {
        var asset = new ScormPreludeAsset();

        Assert.Equal($"scorm-prelude.{asset.Hash}.js", asset.Name);
        Assert.Equal(16, asset.Hash.Length);
        Assert.Contains("API_1484_11", Prelude);
        Assert.Contains("window.lmsPort", Prelude);
        Assert.DoesNotContain("lms-port", Prelude);
    }
}
