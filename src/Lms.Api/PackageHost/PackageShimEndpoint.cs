using System.Reflection;
using Lms.Domain.Abstractions;

namespace Lms.Api.PackageHost;

/// <summary>
/// <c>GET /p/{token}/_lms/port-shim.js</c> on the content host: the content-side half of the private tracking port (M4b Task 9b). The
/// token is verified like every other host request and a bad one is the same empty 404; the script is the same for every package and
/// holds nothing about the learner.
/// </summary>
internal static class PackageShimEndpoint
{
    public const string Pattern = "/p/{token}/_lms/port-shim.js";

    private const string ResourceName = "Lms.Api.PackageHost.package-port-shim.js";

    private static readonly Lazy<string> Script = new(() =>
    {
        using var stream = typeof(PackageShimEndpoint).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException("The port shim is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static IResult Handle(string token, PackageTokenService tokens, IClock clock) =>
        tokens.Verify(token, clock.UtcNow) is null
            ? Results.StatusCode(StatusCodes.Status404NotFound)
            : Results.Text(Script.Value, "text/javascript; charset=utf-8");

    /// <summary>The script text (for tests that pin its message shape and limits).</summary>
    public static string Source => Script.Value;
}
