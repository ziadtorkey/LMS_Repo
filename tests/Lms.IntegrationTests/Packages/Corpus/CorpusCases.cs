using System.Security.Cryptography;
using System.Text.Json;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>
/// The crafted hostile package corpus of the package-hosting ADR (M4b Task 3, S6/S10) and the input of the package kernel tests
/// (Task 8a/8b) and the SCORM runtime tests. The archives are committed under <c>Corpus/files</c>; <see cref="Build"/> regenerates
/// them deterministically (<c>LMS_REGEN_CORPUS=1 dotnet test --filter CorpusFiles</c>) and <c>CorpusTests</c> proves on the raw bytes that
/// each file still contains the defect it exists for.
/// </summary>
public static partial class CorpusCases
{
    public static IReadOnlyList<CorpusCase> All { get; } = [.. Paths(), .. Sizes(), .. Entries(), .. Names(), .. Structure(), .. Manifests(), .. Injection(), .. Sandbox(), .. H5p()];

    /// <summary>The directory of this source file's tests project, found from the build output (the files are copied next to the binaries too).</summary>
    public static string CommittedFile(CorpusCase c) => Path.Combine(AppContext.BaseDirectory, "Packages", "Corpus", "files", c.FileName);

    /// <summary>The machine-readable index written next to the archives (for the ADR, the README and non-.NET consumers).</summary>
    public static string ManifestJson(Func<CorpusCase, byte[]> bytesOf) =>
        JsonSerializer.Serialize(
            All.Select(c =>
            {
                var bytes = bytesOf(c);
                return new
                {
                    name = c.Name,
                    file = c.FileName,
                    kind = c.Kind.ToString().ToLowerInvariant(),
                    expect = c.Expect,
                    intent = c.Intent,
                    notes = c.Notes,
                    bytes = bytes.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                };
            }),
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
