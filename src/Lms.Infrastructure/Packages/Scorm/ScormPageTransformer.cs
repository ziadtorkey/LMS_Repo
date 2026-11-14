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

        // The attempt of the token (never a preview, which has none) gives the current data model; the page continues the server's sequence.
        var model = new Dictionary<string, string>();
        long lastSeq = -1;
        if (context.AttemptId is { } attemptId && !context.Preview)
        {
            var attempt = await db.Set<PackageAttempt>().FirstOrDefaultAsync(
                a => a.Id == attemptId && a.PackageId == context.PackageId && a.OrganizationId == context.OrganizationId, ct);
            if (attempt is not null)
            {
                model = attempt.Model;
                lastSeq = attempt.LastSeq;
            }
        }

        var data = ScormInitialDataBlock.Build(version, model, lastSeq >= 0 ? "resume" : "ab-initio", lastSeq);
        return ScormPreludeInjector.InjectIfHtml(
            relativePath, html, context.PathPrefix + "_lms/" + asset.Name, data, context.PathPrefix + "_lms/port-shim.js");
    }
}
