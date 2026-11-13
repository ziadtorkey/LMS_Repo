using System.Reflection;
using System.Security.Cryptography;
using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Packages.Scorm;

/// <summary>The SCORM prelude script as an application asset of the content host: <c>_lms/scorm-prelude.{hash}.js</c>, the name carrying the content hash.</summary>
public sealed class ScormPreludeAsset : IPackageHostAsset
{
    public ScormPreludeAsset()
    {
        using var stream = typeof(ScormPreludeAsset).Assembly.GetManifestResourceStream("Lms.Infrastructure.Packages.Scorm.scorm-prelude.js")
                           ?? throw new InvalidOperationException("The SCORM prelude script is not embedded.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Content = buffer.ToArray();
        Hash = Convert.ToHexString(SHA256.HashData(Content))[..16].ToLowerInvariant();
        Name = $"scorm-prelude.{Hash}.js";
    }

    public string Name { get; }

    public byte[] Content { get; }

    public string ContentType => "text/javascript; charset=utf-8";

    public string Hash { get; }
}

/// <summary>
/// The SCORM side of the content host's page seam (M4b Task 10): every HTML page of a SCORM package gets the prelude script tag (an absolute
/// path under the token prefix) and the data block as the first children of head. Until the attempts of Task 9b exist the data block is the
/// empty first-launch model of the package's SCORM version.
/// </summary>
public sealed class ScormPageTransformer(LmsDbContext db, ScormPreludeAsset asset) : IPackagePageTransformer
{
    public string Kind => PackageKinds.Scorm;

    public async Task<byte[]> TransformAsync(PackagePageContext context, string relativePath, byte[] html, CancellationToken ct)
    {
        var version = await db.Set<PackageUpload>()
            .Where(p => p.Id == context.PackageId && p.OrganizationId == context.OrganizationId)
            .Select(p => p.Version).FirstOrDefaultAsync(ct) ?? ScormVersions.Scorm12;
        var data = ScormInitialDataBlock.Build(version, new Dictionary<string, string>(), "ab-initio", 0);
        return ScormPreludeInjector.InjectIfHtml(relativePath, html, context.PathPrefix + "_lms/" + asset.Name, data);
    }
}
