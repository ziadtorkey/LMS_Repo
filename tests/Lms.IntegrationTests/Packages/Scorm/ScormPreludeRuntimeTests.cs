using System.Diagnostics;
using System.Text.Json;
using Lms.Infrastructure.Packages.Scorm;

namespace Lms.IntegrationTests.Packages.Scorm;

/// <summary>
/// The prelude script run under Node with a minimal window and document (no browser): the API answers from the data block at once, changes are
/// queued with a sequence number and delivered over the port only after the handshake, and the prelude's object cannot be replaced. The sandbox
/// itself (opaque origin, CSP) is exercised in a real browser by the E2E suite, not here. Skipped where Node is not installed.
/// </summary>
public sealed class ScormPreludeRuntimeTests
{
    private static readonly string Script = new ScormPreludeAsset().Content is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : string.Empty;

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

    /// <summary>Runs <paramref name="scenario"/> (JavaScript that may use window, api12, api2004, port and returns a value) and gives back its JSON result.</summary>
    private static JsonElement Run(string dataJson, string scenario)
    {
        var harness = """
            const posted = [];
            const listeners = {};
            const window = { addEventListener: (t, f) => { listeners[t] = f; } };
            const document = { getElementById: () => ({ textContent: process.env.LMS_DATA }) };
            globalThis.window = window; globalThis.document = document;
            const port = { postMessage: (m) => posted.push(m), onmessage: null };
            const handshake = () => listeners['message']({ data: { type: 'lms-port' }, ports: [port] });
            """
                      + Script
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

    private const string Data12 = "{\"version\":\"Scorm12\",\"entry\":\"resume\",\"seq\":4,\"model\":{\"cmi.core.lesson_status\":\"incomplete\",\"cmi.suspend_data\":\"page=3\"}}";

    [Fact]
    public void The_script_parses()
    {
        if (!HasNode())
        {
            return;
        }

        var result = Run(Data12, "return typeof api12.LMSInitialize + ',' + typeof api2004.Initialize;");

        Assert.Equal("function,function", result.GetString());
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
    public void Commit_before_the_handshake_is_queued_and_delivered_in_seq_order()
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
        Assert.Equal(("commit", 5), (messages[0][0].GetString(), messages[0][1].GetInt32()));
        Assert.Equal("completed", messages[0][3].GetProperty("cmi.core.lesson_status").GetString());
        Assert.Equal(("finish", 6, "Scorm"), (messages[1][0].GetString(), messages[1][1].GetInt32(), messages[1][2].GetString()));
        Assert.Equal("80", messages[1][3].GetProperty("cmi.core.score.raw").GetString());
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

        var result = Run("{\"version\":\"Scorm2004\",\"seq\":0,\"model\":{\"cmi.completion_status\":\"unknown\"}}", """
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
    public void The_asset_name_carries_the_content_hash()
    {
        var asset = new ScormPreludeAsset();

        Assert.Equal($"scorm-prelude.{asset.Hash}.js", asset.Name);
        Assert.Equal(16, asset.Hash.Length);
        Assert.Contains("API_1484_11", Script);
    }
}
