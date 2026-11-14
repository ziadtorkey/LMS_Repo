using System.Net;
using System.Text.RegularExpressions;
using Lms.Api.PackageHost;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Packages;

namespace Lms.IntegrationTests.PackageHost;

/// <summary>
/// The content-side port shim (M4b Task 9b): served by the content host under a valid token only, with the host's headers, and pinned to
/// the message shape and limits of the app page's <c>package-bridge.js</c> (handshake, commit, finish, 8 KB / 4 KB, 256 keys).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PackageShimTests(LmsApiFactory factory) : IDisposable
{
    private readonly PackageHostKit _kit = new(factory);

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task The_shim_is_served_under_a_valid_token_with_the_host_headers_and_a_bad_token_is_404()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PS1");
        var package = await _kit.SeedPackageAsync(tree.Root, tree.School1, ("index.html", "<p>hi</p>"));
        var token = _kit.Token(package);

        var ok = await _kit.Content().GetAsync($"/p/{token}/_lms/port-shim.js");
        var forged = await _kit.Content().GetAsync($"/p/{token}x/_lms/port-shim.js");
        var expired = await _kit.Content().GetAsync($"/p/{_kit.Token(package, lifetime: TimeSpan.FromSeconds(-5))}/_lms/port-shim.js");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("text/javascript; charset=utf-8", ok.Content.Headers.ContentType!.ToString());
        Assert.Contains("sandbox allow-scripts", string.Join(' ', ok.Headers.GetValues("Content-Security-Policy")));
        Assert.False(ok.Headers.Contains("Set-Cookie"));
        Assert.Contains("window.lmsPort", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
        Assert.Empty(await forged.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public void The_shim_uses_the_message_shape_and_the_limits_of_the_page_bridge()
    {
        var js = PackageShimEndpoint.Source;

        // Handshake: { lms: 'package-port', v: 1 } from the parent window, with a port.
        Assert.Contains("d.lms !== 'package-port'", js);
        Assert.Contains("d.v !== 1", js);
        Assert.Contains("event.source !== window.parent", js);
        // Port messages: { type: 'commit' | 'finish', seq, format: 'Scorm' | 'Xapi', data }.
        Assert.Contains("type: type, seq: seq, format: format === 'Xapi' ? 'Xapi' : 'Scorm', data: clean", js);
        Assert.Contains("make('commit'", js);
        Assert.Contains("make('finish'", js);
        Assert.Contains(PackageCommitFormats.Scorm, js);
        Assert.Contains(PackageCommitFormats.Xapi, js);
        // The limits equal the contract's (the page and the server enforce the same numbers).
        Assert.Equal(PackageTrackingLimits.MaxMessageBytes, Const(js, "MAX_MESSAGE_BYTES"));
        Assert.Equal(PackageTrackingLimits.MaxValueBytes, Const(js, "MAX_VALUE_BYTES"));
        Assert.Equal(256, Const(js, "MAX_KEYS"));
        // Nothing about the learner is read and nothing is sent to the network.
        Assert.DoesNotContain("fetch(", js);
        Assert.DoesNotContain("XMLHttpRequest", js);
        Assert.DoesNotContain("document.cookie", js);
        Assert.DoesNotContain("localStorage", js);
    }

    private static int Const(string js, string name)
    {
        var m = Regex.Match(js, $@"var {name} = (\d+)(?: \* (\d+))?;");
        Assert.True(m.Success, name);
        return int.Parse(m.Groups[1].Value) * (m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1);
    }
}
