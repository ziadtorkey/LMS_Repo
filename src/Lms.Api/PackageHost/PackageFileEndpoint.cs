using Lms.Domain.Abstractions;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.PackageHost;

/// <summary>
/// <c>GET /p/{token}/{**path}</c> on the content host (ADR 0005, plan Task 9a). The token is verified before anything is read; the
/// package is read under the token's own organization and unit (plan site 7); the store prefix is built from the token's two ids only
/// and the requested path is validated component by component, so a request cannot leave its package. Every failure is the same empty
/// 404. The server assigns the content type from its allow-list; anything outside it, and any SVG opened as a page, is a download.
/// </summary>
internal static class PackageFileEndpoint
{
    public const string Pattern = "/p/{token}/{**path}";

    public static async Task<IResult> HandleAsync(
        string token, string? path, HttpContext http, PackageTokenService tokens, PackageSystemContexts contexts,
        PackageRevocationCache revocations, IPackageStore store, IEnumerable<IPackagePageTransformer> transformers, IClock clock,
        CancellationToken ct)
    {
        var claims = tokens.Verify(token, clock.UtcNow);
        if (claims is null || !PackagePathRules.TryNormalise(path, out var relative))
        {
            return NotFound;
        }

        string? kind;
        await using (var db = contexts.Create(claims))
        {
            kind = await db.Set<PackageUpload>().Where(p => p.Id == claims.Package && p.Status == PackageUpload.ReadyStatus)
                .Select(p => p.Kind).FirstOrDefaultAsync(ct);
        }

        if (kind is null)
        {
            return NotFound;
        }

        if (await revocations.IsRevokedAsync(claims, ct))
        {
            return NotFound;
        }

        var file = await store.OpenAsync(IPackageStore.PrefixOf(claims.Org, claims.Package), relative, ct);
        if (file is null)
        {
            return NotFound;
        }

        var type = PackageContentTypes.For(relative);
        var asPage = string.Equals(http.Request.Headers["Sec-Fetch-Dest"], "document", StringComparison.OrdinalIgnoreCase);
        var download = !type.Inline || (asPage && type.Type.StartsWith("image/svg", StringComparison.Ordinal));
        if (download)
        {
            return Results.File(file.Content, PackageContentTypes.Fallback, Path.GetFileName(relative));
        }

        // The seam of the format adapters: HTML of a package whose kind has a transformer (small enough to hold in memory) is rewritten before it is sent.
        var transformer = type.Type.StartsWith("text/html", StringComparison.Ordinal) && file.Length <= IPackagePageTransformer.MaxBytes
            ? transformers.FirstOrDefault(t => t.Kind == kind)
            : null;
        if (transformer is null)
        {
            return Results.Stream(file.Content, type.Type);
        }

        await using (file.Content)
        {
            using var buffer = new MemoryStream();
            await file.Content.CopyToAsync(buffer, ct);
            var context = new PackagePageContext(claims.Org, claims.Package, claims.Student, claims.Attempt, claims.Preview);
            return Results.Bytes(await transformer.TransformAsync(context, relative, buffer.ToArray(), ct), type.Type);
        }
    }

    /// <summary>The one answer of every failure: forged, expired, foreign, revoked, missing or malformed alike.</summary>
    private static IResult NotFound => Results.StatusCode(StatusCodes.Status404NotFound);
}

/// <summary>The rules a requested package path must pass before it becomes a store key.</summary>
internal static class PackagePathRules
{
    private const int MaxLength = 1024;
    private const int MaxDepth = 20;
    private const int MaxSegment = 255;

    public static bool TryNormalise(string? raw, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength || raw[0] == '/' || raw[^1] == '/')
        {
            return false;
        }

        var segments = raw.Split('/');
        if (segments.Length > MaxDepth)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length is 0 or > MaxSegment || segment is "." or ".." || segment.Any(IsForbidden))
            {
                return false;
            }
        }

        relative = raw;
        return true;
    }

    private static bool IsForbidden(char c) => c is '\\' or '\0' or ':' || char.IsControl(c) || ZipNameRules.IsHidden(c);
}
