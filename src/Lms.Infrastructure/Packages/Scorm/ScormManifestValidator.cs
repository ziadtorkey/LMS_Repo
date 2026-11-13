using System.Xml.Linq;
using Lms.Domain.Packages;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.Scorm;

/// <summary>
/// The SCORM part of package validation (M4b Task 10, #427 7.2): the manifest is read through <see cref="SafeXml"/> (no resolver, DOCTYPE
/// refused), the version is detected (1.2 or 2004), a single SCO is required (several SCOs or 2004 sequencing rules are <c>multi_sco</c>), the
/// launch <c>href</c> is resolved against the package root only (<see cref="ScormLaunchResolver"/>) and must be a file of the package, and a
/// launch page that is a frameset or nests a frame is <c>needs_nested_frames</c> (Design A has no wrapper frame).
/// </summary>
public sealed class ScormManifestValidator : IPackageValidator
{
    public const string ManifestPath = "imsmanifest.xml";

    public string Kind => PackageKinds.Scorm;

    public Task<PackageValidationResult> ValidateAsync(IPackageContent content, CancellationToken ct)
    {
        if (!content.Exists(ManifestPath))
        {
            return Task.FromResult(PackageValidationResult.Reject(PackageRejectReasons.manifest_invalid));
        }

        SafeXml.Result parsed;
        using (var stream = content.OpenRead(ManifestPath))
        {
            parsed = SafeXml.Parse(stream);
        }

        return Task.FromResult(parsed.Document is null ? PackageValidationResult.Reject(parsed.RejectReason!) : Validate(parsed.Document, content));
    }

    private static PackageValidationResult Validate(XDocument document, IPackageContent content)
    {
        var root = document.Root!;
        if (root.Name.LocalName != "manifest" || Version(root) is not { } version)
        {
            return PackageValidationResult.Reject(PackageRejectReasons.manifest_invalid);
        }

        // Sequencing rules belong to SCORM 2004 simple sequencing, which v1 does not run: refused before anything else is judged.
        if (document.Descendants().Any(e => e.Name.LocalName == "sequencing" || e.Name.LocalName == "sequencingCollection"))
        {
            return PackageValidationResult.Reject(PackageRejectReasons.multi_sco);
        }

        var organization = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "organization");
        var items = organization?.Descendants().Where(e => e.Name.LocalName == "item" && e.Attribute("identifierref") is not null).ToList() ?? [];
        var resources = root.Descendants().Where(e => e.Name.LocalName == "resource").ToList();
        var scos = resources.Where(IsSco).ToList();
        if (items.Count > 1 || scos.Count > 1 || items.Select(i => (string?)i.Attribute("identifierref")).Distinct().Count() > 1)
        {
            return PackageValidationResult.Reject(PackageRejectReasons.multi_sco);
        }

        if (items.Count == 0 || scos.Count == 0)
        {
            return PackageValidationResult.Reject(PackageRejectReasons.manifest_invalid);
        }

        var sco = resources.FirstOrDefault(r => (string?)r.Attribute("identifier") == (string?)items[0].Attribute("identifierref"));
        if (sco is null || !IsSco(sco) || ScormLaunchResolver.Normalize((string?)sco.Attribute("href")) is not { } launch || !content.Exists(launch))
        {
            return PackageValidationResult.Reject(PackageRejectReasons.manifest_invalid);
        }

        if (IsHtml(launch))
        {
            using var stream = content.OpenRead(launch);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (ScormLaunchResolver.UsesFrames(buffer.ToArray()))
            {
                return PackageValidationResult.Reject(PackageRejectReasons.needs_nested_frames);
            }
        }

        var title = items[0].Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim()
                    ?? organization?.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim();
        return new PackageValidationResult(null, string.IsNullOrWhiteSpace(title) ? null : title, version, null, launch);
    }

    private static bool IsHtml(string path) => PackageContentTypes.For(path).Type.StartsWith("text/html", StringComparison.Ordinal);

    /// <summary>A resource whose <c>scormType</c> is <c>sco</c> (the attribute name is lower or camel case between 1.2 and 2004).</summary>
    private static bool IsSco(XElement resource) =>
        resource.Attributes().Any(a => a.Name.LocalName.Equals("scormtype", StringComparison.OrdinalIgnoreCase)
                                       && a.Value.Equals("sco", StringComparison.OrdinalIgnoreCase));

    /// <summary>1.2 or 2004 from the content-packaging namespace, then from the declared schema version; anything else is not a SCORM manifest.</summary>
    private static string? Version(XElement manifest)
    {
        var ns = manifest.Name.NamespaceName;
        var declared = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "schemaversion")?.Value ?? string.Empty;
        if (ns.Contains("imscp_v1p1", StringComparison.OrdinalIgnoreCase) || declared.Contains("2004", StringComparison.Ordinal)
                                                                         || declared.Contains("CAM 1.3", StringComparison.OrdinalIgnoreCase))
        {
            return ScormVersions.Scorm2004;
        }

        return ns.Contains("imscp_rootv1p1p2", StringComparison.OrdinalIgnoreCase) || declared.Contains("1.2", StringComparison.Ordinal)
            ? ScormVersions.Scorm12
            : null;
    }
}
